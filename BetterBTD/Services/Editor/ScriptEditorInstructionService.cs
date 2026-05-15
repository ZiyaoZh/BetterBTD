using System.IO;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.Editor;

public sealed class ScriptEditorInstructionService
{
    private static readonly Lazy<ScriptEditorInstructionService> InstanceHolder = new(() => new ScriptEditorInstructionService());

    private ScriptEditorInstructionService()
    {
    }

    public static ScriptEditorInstructionService Instance => InstanceHolder.Value;

    public IReadOnlyList<ScriptInstructionTemplate> CreateInstructionLibrary()
    {
        return
        [
            CreateTemplate(ScriptCommandType.PlaceMonkey, "Editor.Command.PlaceMonkey.Title", "Editor.Command.PlaceMonkey.Description"),
            CreateTemplate(ScriptCommandType.UpgradeMonkey, "Editor.Command.UpgradeMonkey.Title", "Editor.Command.UpgradeMonkey.Description"),
            CreateTemplate(ScriptCommandType.SwitchMonkeyTarget, "Editor.Command.SwitchMonkeyTarget.Title", "Editor.Command.SwitchMonkeyTarget.Description"),
            CreateTemplate(ScriptCommandType.SetMonkeyAbility, "Editor.Command.SetMonkeyAbility.Title", "Editor.Command.SetMonkeyAbility.Description"),
            CreateTemplate(ScriptCommandType.SellMonkey, "Editor.Command.SellMonkey.Title", "Editor.Command.SellMonkey.Description"),
            CreateTemplate(ScriptCommandType.PlaceHeroInventory, "Editor.Command.PlaceHeroInventory.Title", "Editor.Command.PlaceHeroInventory.Description"),
            CreateTemplate(ScriptCommandType.ActivateAbility, "Editor.Command.ActivateAbility.Title", "Editor.Command.ActivateAbility.Description"),
            CreateTemplate(ScriptCommandType.MouseClick, "Editor.Command.MouseClick.Title", "Editor.Command.MouseClick.Description"),
            CreateTemplate(ScriptCommandType.NextRound, "Editor.Command.NextRound.Title", "Editor.Command.NextRound.Description"),
            CreateTemplate(ScriptCommandType.Wait, "Editor.Command.Wait.Title", "Editor.Command.Wait.Description"),
            CreateTemplate(ScriptCommandType.ModifyMonkeyCoordinate, "Editor.Command.ModifyMonkeyCoordinate.Title", "Editor.Command.ModifyMonkeyCoordinate.Description"),
            CreateTemplate(ScriptCommandType.Comment, "Editor.Command.Comment.Title", "Editor.Command.Comment.Description")
        ];
    }

    public ScriptInstructionInstance CreateInstructionInstance(
        ScriptInstructionTemplate template,
        string defaultTargetBindingId,
        string defaultInventoryCode,
        string defaultActivatedAbilityCode)
    {
        ArgumentNullException.ThrowIfNull(template);

        var instruction = new ScriptInstructionInstance(template.Type, template.NameKey, template.DescriptionKey)
        {
            DisplayName = template.DisplayName,
            Description = template.Description
        };

        if (RequiresMonkeyObjectTarget(instruction))
        {
            instruction.TargetMonkeyBindingId = defaultTargetBindingId;
        }

        if (instruction.Type == ScriptCommandType.PlaceHeroInventory)
        {
            instruction.SelectedInventoryItem = defaultInventoryCode;
        }

        if (instruction.Type == ScriptCommandType.ActivateAbility)
        {
            instruction.SelectedActivatedAbility = defaultActivatedAbilityCode;
        }

        if (instruction.Type == ScriptCommandType.NextRound)
        {
            instruction.NextRoundAction = "PlayFastForward";
            instruction.NextRoundSendCount = 1;
        }

        if (instruction.Type == ScriptCommandType.MouseClick)
        {
            instruction.ClickCount = 1;
            instruction.ClickIntervalMilliseconds = 80;
        }

        if (instruction.Type == ScriptCommandType.Wait)
        {
            instruction.WaitMode = WaitModeType.Time.ToString();
            instruction.WaitTimeMilliseconds = 1000;
            instruction.WaitGoldAmount = 0;
            instruction.WaitRoundCount = 1;
            instruction.WaitColorHex = "#FFFFFF";
            instruction.WaitColorTolerance = 0;
        }

        return instruction;
    }

