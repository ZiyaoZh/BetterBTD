using System.Text.Json;
using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.MyScripts;

public sealed class LegacyScriptConversionService
{
    private const string LegacyNotesPrefix = "Legacy:";
    private const int DefaultInstructionIntervalMilliseconds = 50;
    private const string HeroBindingId = "legacy-hero";

    private static readonly Lazy<LegacyScriptConversionService> InstanceHolder = new(() => new LegacyScriptConversionService());

    private LegacyScriptConversionService()
    {
    }

    public static LegacyScriptConversionService Instance => InstanceHolder.Value;

    public LegacyScriptConversionResult Convert(LegacyScriptModel legacyScript)
    {
        ArgumentNullException.ThrowIfNull(legacyScript);

        var context = new LegacyConversionContext(legacyScript);
        context.Convert();

        return new LegacyScriptConversionResult
        {
            Document = context.Document,
            Warnings = context.Warnings
        };
    }

    private sealed class LegacyConversionContext
    {
        private readonly LegacyScriptModel _legacyScript;
        private readonly Dictionary<int, LegacyMonkeyBindingState> _monkeysByLegacyId = [];
        private readonly Dictionary<MonkeyTowerType, int> _towerPlacementCounts = [];
        private readonly List<ScriptMonkeyObjectDocument> _monkeyObjects = [];
        private readonly List<ScriptInstructionDocument> _instructions = [];
        private readonly List<string> _warnings = [];
        private readonly HeroType _heroType;
        private readonly string _heroObjectId;
        private int _placementOrder;

        public LegacyConversionContext(LegacyScriptModel legacyScript)
        {
            _legacyScript = legacyScript;
            _heroType = ResolveHeroType(legacyScript.Metadata.SelectedHero, _warnings);
            _heroObjectId = ScriptEditorInstructionService.BuildHeroObjectKey(_heroType);
        }

        public ScriptDocument Document { get; private set; } = new();

        public List<string> Warnings => _warnings;

        public void Convert()
        {
            foreach (var rawInstruction in LegacyScriptDocumentService.Instance.GetInstructions(_legacyScript))
            {
                AppendTriggerWaits(rawInstruction);
                ConvertInstruction(rawInstruction);
            }

            var convertedDocument = new ScriptDocument
            {
                Metadata = BuildMetadata(),
                MonkeyObjects = _monkeyObjects,
                Instructions = _instructions
            };

            Document = ScriptInstructionOptimizationService.Instance.OptimizeDocument(convertedDocument);
        }

        private ScriptMetadataDocument BuildMetadata()
        {
            var map = ResolveMap(_legacyScript.Metadata.SelectedMap, _warnings);
            var difficulty = ResolveDifficulty(_legacyScript.Metadata.SelectedDifficulty, _warnings);
            var mode = ResolveMode(_legacyScript.Metadata.SelectedMode, _warnings);

            if (_legacyScript.Metadata.AnchorCoords.X != 0 || _legacyScript.Metadata.AnchorCoords.Y != 0)
            {
                _warnings.Add(
                    $"Legacy anchor coordinate ({_legacyScript.Metadata.AnchorCoords.X}, {_legacyScript.Metadata.AnchorCoords.Y}) is not persisted by the new script model.");
            }

            return new ScriptMetadataDocument
            {
                ScriptVersion = string.IsNullOrWhiteSpace(_legacyScript.Metadata.Version)
                    ? ScriptDocumentFormat.DefaultScriptVersion
                    : _legacyScript.Metadata.Version,
                Category = ScriptDocumentCategories.Custom,
                Name = _legacyScript.Metadata.ScriptName ?? string.Empty,
                Description = string.Empty,
                Map = map.ToString(),
                Difficulty = difficulty.ToString(),
                Mode = mode.ToString(),
                Hero = _heroType.ToString()
            };
        }

