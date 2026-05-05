using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services;

public sealed class ScriptEditorSequenceService
{
    private static readonly Lazy<ScriptEditorSequenceService> InstanceHolder = new(() => new ScriptEditorSequenceService());
    private readonly ScriptEditorInstructionService _instructionService = ScriptEditorInstructionService.Instance;

    private ScriptEditorSequenceService()
    {
    }

    public static ScriptEditorSequenceService Instance => InstanceHolder.Value;

    public void UpdateInstructionLocalization(
        IEnumerable<ScriptInstructionTemplate> templates,
        IList<ScriptInstructionInstance> instructions,
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(localizationService);

        foreach (var template in templates)
        {
            template.DisplayName = localizationService.T(template.NameKey);
            template.Description = localizationService.T(template.DescriptionKey);
        }

        foreach (var instance in instructions)
        {
            instance.Description = localizationService.T(instance.DescriptionKey);
        }

        UpdateInstructionDisplayNames(instructions, localizationService);
    }

    public List<LanguageOption> RebuildMonkeyObjectOptions(
        IList<ScriptInstructionInstance> instructions,
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(localizationService);

        var originalTargetBindingIds = instructions
            .Where(_instructionService.RequiresMonkeyObjectTarget)
            .ToDictionary(x => x, x => x.TargetMonkeyBindingId);
        var originalTargetObjectIds = instructions
            .Where(_instructionService.RequiresMonkeyObjectTarget)
            .ToDictionary(x => x, x => x.TargetMonkeyObjectId);

        var existingKeyCounts = instructions
            .Where(x => x.Type == ScriptCommandType.PlaceMonkey && !string.IsNullOrWhiteSpace(x.MonkeyObjectId))
            .GroupBy(x => x.MonkeyObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var maxTowerIndexes = new Dictionary<MonkeyTowerType, int>();
        foreach (var key in existingKeyCounts.Keys)
        {
            if (!ScriptEditorInstructionService.TryParseMonkeyObjectKey(key, out var towerType, out var index))
            {
                continue;
            }

            maxTowerIndexes.TryGetValue(towerType, out var currentMaxIndex);
            maxTowerIndexes[towerType] = Math.Max(currentMaxIndex, index);
        }

        var bindingIdToObjectKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var objectKeyToBindingId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new List<LanguageOption>();

        foreach (var instruction in instructions)
        {
            if (instruction.Type != ScriptCommandType.PlaceMonkey)
            {
                continue;
            }

            var selectionCode = ScriptEditorInstructionService.NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower);
            var originalKey = instruction.MonkeyObjectId;
            if (string.IsNullOrWhiteSpace(instruction.MonkeyBindingId))
            {
                instruction.MonkeyBindingId = ScriptEditorInstructionService.CreateMonkeyBindingId();
            }

            if (ScriptEditorInstructionService.TryParseHeroSelection(selectionCode, out var heroType))
            {
                var heroKey = ScriptEditorInstructionService.BuildHeroObjectKey(heroType);
                if (objectKeyToBindingId.TryGetValue(heroKey, out var existingHeroBindingId))
                {
                    instruction.MonkeyBindingId = existingHeroBindingId;
                }
                else
                {
                    objectKeyToBindingId[heroKey] = instruction.MonkeyBindingId;
                }

                instruction.MonkeyObjectId = heroKey;
                bindingIdToObjectKey[instruction.MonkeyBindingId] = heroKey;

                if (options.All(x => !string.Equals(x.Code, instruction.MonkeyBindingId, StringComparison.OrdinalIgnoreCase)))
                {
                    options.Add(new LanguageOption
                    {
                        Code = instruction.MonkeyBindingId,
                        DisplayName = BuildHeroObjectDisplayName(heroType, localizationService)
                    });
                }

                continue;
            }

            var towerType = ScriptEditorInstructionService.TryParseTowerSelection(selectionCode, out var parsedTowerType)
                ? parsedTowerType
                : MonkeyTowerType.DartMonkey;
            var key = ResolveMonkeyObjectKey(originalKey, towerType, maxTowerIndexes, usedKeys);
            instruction.MonkeyObjectId = key;
            bindingIdToObjectKey[instruction.MonkeyBindingId] = key;
            objectKeyToBindingId.TryAdd(key, instruction.MonkeyBindingId);

            usedKeys.Add(key);

            options.Add(new LanguageOption
            {
                Code = instruction.MonkeyBindingId,
                DisplayName = BuildMonkeyObjectDisplayName(key, localizationService)
            });
        }

        var availableBindingIds = options.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var instruction in instructions.Where(_instructionService.RequiresMonkeyObjectTarget))
        {
            var targetBindingId = originalTargetBindingIds.GetValueOrDefault(instruction) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetBindingId))
            {
                var targetObjectId = originalTargetObjectIds.GetValueOrDefault(instruction) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(targetObjectId) &&
                    objectKeyToBindingId.TryGetValue(targetObjectId, out var resolvedBindingId))
                {
                    targetBindingId = resolvedBindingId;
                }
            }

            if (string.IsNullOrWhiteSpace(targetBindingId) ||
                !availableBindingIds.Contains(targetBindingId))
            {
                targetBindingId = string.Empty;
            }

            instruction.TargetMonkeyBindingId = targetBindingId;
            instruction.TargetMonkeyObjectId = bindingIdToObjectKey.GetValueOrDefault(targetBindingId, string.Empty);
        }

        UpdateInstructionDisplayNames(instructions, localizationService);
        return options;
    }

    public string ResolveTargetMonkeyObjectKey(
        ScriptInstructionInstance instruction,
        IEnumerable<ScriptInstructionInstance> instructions)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(instructions);

        if (!_instructionService.RequiresMonkeyObjectTarget(instruction))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            var placeInstruction = instructions.FirstOrDefault(x =>
                x.Type == ScriptCommandType.PlaceMonkey &&
                string.Equals(x.MonkeyBindingId, instruction.TargetMonkeyBindingId, StringComparison.OrdinalIgnoreCase));
            if (placeInstruction is not null && !string.IsNullOrWhiteSpace(placeInstruction.MonkeyObjectId))
            {
                return placeInstruction.MonkeyObjectId;
            }
        }

        return instruction.TargetMonkeyObjectId;
    }

    public bool ShouldRefreshAllInstructionDisplayNames(ScriptInstructionInstance instruction, string? propertyName)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        if (instruction.Type != ScriptCommandType.UpgradeMonkey)
        {
            return false;
        }

        return string.Equals(propertyName, nameof(ScriptInstructionInstance.TargetMonkeyBindingId), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(ScriptInstructionInstance.UpgradePath), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(ScriptInstructionInstance.UpgradeCount), StringComparison.Ordinal);
    }

    public void UpdateInstructionDisplayNames(
        IList<ScriptInstructionInstance> instructions,
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(localizationService);

        var upgradeLevelStates = BuildUpgradeLevelStates(instructions);
        foreach (var instruction in instructions)
        {
            UpdateInstructionDisplayName(instruction, instructions, upgradeLevelStates, localizationService);
        }
    }

    public void UpdateInstructionDisplayName(
        ScriptInstructionInstance instruction,
        IList<ScriptInstructionInstance> instructions,
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(localizationService);

        UpdateInstructionDisplayName(instruction, instructions, BuildUpgradeLevelStates(instructions), localizationService);
    }

    private void UpdateInstructionDisplayName(
        ScriptInstructionInstance instruction,
        IList<ScriptInstructionInstance> instructions,
        IReadOnlyDictionary<ScriptInstructionInstance, string> upgradeLevelStates,
        LocalizationService localizationService)
    {
        var text = instruction.Type switch
        {
            ScriptCommandType.PlaceMonkey => string.Format(
                localizationService.T("Editor.Display.PlaceMonkey"),
                string.IsNullOrWhiteSpace(instruction.MonkeyObjectId)
                    ? GetPlaceSelectionDisplayName(instruction.SelectedMonkeyTower, localizationService)
                    : GetMonkeyObjectDisplayName(instruction.MonkeyObjectId, localizationService),
                FormatCoordinate(instruction.PositionX),
                FormatCoordinate(instruction.PositionY)),
            ScriptCommandType.UpgradeMonkey => IsHeroObjectKey(ResolveTargetMonkeyObjectKey(instruction, instructions))
                ? string.Format(
                    localizationService.T("Editor.Display.UpgradeMonkey.Hero"),
                    GetTargetMonkeyDisplayName(instruction, instructions, localizationService),
                    instruction.UpgradeCount)
                : string.Format(
                    localizationService.T("Editor.Display.UpgradeMonkey"),
                    GetTargetMonkeyDisplayName(instruction, instructions, localizationService),
                    upgradeLevelStates.GetValueOrDefault(instruction, "000")),
            ScriptCommandType.SwitchMonkeyTarget => string.Format(
                localizationService.T("Editor.Display.SwitchMonkeyTarget"),
                GetTargetMonkeyDisplayName(instruction, instructions, localizationService),
                GetSwitchDirectionDisplayName(instruction.SwitchDirection, localizationService),
                instruction.SwitchCount),
            ScriptCommandType.SetMonkeyAbility => string.Format(
                localizationService.T("Editor.Display.SetMonkeyAbility"),
                GetTargetMonkeyDisplayName(instruction, instructions, localizationService),
                GetAbilityDisplayNumber(instruction.SelectedAbility),
                instruction.RequiresAbilityCoordinate
                    ? string.Format(
                        localizationService.T("Editor.Display.SetMonkeyAbility.WithCoordinateSuffix"),
                        FormatCoordinate(instruction.AbilityCoordinateX),
                        FormatCoordinate(instruction.AbilityCoordinateY))
                    : string.Empty),
            ScriptCommandType.SellMonkey => string.Format(
                localizationService.T("Editor.Display.SellMonkey"),
                GetTargetMonkeyDisplayName(instruction, instructions, localizationService)),
            ScriptCommandType.PlaceHeroInventory => string.Format(
                localizationService.T("Editor.Display.PlaceHeroInventory"),
                GetInventoryDisplayName(instruction.SelectedInventoryItem, localizationService),
                instruction.RequiresAbilityCoordinate
                    ? string.Format(
                        localizationService.T("Editor.Display.PlaceHeroInventory.WithCoordinateSuffix"),
                        FormatCoordinate(instruction.PositionX),
                        FormatCoordinate(instruction.PositionY))
                    : string.Empty),
            ScriptCommandType.ActivateAbility => string.Format(
                localizationService.T("Editor.Display.ActivateAbility"),
                GetActivatedAbilityDisplayName(instruction.SelectedActivatedAbility, localizationService),
                instruction.RequiresAbilityCoordinate
                    ? string.Format(
                        localizationService.T("Editor.Display.ActivateAbility.WithCoordinateSuffix"),
                        FormatCoordinate(instruction.AbilityCoordinateX),
                        FormatCoordinate(instruction.AbilityCoordinateY))
                    : string.Empty),
            ScriptCommandType.NextRound => GetNextRoundActionDisplayName(instruction, localizationService),
            ScriptCommandType.Wait => GetWaitDisplayName(instruction, localizationService),
            ScriptCommandType.ModifyMonkeyCoordinate => string.Format(
                localizationService.T("Editor.Display.ModifyMonkeyCoordinate"),
                GetTargetMonkeyDisplayName(instruction, instructions, localizationService),
                FormatCoordinate(instruction.PositionX),
                FormatCoordinate(instruction.PositionY)),
            ScriptCommandType.Comment => string.Format(
                localizationService.T("Editor.Display.Comment"),
                GetCommentPreview(instruction.CommentContent, localizationService)),
            _ => localizationService.T(instruction.NameKey)
        };

        instruction.DisplayName = text;
    }

    private IReadOnlyDictionary<ScriptInstructionInstance, string> BuildUpgradeLevelStates(IList<ScriptInstructionInstance> instructions)
    {
        var upgradeStates = new Dictionary<ScriptInstructionInstance, string>();
        var monkeyLevels = new Dictionary<string, (int Top, int Middle, int Bottom)>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            if (instruction.Type != ScriptCommandType.UpgradeMonkey)
            {
                continue;
            }

            var targetObjectKey = ResolveTargetMonkeyObjectKey(instruction, instructions);
            if (IsHeroObjectKey(targetObjectKey))
            {
                continue;
            }

            var trackingKey = ResolveUpgradeTrackingKey(instruction, targetObjectKey, index);
            monkeyLevels.TryGetValue(trackingKey, out var currentLevels);
            currentLevels = instruction.UpgradePath switch
            {
                UpgradePathType.Top => (currentLevels.Top + instruction.UpgradeCount, currentLevels.Middle, currentLevels.Bottom),
                UpgradePathType.Middle => (currentLevels.Top, currentLevels.Middle + instruction.UpgradeCount, currentLevels.Bottom),
                UpgradePathType.Bottom => (currentLevels.Top, currentLevels.Middle, currentLevels.Bottom + instruction.UpgradeCount),
                _ => currentLevels
            };

            monkeyLevels[trackingKey] = currentLevels;
            upgradeStates[instruction] = $"{currentLevels.Top}{currentLevels.Middle}{currentLevels.Bottom}";
        }

        return upgradeStates;
    }

    private static string ResolveMonkeyObjectKey(
        string? currentKey,
        MonkeyTowerType towerType,
        IDictionary<MonkeyTowerType, int> maxTowerIndexes,
        ISet<string> usedKeys)
    {
        if (ScriptEditorInstructionService.TryParseMonkeyObjectKey(currentKey, out var currentTowerType, out _) &&
            currentTowerType == towerType &&
            !usedKeys.Contains(currentKey!))
        {
            return currentKey!;
        }

        maxTowerIndexes.TryGetValue(towerType, out var currentMaxIndex);
        var nextIndex = currentMaxIndex + 1;
        maxTowerIndexes[towerType] = nextIndex;
        return ScriptEditorInstructionService.BuildMonkeyObjectKey(towerType, nextIndex);
    }

    private static string ResolveUpgradeTrackingKey(
        ScriptInstructionInstance instruction,
        string targetObjectKey,
        int sequenceIndex)
    {
        if (!string.IsNullOrWhiteSpace(targetObjectKey))
        {
            return $"Object:{targetObjectKey}";
        }

        if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            return $"Binding:{instruction.TargetMonkeyBindingId}";
        }

        if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyObjectId))
        {
            return $"Target:{instruction.TargetMonkeyObjectId}";
        }

        return $"Instruction:{sequenceIndex}";
    }

    private static bool IsHeroObjectKey(string objectKey)
    {
        return objectKey.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetAbilityDisplayNumber(MonkeyAbilityType ability)
    {
        return ability == MonkeyAbilityType.Ability2 ? 2 : 1;
    }

    private static string FormatCoordinate(double value)
    {
        return value.ToString("0.##");
    }

    private static string FormatWaitColorHex(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim().ToUpperInvariant();
    }

    private string GetPlaceSelectionDisplayName(string selectionCode, LocalizationService localizationService)
    {
        var normalized = ScriptEditorInstructionService.NormalizePlaceSelectionCode(selectionCode);
        if (ScriptEditorInstructionService.TryParseTowerSelection(normalized, out var towerType))
        {
            return GetTowerDisplayName(towerType, localizationService);
        }

        if (ScriptEditorInstructionService.TryParseHeroSelection(normalized, out var heroType))
        {
            return BuildHeroObjectDisplayName(heroType, localizationService);
        }

        return GetTowerDisplayName(MonkeyTowerType.DartMonkey, localizationService);
    }

    private string GetMonkeyObjectDisplayName(string objectKey, LocalizationService localizationService)
    {
        return string.IsNullOrWhiteSpace(objectKey)
            ? localizationService.T("Editor.Property.TargetMonkey")
            : BuildMonkeyObjectDisplayName(objectKey, localizationService);
    }

    private string GetTargetMonkeyDisplayName(
        ScriptInstructionInstance instruction,
        IEnumerable<ScriptInstructionInstance> instructions,
        LocalizationService localizationService)
    {
        return GetMonkeyObjectDisplayName(ResolveTargetMonkeyObjectKey(instruction, instructions), localizationService);
    }

    private static string GetSwitchDirectionDisplayName(SwitchDirectionType direction, LocalizationService localizationService)
    {
        return direction switch
        {
            SwitchDirectionType.Right => localizationService.T("Editor.Display.Direction.Right"),
            SwitchDirectionType.Left => localizationService.T("Editor.Display.Direction.Left"),
            _ => localizationService.T("Editor.Display.Direction.Right")
        };
    }

    private static string GetInventoryDisplayName(string inventoryCode, LocalizationService localizationService)
    {
        return Enum.TryParse<InventoryType>(inventoryCode, out var inventoryType)
            ? GameElementCatalog.GetInventoryDisplayName(inventoryType)
            : localizationService.T("Editor.Property.Inventory");
    }

    private static string GetActivatedAbilityDisplayName(string activatedAbilityCode, LocalizationService localizationService)
    {
        return Enum.TryParse<ActivatedAbilityType>(activatedAbilityCode, out var abilityType)
            ? GameElementCatalog.GetActivatedAbilityDisplayName(abilityType)
            : localizationService.T("Editor.Property.ActivatedAbility");
    }

    private static string GetNextRoundActionDisplayName(ScriptInstructionInstance instruction, LocalizationService localizationService)
    {
        return instruction.NextRoundAction switch
        {
            "SendNextRound" => string.Format(
                localizationService.T("Editor.Display.NextRound.SendNextRound"),
                instruction.NextRoundSendCount),
            _ => localizationService.T("Editor.Display.NextRound.PlayFastForward")
        };
    }

    private static string GetWaitDisplayName(ScriptInstructionInstance instruction, LocalizationService localizationService)
    {
        return instruction.WaitMode switch
        {
            nameof(WaitModeType.Gold) => string.Format(
                localizationService.T("Editor.Display.Wait.Gold"),
                instruction.WaitGoldAmount),
            nameof(WaitModeType.Round) => string.Format(
                localizationService.T("Editor.Display.Wait.Round"),
                instruction.WaitRoundCount),
            nameof(WaitModeType.CoordinateColor) => string.Format(
                localizationService.T("Editor.Display.Wait.CoordinateColor"),
                FormatCoordinate(instruction.WaitColorCoordinateX),
                FormatCoordinate(instruction.WaitColorCoordinateY),
                FormatWaitColorHex(instruction.WaitColorHex),
                instruction.WaitColorTolerance),
            _ => string.Format(
                localizationService.T("Editor.Display.Wait.Time"),
                instruction.WaitTimeMilliseconds)
        };
    }

    private static string GetCommentPreview(string? content, LocalizationService localizationService)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return localizationService.T("Editor.Display.Comment.Empty");
        }

        return content
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();
    }

    private static string GetTowerDisplayName(MonkeyTowerType towerType, LocalizationService localizationService)
    {
        var tower = GameElementCatalog.MonkeyTowers.FirstOrDefault(x => x.Type == towerType);
        return tower is null ? towerType.ToString() : localizationService.T(tower.NameKey);
    }

    private string BuildMonkeyObjectDisplayName(string objectKey, LocalizationService localizationService)
    {
        if (ScriptEditorInstructionService.TryParseMonkeyObjectKey(objectKey, out var towerType, out var index))
        {
            return BuildMonkeyObjectDisplayName(towerType, index, localizationService);
        }

        if (ScriptEditorInstructionService.TryParseHeroSelection(objectKey, out var heroType))
        {
            return BuildHeroObjectDisplayName(heroType, localizationService);
        }

        return objectKey;
    }

    private string BuildMonkeyObjectDisplayName(MonkeyTowerType towerType, int index, LocalizationService localizationService)
    {
        return $"{GetTowerDisplayName(towerType, localizationService)}{index}";
    }

    private static string BuildHeroObjectDisplayName(HeroType heroType, LocalizationService localizationService)
    {
        var hero = GameElementCatalog.Heroes.FirstOrDefault(x => x.Type == heroType);
        return hero is null ? heroType.ToString() : localizationService.T(hero.NameKey);
    }
}