    public List<ScriptMonkeyObjectDocument> BuildMonkeyObjectDocuments(IEnumerable<ScriptInstructionInstance> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var documents = new List<ScriptMonkeyObjectDocument>();
        var placementOrder = 0;

        foreach (var instruction in instructions.Where(x => x.Type == ScriptCommandType.PlaceMonkey))
        {
            placementOrder++;
            documents.Add(new ScriptMonkeyObjectDocument
            {
                BindingId = instruction.MonkeyBindingId,
                ObjectId = instruction.MonkeyObjectId,
                SelectionCode = NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower),
                PlacementOrder = placementOrder
            });
        }

        return documents;
    }

    public List<ScriptInstructionDocument> BuildInstructionDocuments(IEnumerable<ScriptInstructionInstance> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        return instructions.Select(instruction => new ScriptInstructionDocument
        {
            CommandType = instruction.Type.ToString(),
            SelectedMonkeyTower = NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower),
            MonkeyBindingId = instruction.MonkeyBindingId,
            MonkeyObjectId = instruction.MonkeyObjectId,
            TargetMonkeyBindingId = instruction.TargetMonkeyBindingId,
            TargetMonkeyObjectId = instruction.TargetMonkeyObjectId,
            SelectedInventoryItem = instruction.SelectedInventoryItem,
            SelectedActivatedAbility = instruction.SelectedActivatedAbility,
            NextRoundAction = instruction.NextRoundAction,
            WaitMode = instruction.WaitMode,
            ClickCount = instruction.ClickCount,
            ClickIntervalMilliseconds = instruction.ClickIntervalMilliseconds,
            NextRoundSendCount = instruction.NextRoundSendCount,
            NextRoundOperationIntervalMilliseconds = instruction.NextRoundOperationIntervalMilliseconds,
            WaitTimeMilliseconds = instruction.WaitTimeMilliseconds,
            PlacementDetectionEnabled = instruction.PlacementDetectionEnabled,
            PlacementFailureAdjustmentEnabled = instruction.PlacementFailureAdjustmentEnabled,
            PlacementAttemptIntervalMilliseconds = instruction.PlacementAttemptIntervalMilliseconds,
            PlacementAdjustmentAttemptIntervalMilliseconds = instruction.PlacementAdjustmentAttemptIntervalMilliseconds,
            UpgradeDetectionEnabled = instruction.UpgradeDetectionEnabled,
            UpgradeDetectionIntervalMilliseconds = instruction.UpgradeDetectionIntervalMilliseconds,
            UpgradeOperationIntervalMilliseconds = instruction.UpgradeOperationIntervalMilliseconds,
            MonkeyPanelDetectionEnabled = instruction.MonkeyPanelDetectionEnabled,
            MonkeyPanelDetectionIntervalMilliseconds = instruction.MonkeyPanelDetectionIntervalMilliseconds,
            MonkeyPanelOperationIntervalMilliseconds = instruction.MonkeyPanelOperationIntervalMilliseconds,
            SellDetectionEnabled = instruction.SellDetectionEnabled,
            WaitGoldAmount = instruction.WaitGoldAmount,
            WaitRoundCount = instruction.WaitRoundCount,
            PositionX = instruction.PositionX,
            PositionY = instruction.PositionY,
            WaitColorCoordinateX = instruction.WaitColorCoordinateX,
            WaitColorCoordinateY = instruction.WaitColorCoordinateY,
            UpgradePath = instruction.UpgradePath.ToString(),
            UpgradeCount = instruction.UpgradeCount,
            SwitchDirection = instruction.SwitchDirection.ToString(),
            SwitchCount = instruction.SwitchCount,
            SelectedAbility = instruction.SelectedAbility.ToString(),
            RequiresAbilityCoordinate = instruction.RequiresAbilityCoordinate,
            AbilityCoordinateX = instruction.AbilityCoordinateX,
            AbilityCoordinateY = instruction.AbilityCoordinateY,
            WaitColorHex = instruction.WaitColorHex,
            WaitColorTolerance = instruction.WaitColorTolerance,
            CommentContent = instruction.CommentContent,
            IntervalToNextInstructionMs = instruction.IntervalToNextInstructionMs,
            Notes = instruction.Notes
        }).ToList();
    }

    public ScriptInstructionInstance CreateInstructionInstanceFromDocument(
        ScriptInstructionDocument document,
        IReadOnlyDictionary<string, ScriptMonkeyObjectDocument> monkeyObjectsByBindingId,
        IReadOnlyDictionary<ScriptCommandType, ScriptInstructionTemplate> templatesByType,
        string defaultTargetBindingId,
        string defaultInventoryCode,
        string defaultActivatedAbilityCode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(monkeyObjectsByBindingId);
        ArgumentNullException.ThrowIfNull(templatesByType);

        if (!Enum.TryParse<ScriptCommandType>(document.CommandType, true, out var commandType))
        {
            throw new InvalidDataException($"Unsupported script command type '{document.CommandType}'.");
        }

        if (!templatesByType.TryGetValue(commandType, out var template))
        {
            throw new InvalidDataException($"Missing instruction template for '{commandType}'.");
        }

        var instruction = CreateInstructionInstance(template, defaultTargetBindingId, defaultInventoryCode, defaultActivatedAbilityCode);
        var placeMonkeySnapshot = ResolveMonkeyObjectSnapshot(document.MonkeyBindingId, monkeyObjectsByBindingId);

        instruction.SelectedMonkeyTower = ResolveDocumentPlaceSelectionCode(document, placeMonkeySnapshot);
        instruction.MonkeyBindingId = document.MonkeyBindingId;
        instruction.MonkeyObjectId = ResolveDocumentMonkeyObjectId(document, placeMonkeySnapshot);
        instruction.TargetMonkeyBindingId = document.TargetMonkeyBindingId;
        instruction.TargetMonkeyObjectId = document.TargetMonkeyObjectId;
        if (!string.IsNullOrWhiteSpace(document.SelectedInventoryItem))
        {
            instruction.SelectedInventoryItem = document.SelectedInventoryItem;
        }

        if (!string.IsNullOrWhiteSpace(document.SelectedActivatedAbility))
        {
            instruction.SelectedActivatedAbility = document.SelectedActivatedAbility;
        }

        instruction.NextRoundAction = string.IsNullOrWhiteSpace(document.NextRoundAction)
            ? instruction.NextRoundAction
            : document.NextRoundAction;
        instruction.WaitMode = string.IsNullOrWhiteSpace(document.WaitMode)
            ? instruction.WaitMode
            : document.WaitMode;
        instruction.ClickCount = document.ClickCount <= 0 ? instruction.ClickCount : document.ClickCount;
        instruction.ClickIntervalMilliseconds = document.ClickIntervalMilliseconds < 0
            ? instruction.ClickIntervalMilliseconds
            : document.ClickIntervalMilliseconds;
        instruction.NextRoundSendCount = document.NextRoundSendCount <= 0 ? instruction.NextRoundSendCount : document.NextRoundSendCount;
        instruction.NextRoundOperationIntervalMilliseconds = document.NextRoundOperationIntervalMilliseconds is null or < 0
            ? instruction.NextRoundOperationIntervalMilliseconds
            : document.NextRoundOperationIntervalMilliseconds.Value;
        instruction.WaitTimeMilliseconds = document.WaitTimeMilliseconds < 0 ? instruction.WaitTimeMilliseconds : document.WaitTimeMilliseconds;
        instruction.PlacementDetectionEnabled = document.PlacementDetectionEnabled ?? instruction.PlacementDetectionEnabled;
        instruction.PlacementFailureAdjustmentEnabled = document.PlacementFailureAdjustmentEnabled ?? instruction.PlacementFailureAdjustmentEnabled;
        instruction.PlacementAttemptIntervalMilliseconds = document.PlacementAttemptIntervalMilliseconds is null or < 0
            ? instruction.PlacementAttemptIntervalMilliseconds
            : document.PlacementAttemptIntervalMilliseconds.Value;
        instruction.PlacementAdjustmentAttemptIntervalMilliseconds = document.PlacementAdjustmentAttemptIntervalMilliseconds is null or < 0
            ? instruction.PlacementAdjustmentAttemptIntervalMilliseconds
            : document.PlacementAdjustmentAttemptIntervalMilliseconds.Value;
        instruction.UpgradeDetectionEnabled = document.UpgradeDetectionEnabled ?? instruction.UpgradeDetectionEnabled;
        instruction.UpgradeDetectionIntervalMilliseconds = document.UpgradeDetectionIntervalMilliseconds is null or < 0
            ? instruction.UpgradeDetectionIntervalMilliseconds
            : document.UpgradeDetectionIntervalMilliseconds.Value;
        instruction.UpgradeOperationIntervalMilliseconds = document.UpgradeOperationIntervalMilliseconds is null or < 0
            ? instruction.UpgradeOperationIntervalMilliseconds
            : document.UpgradeOperationIntervalMilliseconds.Value;
        instruction.MonkeyPanelDetectionEnabled = document.MonkeyPanelDetectionEnabled ?? instruction.MonkeyPanelDetectionEnabled;
        instruction.MonkeyPanelDetectionIntervalMilliseconds = document.MonkeyPanelDetectionIntervalMilliseconds is null or < 0
            ? instruction.MonkeyPanelDetectionIntervalMilliseconds
            : document.MonkeyPanelDetectionIntervalMilliseconds.Value;
        instruction.MonkeyPanelOperationIntervalMilliseconds = document.MonkeyPanelOperationIntervalMilliseconds is null or < 0
            ? instruction.MonkeyPanelOperationIntervalMilliseconds
            : document.MonkeyPanelOperationIntervalMilliseconds.Value;
        instruction.SellDetectionEnabled = document.SellDetectionEnabled ?? instruction.SellDetectionEnabled;
        instruction.WaitGoldAmount = document.WaitGoldAmount;
        instruction.WaitRoundCount = document.WaitRoundCount <= 0 ? instruction.WaitRoundCount : document.WaitRoundCount;
        instruction.PositionX = document.PositionX;
        instruction.PositionY = document.PositionY;
        instruction.WaitColorCoordinateX = document.WaitColorCoordinateX;
        instruction.WaitColorCoordinateY = document.WaitColorCoordinateY;
        instruction.UpgradePath = Enum.TryParse<UpgradePathType>(document.UpgradePath, true, out var upgradePath)
            ? upgradePath
            : instruction.UpgradePath;
        instruction.UpgradeCount = document.UpgradeCount <= 0 ? instruction.UpgradeCount : document.UpgradeCount;
        instruction.SwitchDirection = Enum.TryParse<SwitchDirectionType>(document.SwitchDirection, true, out var switchDirection)
            ? switchDirection
            : instruction.SwitchDirection;
        instruction.SwitchCount = document.SwitchCount <= 0 ? instruction.SwitchCount : document.SwitchCount;
        instruction.SelectedAbility = Enum.TryParse<MonkeyAbilityType>(document.SelectedAbility, true, out var ability)
            ? ability
            : instruction.SelectedAbility;
        instruction.RequiresAbilityCoordinate = document.RequiresAbilityCoordinate;
        instruction.AbilityCoordinateX = document.AbilityCoordinateX;
        instruction.AbilityCoordinateY = document.AbilityCoordinateY;
        instruction.WaitColorHex = string.IsNullOrWhiteSpace(document.WaitColorHex) ? instruction.WaitColorHex : document.WaitColorHex;
        instruction.WaitColorTolerance = document.WaitColorTolerance;
        instruction.CommentContent = document.CommentContent;
        instruction.IntervalToNextInstructionMs = document.IntervalToNextInstructionMs;
        instruction.Notes = document.Notes;

        return instruction;
    }

    public List<ScriptInstructionInstance> CloneSnapshot(IEnumerable<ScriptInstructionInstance> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Select(CloneInstructionInstance).ToList();
    }

    public ScriptInstructionInstance CloneInstructionInstance(ScriptInstructionInstance source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ScriptInstructionInstance(source.Type, source.NameKey, source.DescriptionKey)
        {
            DisplayName = source.DisplayName,
            Description = source.Description,
            SelectedMonkeyTower = NormalizePlaceSelectionCode(source.SelectedMonkeyTower),
            MonkeyBindingId = source.MonkeyBindingId,
            MonkeyObjectId = source.MonkeyObjectId,
            TargetMonkeyBindingId = source.TargetMonkeyBindingId,
            TargetMonkeyObjectId = source.TargetMonkeyObjectId,
            SelectedInventoryItem = source.SelectedInventoryItem,
            SelectedActivatedAbility = source.SelectedActivatedAbility,
            NextRoundAction = source.NextRoundAction,
            ClickCount = source.ClickCount,
            ClickIntervalMilliseconds = source.ClickIntervalMilliseconds,
            NextRoundSendCount = source.NextRoundSendCount,
            NextRoundOperationIntervalMilliseconds = source.NextRoundOperationIntervalMilliseconds,
            WaitMode = source.WaitMode,
            WaitTimeMilliseconds = source.WaitTimeMilliseconds,
            PlacementDetectionEnabled = source.PlacementDetectionEnabled,
            PlacementFailureAdjustmentEnabled = source.PlacementFailureAdjustmentEnabled,
            PlacementAttemptIntervalMilliseconds = source.PlacementAttemptIntervalMilliseconds,
            PlacementAdjustmentAttemptIntervalMilliseconds = source.PlacementAdjustmentAttemptIntervalMilliseconds,
            UpgradeDetectionEnabled = source.UpgradeDetectionEnabled,
            UpgradeDetectionIntervalMilliseconds = source.UpgradeDetectionIntervalMilliseconds,
            UpgradeOperationIntervalMilliseconds = source.UpgradeOperationIntervalMilliseconds,
            MonkeyPanelDetectionEnabled = source.MonkeyPanelDetectionEnabled,
            MonkeyPanelDetectionIntervalMilliseconds = source.MonkeyPanelDetectionIntervalMilliseconds,
            MonkeyPanelOperationIntervalMilliseconds = source.MonkeyPanelOperationIntervalMilliseconds,
            SellDetectionEnabled = source.SellDetectionEnabled,
            WaitGoldAmount = source.WaitGoldAmount,
            WaitRoundCount = source.WaitRoundCount,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            WaitColorCoordinateX = source.WaitColorCoordinateX,
            WaitColorCoordinateY = source.WaitColorCoordinateY,
            UpgradePath = source.UpgradePath,
            UpgradeCount = source.UpgradeCount,
            SwitchDirection = source.SwitchDirection,
            SwitchCount = source.SwitchCount,
            SelectedAbility = source.SelectedAbility,
            RequiresAbilityCoordinate = source.RequiresAbilityCoordinate,
            AbilityCoordinateX = source.AbilityCoordinateX,
            AbilityCoordinateY = source.AbilityCoordinateY,
            WaitColorHex = source.WaitColorHex,
            WaitColorTolerance = source.WaitColorTolerance,
            CommentContent = source.CommentContent,
            IntervalToNextInstructionMs = source.IntervalToNextInstructionMs,
            Notes = source.Notes
        };
    }

    public List<ScriptInstructionInstance> CloneInstructionsForPaste(IEnumerable<ScriptInstructionInstance> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clonedInstructions = source.Select(CloneInstructionInstance).ToList();
        var pastedBindingIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instruction in clonedInstructions.Where(x => x.Type == ScriptCommandType.PlaceMonkey))
        {
            var originalBindingId = instruction.MonkeyBindingId;
            var newBindingId = CreateMonkeyBindingId();

            if (!string.IsNullOrWhiteSpace(originalBindingId) &&
                !pastedBindingIdMap.ContainsKey(originalBindingId))
            {
                pastedBindingIdMap[originalBindingId] = newBindingId;
            }

            instruction.MonkeyBindingId = newBindingId;
        }

        foreach (var instruction in clonedInstructions.Where(RequiresMonkeyObjectTarget))
        {
            if (!string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId) &&
                pastedBindingIdMap.TryGetValue(instruction.TargetMonkeyBindingId, out var remappedBindingId))
            {
                instruction.TargetMonkeyBindingId = remappedBindingId;
            }
        }

        return clonedInstructions;
    }

    public bool RequiresMonkeyObjectTarget(ScriptInstructionInstance instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        return instruction.Type is ScriptCommandType.UpgradeMonkey
            or ScriptCommandType.SwitchMonkeyTarget
            or ScriptCommandType.SetMonkeyAbility
            or ScriptCommandType.SellMonkey
            or ScriptCommandType.ModifyMonkeyCoordinate;
    }

    public static string BuildMonkeyObjectKey(MonkeyTowerType towerType, int index)
    {
        return $"{towerType}:{index}";
    }

    public static string CreateMonkeyBindingId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static bool TryParseMonkeyObjectKey(string? objectKey, out MonkeyTowerType towerType, out int index)
    {
        towerType = default;
        index = 0;
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return false;
        }

        var separatorIndex = objectKey.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= objectKey.Length - 1)
        {
            return false;
        }

        var towerText = objectKey.Substring(0, separatorIndex);
        var indexText = objectKey.Substring(separatorIndex + 1);
        return Enum.TryParse(towerText, out towerType) &&
               int.TryParse(indexText, out index) &&
               index > 0;
    }

    public static string BuildHeroObjectKey(HeroType heroType)
    {
        return $"Hero:{heroType}";
    }

    public static string BuildTowerSelectionCode(MonkeyTowerType towerType)
    {
        return $"Tower:{towerType}";
    }

    public static string NormalizePlaceSelectionCode(string? selectionCode)
    {
        if (string.IsNullOrWhiteSpace(selectionCode))
        {
            return BuildTowerSelectionCode(MonkeyTowerType.DartMonkey);
        }

        if (selectionCode.StartsWith("Tower:", StringComparison.OrdinalIgnoreCase) ||
            selectionCode.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase))
        {
            return selectionCode;
        }

        return $"Tower:{selectionCode}";
    }

    public static bool IsHeroSelectionCode(string? selectionCode)
    {
        return !string.IsNullOrWhiteSpace(selectionCode) &&
               NormalizePlaceSelectionCode(selectionCode).StartsWith("Hero:", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseTowerSelection(string selectionCode, out MonkeyTowerType towerType)
    {
        towerType = default;
        if (!selectionCode.StartsWith("Tower:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = selectionCode.Substring("Tower:".Length);
        return Enum.TryParse(raw, out towerType);
    }

    public static bool TryParseHeroSelection(string selectionCode, out HeroType heroType)
    {
        heroType = default;
        if (!selectionCode.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = selectionCode.Substring("Hero:".Length);
        return Enum.TryParse(raw, out heroType);
    }

    private static ScriptInstructionTemplate CreateTemplate(ScriptCommandType type, string nameKey, string descriptionKey)
    {
        return new ScriptInstructionTemplate
        {
            Type = type,
            NameKey = nameKey,
            DescriptionKey = descriptionKey
        };
    }

    private static ScriptMonkeyObjectDocument? ResolveMonkeyObjectSnapshot(
        string bindingId,
        IReadOnlyDictionary<string, ScriptMonkeyObjectDocument> monkeyObjectsByBindingId)
    {
        return string.IsNullOrWhiteSpace(bindingId)
            ? null
            : monkeyObjectsByBindingId.GetValueOrDefault(bindingId);
    }

    private static string ResolveDocumentPlaceSelectionCode(
        ScriptInstructionDocument document,
        ScriptMonkeyObjectDocument? monkeyObject)
    {
        var selectionCode = string.IsNullOrWhiteSpace(document.SelectedMonkeyTower)
            ? monkeyObject?.SelectionCode
            : document.SelectedMonkeyTower;
        return NormalizePlaceSelectionCode(selectionCode);
    }

    private static string ResolveDocumentMonkeyObjectId(
        ScriptInstructionDocument document,
        ScriptMonkeyObjectDocument? monkeyObject)
    {
        if (!string.IsNullOrWhiteSpace(document.MonkeyObjectId))
        {
            return document.MonkeyObjectId;
        }

        return monkeyObject?.ObjectId ?? string.Empty;
    }
}