        private void AppendTriggerWaits(LegacyScriptInstruction instruction)
        {
            if (instruction.RoundTrigger > 0)
            {
                _instructions.Add(new ScriptInstructionDocument
                {
                    CommandType = ScriptCommandType.Wait.ToString(),
                    WaitMode = WaitModeType.Round.ToString(),
                    WaitRoundCount = instruction.RoundTrigger,
                    IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                    Notes = BuildLegacyNotes(instruction)
                });
            }

            if (instruction.CoinTrigger > 0)
            {
                _instructions.Add(new ScriptInstructionDocument
                {
                    CommandType = ScriptCommandType.Wait.ToString(),
                    WaitMode = WaitModeType.Gold.ToString(),
                    WaitGoldAmount = instruction.CoinTrigger,
                    IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                    Notes = BuildLegacyNotes(instruction)
                });
            }
        }

        private void ConvertInstruction(LegacyScriptInstruction instruction)
        {
            switch (instruction.ActionType)
            {
                case LegacyActionType.PlaceMonkey:
                    ConvertPlaceMonkey(instruction);
                    break;
                case LegacyActionType.UpgradeMonkey:
                    ConvertUpgradeMonkey(instruction);
                    break;
                case LegacyActionType.SwitchMonkeyTarget:
                    ConvertSwitchTarget(instruction, isHeroTarget: false);
                    break;
                case LegacyActionType.ActivateAbility:
                    ConvertActivateAbility(instruction);
                    break;
                case LegacyActionType.ToggleGameSpeed:
                    ConvertToggleGameSpeed(instruction);
                    break;
                case LegacyActionType.SellMonkey:
                    ConvertSellMonkey(instruction, isHeroTarget: false);
                    break;
                case LegacyActionType.SetMonkeyAbility:
                    ConvertSetMonkeyAbility(instruction, isHeroTarget: false);
                    break;
                case LegacyActionType.PlaceHero:
                    ConvertPlaceHero(instruction);
                    break;
                case LegacyActionType.UpgradeHero:
                    ConvertUpgradeHero(instruction);
                    break;
                case LegacyActionType.PlaceHeroInventory:
                    ConvertPlaceHeroInventory(instruction);
                    break;
                case LegacyActionType.SwitchHeroTarget:
                    ConvertSwitchTarget(instruction, isHeroTarget: true);
                    break;
                case LegacyActionType.SetHeroAbility:
                    ConvertSetMonkeyAbility(instruction, isHeroTarget: true);
                    break;
                case LegacyActionType.SellHero:
                    ConvertSellMonkey(instruction, isHeroTarget: true);
                    break;
                case LegacyActionType.MouseClick:
                    ConvertMouseClick(instruction);
                    break;
                case LegacyActionType.ModifyMonkeyCoordinate:
                    ConvertModifyMonkeyCoordinate(instruction);
                    break;
                case LegacyActionType.Wait:
                    ConvertWait(instruction);
                    break;
                default:
                    AppendUnsupportedInstructionComment(instruction, $"Unsupported legacy action type '{instruction.Slots[0]}'.");
                    break;
            }
        }

        private void ConvertPlaceMonkey(LegacyScriptInstruction instruction)
        {
            if (!TryResolveMonkeyTowerType(instruction.Argument1, out var towerType))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy monkey type '{instruction.Argument1}' does not have a mapping to the current tower catalog.");
                return;
            }

            if (!instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(instruction, "Legacy place-monkey instruction is missing placement coordinates.");
                return;
            }

            var legacyMonkeyId = instruction.Argument7 > 0
                ? instruction.Argument7
                : CreateSyntheticLegacyMonkeyId(instruction.Argument1);
            var bindingId = $"legacy-monkey-{legacyMonkeyId}";
            var selectionCode = ScriptEditorInstructionService.BuildTowerSelectionCode(towerType);
            var objectId = BuildTowerObjectId(towerType);

            _monkeysByLegacyId[legacyMonkeyId] = new LegacyMonkeyBindingState
            {
                LegacyMonkeyId = legacyMonkeyId,
                BindingId = bindingId,
                ObjectId = objectId,
                SelectionCode = selectionCode,
                UpgradeLevels = [0, 0, 0],
                IsHero = false
            };

            _placementOrder++;
            _monkeyObjects.Add(new ScriptMonkeyObjectDocument
            {
                BindingId = bindingId,
                ObjectId = objectId,
                SelectionCode = selectionCode,
                PlacementOrder = _placementOrder
            });

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.PlaceMonkey.ToString(),
                SelectedMonkeyTower = selectionCode,
                MonkeyBindingId = bindingId,
                MonkeyObjectId = objectId,
                PositionX = instruction.CoordinateX,
                PositionY = instruction.CoordinateY,
                PlacementDetectionEnabled = true,
                PlacementFailureAdjustmentEnabled = instruction.Argument2 == (int)LegacyPlaceCheckType.Check,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertPlaceHero(LegacyScriptInstruction instruction)
        {
            if (!instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(instruction, "Legacy place-hero instruction is missing placement coordinates.");
                return;
            }

            _placementOrder++;
            _monkeyObjects.Add(new ScriptMonkeyObjectDocument
            {
                BindingId = HeroBindingId,
                ObjectId = _heroObjectId,
                SelectionCode = _heroObjectId,
                PlacementOrder = _placementOrder
            });

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.PlaceMonkey.ToString(),
                SelectedMonkeyTower = _heroObjectId,
                MonkeyBindingId = HeroBindingId,
                MonkeyObjectId = _heroObjectId,
                PositionX = instruction.CoordinateX,
                PositionY = instruction.CoordinateY,
                PlacementDetectionEnabled = true,
                PlacementFailureAdjustmentEnabled = true,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertUpgradeMonkey(LegacyScriptInstruction instruction)
        {
            if (!_monkeysByLegacyId.TryGetValue(instruction.Argument1, out var monkey))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy upgrade target monkey ID '{instruction.Argument1}' does not exist in conversion state.");
                return;
            }

            if (instruction.Argument5 < 0)
            {
                if (!TryResolveSingleUpgrade(instruction.Argument2, out var upgradePath))
                {
                    AppendUnsupportedInstructionComment(instruction, $"Legacy upgrade route '{instruction.Argument2}' is not supported.");
                    return;
                }

                ApplyUpgradeInstruction(instruction, monkey, upgradePath, 1);
                return;
            }

            var targetLevels = ParseAbsoluteUpgradeLevel(instruction.Argument5);
            var currentLevels = monkey.UpgradeLevels;
            if (targetLevels.Top < currentLevels[0] ||
                targetLevels.Middle < currentLevels[1] ||
                targetLevels.Bottom < currentLevels[2])
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy upgrade target level '{instruction.Argument5}' would reduce existing upgrade state for monkey '{instruction.Argument1}'.");
                return;
            }

            foreach (var path in BuildUpgradeOrder(instruction.Argument2))
            {
                var targetLevel = path switch
                {
                    UpgradePathType.Top => targetLevels.Top,
                    UpgradePathType.Middle => targetLevels.Middle,
                    UpgradePathType.Bottom => targetLevels.Bottom,
                    _ => 0
                };

                var currentLevel = path switch
                {
                    UpgradePathType.Top => currentLevels[0],
                    UpgradePathType.Middle => currentLevels[1],
                    UpgradePathType.Bottom => currentLevels[2],
                    _ => 0
                };

                var delta = targetLevel - currentLevel;
                if (delta > 0)
                {
                    ApplyUpgradeInstruction(instruction, monkey, path, delta);
                }
            }
        }

        private void ConvertUpgradeHero(LegacyScriptInstruction instruction)
        {
            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = HeroBindingId,
                TargetMonkeyObjectId = _heroObjectId,
                UpgradeCount = 1,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertSwitchTarget(LegacyScriptInstruction instruction, bool isHeroTarget)
        {
            if (!TryResolveSwitchBehavior(instruction.Argument2, out var direction, out var count))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy switch-target behavior '{instruction.Argument2}' is not supported.");
                return;
            }

            var targetBindingId = isHeroTarget ? HeroBindingId : ResolveMonkeyBindingId(instruction.Argument1, instruction);
            var targetObjectId = isHeroTarget ? _heroObjectId : ResolveMonkeyObjectId(instruction.Argument1);
            if (string.IsNullOrWhiteSpace(targetBindingId) || string.IsNullOrWhiteSpace(targetObjectId))
            {
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
                TargetMonkeyBindingId = targetBindingId,
                TargetMonkeyObjectId = targetObjectId,
                SwitchDirection = direction.ToString(),
                SwitchCount = count,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertSetMonkeyAbility(LegacyScriptInstruction instruction, bool isHeroTarget)
        {
            if (!TryResolveMonkeyAbility(instruction.Argument2, out var ability, out var requiresCoordinate))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy monkey ability code '{instruction.Argument2}' does not have a current hotkey mapping.");
                return;
            }

            if (requiresCoordinate && !instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy monkey ability '{instruction.Argument2}' requires a coordinate, but the instruction does not contain one.");
                return;
            }

            var targetBindingId = isHeroTarget ? HeroBindingId : ResolveMonkeyBindingId(instruction.Argument1, instruction);
            var targetObjectId = isHeroTarget ? _heroObjectId : ResolveMonkeyObjectId(instruction.Argument1);
            if (string.IsNullOrWhiteSpace(targetBindingId) || string.IsNullOrWhiteSpace(targetObjectId))
            {
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.SetMonkeyAbility.ToString(),
                TargetMonkeyBindingId = targetBindingId,
                TargetMonkeyObjectId = targetObjectId,
                SelectedAbility = ability.ToString(),
                RequiresAbilityCoordinate = requiresCoordinate,
                AbilityCoordinateX = requiresCoordinate ? instruction.CoordinateX : 0,
                AbilityCoordinateY = requiresCoordinate ? instruction.CoordinateY : 0,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertSellMonkey(LegacyScriptInstruction instruction, bool isHeroTarget)
        {
            var targetBindingId = isHeroTarget ? HeroBindingId : ResolveMonkeyBindingId(instruction.Argument1, instruction);
            var targetObjectId = isHeroTarget ? _heroObjectId : ResolveMonkeyObjectId(instruction.Argument1);
            if (string.IsNullOrWhiteSpace(targetBindingId) || string.IsNullOrWhiteSpace(targetObjectId))
            {
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.SellMonkey.ToString(),
                TargetMonkeyBindingId = targetBindingId,
                TargetMonkeyObjectId = targetObjectId,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });

            if (!isHeroTarget)
            {
                _monkeysByLegacyId.Remove(instruction.Argument1);
            }
        }

        private void ConvertActivateAbility(LegacyScriptInstruction instruction)
        {
            if (!TryResolveActivatedAbility(instruction.Argument1, out var ability))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy activated ability '{instruction.Argument1}' does not have a mapping to the current ability hotkeys.");
                return;
            }

            if (instruction.Argument2 == (int)LegacyCoordinateType.Coordinate && !instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy activated ability '{instruction.Argument1}' requires a coordinate, but the instruction does not contain one.");
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.ActivateAbility.ToString(),
                SelectedActivatedAbility = ability.ToString(),
                RequiresAbilityCoordinate = instruction.Argument2 == (int)LegacyCoordinateType.Coordinate,
                AbilityCoordinateX = instruction.Argument2 == (int)LegacyCoordinateType.Coordinate ? instruction.CoordinateX : 0,
                AbilityCoordinateY = instruction.Argument2 == (int)LegacyCoordinateType.Coordinate ? instruction.CoordinateY : 0,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertToggleGameSpeed(LegacyScriptInstruction instruction)
        {
            var action = instruction.Argument1 switch
            {
                (int)LegacySpeedType.Switch => "PlayFastForward",
                (int)LegacySpeedType.NextRound => "SendNextRound",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(action))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy next-round action '{instruction.Argument1}' is not supported.");
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.NextRound.ToString(),
                NextRoundAction = action,
                NextRoundSendCount = 1,
                NextRoundOperationIntervalMilliseconds = NextRoundInstructionHandler.DefaultOperationIntervalMilliseconds,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertPlaceHeroInventory(LegacyScriptInstruction instruction)
        {
            if (!TryResolveInventory(instruction.Argument1, out var inventory))
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy hero inventory '{instruction.Argument1}' does not have a mapping to the current inventory list.");
                return;
            }

            if (instruction.Argument2 == (int)LegacyCoordinateType.Coordinate && !instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(
                    instruction,
                    $"Legacy hero inventory '{instruction.Argument1}' requires a coordinate, but the instruction does not contain one.");
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.PlaceHeroInventory.ToString(),
                SelectedInventoryItem = inventory.ToString(),
                RequiresAbilityCoordinate = instruction.Argument2 == (int)LegacyCoordinateType.Coordinate,
                AbilityCoordinateX = instruction.Argument2 == (int)LegacyCoordinateType.Coordinate ? instruction.CoordinateX : 0,
                AbilityCoordinateY = instruction.Argument2 == (int)LegacyCoordinateType.Coordinate ? instruction.CoordinateY : 0,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertMouseClick(LegacyScriptInstruction instruction)
        {
            if (!instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(instruction, "Legacy mouse-click instruction is missing click coordinates.");
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.MouseClick.ToString(),
                PositionX = instruction.CoordinateX,
                PositionY = instruction.CoordinateY,
                ClickCount = Math.Max(1, instruction.Argument1),
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertModifyMonkeyCoordinate(LegacyScriptInstruction instruction)
        {
            if (!instruction.HasCoordinate)
            {
                AppendUnsupportedInstructionComment(instruction, "Legacy modify-coordinate instruction is missing target coordinates.");
                return;
            }

            var targetBindingId = ResolveMonkeyBindingId(instruction.Argument1, instruction);
            var targetObjectId = ResolveMonkeyObjectId(instruction.Argument1);
            if (string.IsNullOrWhiteSpace(targetBindingId) || string.IsNullOrWhiteSpace(targetObjectId))
            {
                return;
            }

            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.ModifyMonkeyCoordinate.ToString(),
                TargetMonkeyBindingId = targetBindingId,
                TargetMonkeyObjectId = targetObjectId,
                PositionX = instruction.CoordinateX,
                PositionY = instruction.CoordinateY,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ConvertWait(LegacyScriptInstruction instruction)
        {
            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.Wait.ToString(),
                WaitMode = WaitModeType.Time.ToString(),
                WaitTimeMilliseconds = Math.Max(0, instruction.Argument1),
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private void ApplyUpgradeInstruction(
            LegacyScriptInstruction legacyInstruction,
            LegacyMonkeyBindingState monkey,
            UpgradePathType path,
            int count)
        {
            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = monkey.BindingId,
                TargetMonkeyObjectId = monkey.ObjectId,
                UpgradePath = path.ToString(),
                UpgradeCount = count,
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(legacyInstruction)
            });

            switch (path)
            {
                case UpgradePathType.Top:
                    monkey.UpgradeLevels[0] += count;
                    break;
                case UpgradePathType.Middle:
                    monkey.UpgradeLevels[1] += count;
                    break;
                case UpgradePathType.Bottom:
                    monkey.UpgradeLevels[2] += count;
                    break;
            }
        }

        private string ResolveMonkeyBindingId(int legacyMonkeyId, LegacyScriptInstruction instruction)
        {
            if (_monkeysByLegacyId.TryGetValue(legacyMonkeyId, out var monkey))
            {
                return monkey.BindingId;
            }

            AppendUnsupportedInstructionComment(
                instruction,
                $"Legacy monkey target ID '{legacyMonkeyId}' does not exist in conversion state.");
            return string.Empty;
        }

        private string ResolveMonkeyObjectId(int legacyMonkeyId)
        {
            return _monkeysByLegacyId.TryGetValue(legacyMonkeyId, out var monkey)
                ? monkey.ObjectId
                : string.Empty;
        }

        private string BuildTowerObjectId(MonkeyTowerType towerType)
        {
            var nextIndex = _towerPlacementCounts.TryGetValue(towerType, out var currentIndex)
                ? currentIndex + 1
                : 1;
            _towerPlacementCounts[towerType] = nextIndex;
            return ScriptEditorInstructionService.BuildMonkeyObjectKey(towerType, nextIndex);
        }

        private int CreateSyntheticLegacyMonkeyId(int legacyMonkeyType)
        {
            var count = _legacyScript.MonkeyCounts.Count > legacyMonkeyType && legacyMonkeyType >= 0
                ? Math.Max(1, _legacyScript.MonkeyCounts[legacyMonkeyType])
                : _placementOrder + 1;
            return (count * 100) + Math.Max(0, legacyMonkeyType);
        }

        private void AppendUnsupportedInstructionComment(LegacyScriptInstruction instruction, string message)
        {
            _warnings.Add($"Instruction {instruction.Index}: {message}");
            _instructions.Add(new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.Comment.ToString(),
                CommentContent = $"[Legacy Conversion] {message}",
                IntervalToNextInstructionMs = DefaultInstructionIntervalMilliseconds,
                Notes = BuildLegacyNotes(instruction)
            });
        }

        private static string BuildLegacyNotes(LegacyScriptInstruction instruction)
        {
            return $"{LegacyNotesPrefix} index={instruction.Index}, slots={JsonSerializer.Serialize(instruction.Slots)}";
        }
    }

    private static GameMapType ResolveMap(int legacyMapId, ICollection<string> warnings)
    {
        if (Enum.IsDefined(typeof(LegacyMapType), legacyMapId) &&
            Enum.TryParse<GameMapType>(((LegacyMapType)legacyMapId).ToString(), out var map))
        {
            return map;
        }

        warnings.Add($"Legacy map id '{legacyMapId}' is out of range. Defaulting to '{GameMapType.MonkeyMeadow}'.");
        return GameMapType.MonkeyMeadow;
    }

    private static StageDifficulty ResolveDifficulty(int legacyDifficultyId, ICollection<string> warnings)
    {
        if (Enum.IsDefined(typeof(LegacyLevelDifficulty), legacyDifficultyId))
        {
            var difficultyName = ((LegacyLevelDifficulty)legacyDifficultyId).ToString();
            if (Enum.TryParse<StageDifficulty>(difficultyName, out var difficulty))
            {
                return difficulty;
            }
        }

        warnings.Add($"Legacy difficulty id '{legacyDifficultyId}' is out of range. Defaulting to '{StageDifficulty.Medium}'.");
        return StageDifficulty.Medium;
    }

    private static StageMode ResolveMode(int legacyModeId, ICollection<string> warnings)
    {
        if (Enum.IsDefined(typeof(LegacyLevelMode), legacyModeId))
        {
            var modeName = ((LegacyLevelMode)legacyModeId).ToString();
            if (string.Equals(modeName, nameof(LegacyLevelMode.MagicMonkeysOnly), StringComparison.Ordinal))
            {
                modeName = nameof(StageMode.MagicOnly);
            }

            if (Enum.TryParse<StageMode>(modeName, out var mode))
            {
                return mode;
            }
        }

        warnings.Add($"Legacy mode id '{legacyModeId}' is out of range. Defaulting to '{StageMode.Standard}'.");
        return StageMode.Standard;
    }

    private static HeroType ResolveHeroType(int legacyHeroId, ICollection<string> warnings)
    {
        if (Enum.IsDefined(typeof(LegacyHeroType), legacyHeroId) &&
            Enum.TryParse<HeroType>(((LegacyHeroType)legacyHeroId).ToString(), out var hero))
        {
            return hero;
        }

        warnings.Add($"Legacy hero id '{legacyHeroId}' is out of range. Defaulting to '{HeroType.Quincy}'.");
        return HeroType.Quincy;
    }

    private static bool TryResolveActivatedAbility(int legacyAbilityId, out ActivatedAbilityType ability)
    {
        if (Enum.IsDefined(typeof(LegacySkillType), legacyAbilityId))
        {
            var abilityName = $"ActivatedAbility{legacyAbilityId + 1}";
            if (Enum.TryParse<ActivatedAbilityType>(abilityName, out ability))
            {
                return true;
            }
        }

        ability = default;
        return false;
    }

    private static bool TryResolveInventory(int legacyInventoryId, out InventoryType inventory)
    {
        if (Enum.IsDefined(typeof(LegacyHeroObjectType), legacyInventoryId))
        {
            var inventoryName = $"Inventory{legacyInventoryId}";
            if (Enum.TryParse<InventoryType>(inventoryName, out inventory))
            {
                return true;
            }
        }

        inventory = default;
        return false;
    }

    private static bool TryResolveSingleUpgrade(int legacyRoute, out UpgradePathType path)
    {
        switch ((LegacyUpgradeType)legacyRoute)
        {
            case LegacyUpgradeType.Top:
            case LegacyUpgradeType.TopOnce:
                path = UpgradePathType.Top;
                return true;
            case LegacyUpgradeType.Middle:
            case LegacyUpgradeType.MiddleOnce:
                path = UpgradePathType.Middle;
                return true;
            case LegacyUpgradeType.Bottom:
            case LegacyUpgradeType.BottomOnce:
                path = UpgradePathType.Bottom;
                return true;
            default:
                path = default;
                return false;
        }
    }

    private static (int Top, int Middle, int Bottom) ParseAbsoluteUpgradeLevel(int value)
    {
        var safeValue = Math.Max(0, value);
        return (safeValue / 100, (safeValue / 10) % 10, safeValue % 10);
    }

    private static IReadOnlyList<UpgradePathType> BuildUpgradeOrder(int legacyRoute)
    {
        return (LegacyUpgradeType)legacyRoute switch
        {
            LegacyUpgradeType.Top or LegacyUpgradeType.TopOnce => [UpgradePathType.Top, UpgradePathType.Middle, UpgradePathType.Bottom],
            LegacyUpgradeType.Middle or LegacyUpgradeType.MiddleOnce => [UpgradePathType.Middle, UpgradePathType.Top, UpgradePathType.Bottom],
            LegacyUpgradeType.Bottom or LegacyUpgradeType.BottomOnce => [UpgradePathType.Bottom, UpgradePathType.Top, UpgradePathType.Middle],
            _ => [UpgradePathType.Top, UpgradePathType.Middle, UpgradePathType.Bottom]
        };
    }

    private static bool TryResolveSwitchBehavior(int legacySwitchCode, out SwitchDirectionType direction, out int count)
    {
        direction = (LegacyTargetType)legacySwitchCode switch
        {
            LegacyTargetType.Right or LegacyTargetType.RightDouble or LegacyTargetType.RightTriple => SwitchDirectionType.Right,
            LegacyTargetType.Left or LegacyTargetType.LeftDouble or LegacyTargetType.LeftTriple => SwitchDirectionType.Left,
            _ => default
        };

        count = (LegacyTargetType)legacySwitchCode switch
        {
            LegacyTargetType.Right or LegacyTargetType.Left => 1,
            LegacyTargetType.RightDouble or LegacyTargetType.LeftDouble => 2,
            LegacyTargetType.RightTriple or LegacyTargetType.LeftTriple => 3,
            _ => 0
        };

        return count > 0;
    }

    private static bool TryResolveMonkeyTowerType(int legacyMonkeyId, out MonkeyTowerType towerType)
    {
        if (Enum.IsDefined(typeof(LegacyMonkeyType), legacyMonkeyId) &&
            Enum.TryParse<MonkeyTowerType>(((LegacyMonkeyType)legacyMonkeyId).ToString(), out towerType))
        {
            return true;
        }

        towerType = default;
        return false;
    }

    private static bool TryResolveMonkeyAbility(int legacyFunctionId, out MonkeyAbilityType ability, out bool requiresCoordinate)
    {
        switch ((LegacyMonkeyFunctionType)legacyFunctionId)
        {
            case LegacyMonkeyFunctionType.Function1:
                ability = MonkeyAbilityType.Ability1;
                requiresCoordinate = false;
                return true;
            case LegacyMonkeyFunctionType.Function1Coordinate:
                ability = MonkeyAbilityType.Ability1;
                requiresCoordinate = true;
                return true;
            case LegacyMonkeyFunctionType.Function2:
                ability = MonkeyAbilityType.Ability2;
                requiresCoordinate = false;
                return true;
            case LegacyMonkeyFunctionType.Function2Coordinate:
                ability = MonkeyAbilityType.Ability2;
                requiresCoordinate = true;
                return true;
            default:
                ability = default;
                requiresCoordinate = false;
                return false;
        }
    }

    public sealed class LegacyScriptConversionResult
    {
        public required ScriptDocument Document { get; init; }

        public required IReadOnlyList<string> Warnings { get; init; }
    }

    private sealed class LegacyMonkeyBindingState
    {
        public required int LegacyMonkeyId { get; init; }

        public required string BindingId { get; init; }

        public required string ObjectId { get; init; }

        public required string SelectionCode { get; init; }

        public required int[] UpgradeLevels { get; init; }

        public required bool IsHero { get; init; }
    }
}

