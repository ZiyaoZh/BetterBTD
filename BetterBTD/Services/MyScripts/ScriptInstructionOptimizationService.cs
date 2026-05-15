using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Services.MyScripts;

public sealed class ScriptInstructionOptimizationService
{
    private static readonly Lazy<ScriptInstructionOptimizationService> InstanceHolder = new(() => new ScriptInstructionOptimizationService());

    private ScriptInstructionOptimizationService()
    {
    }

    public static ScriptInstructionOptimizationService Instance => InstanceHolder.Value;

    public ScriptDocument OptimizeDocument(ScriptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new ScriptDocument
        {
            Schema = document.Schema,
            FormatVersion = document.FormatVersion,
            Metadata = CloneMetadata(document.Metadata),
            MonkeyObjects = document.MonkeyObjects.Select(CloneMonkeyObject).ToList(),
            Instructions = OptimizeInstructions(document.Instructions)
        };
    }

    public List<ScriptInstructionDocument> OptimizeInstructions(IReadOnlyList<ScriptInstructionDocument> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var optimized = new List<ScriptInstructionDocument>(instructions.Count);

        foreach (var instruction in instructions)
        {
            var candidate = CloneInstruction(instruction);
            if (optimized.Count == 0)
            {
                optimized.Add(candidate);
                continue;
            }

            var previous = optimized[^1];
            if (TryMerge(previous, candidate))
            {
                continue;
            }

            optimized.Add(candidate);
        }

        return optimized;
    }

    private static bool TryMerge(ScriptInstructionDocument previous, ScriptInstructionDocument current)
    {
        if (previous.IntervalToNextInstructionMs != current.IntervalToNextInstructionMs)
        {
            return false;
        }

        if (CanMergeUpgrade(previous, current))
        {
            previous.UpgradeCount += Math.Max(1, current.UpgradeCount);
            previous.Notes = MergeNotes(previous.Notes, current.Notes);
            return true;
        }

        if (CanMergeSendNextRound(previous, current))
        {
            previous.NextRoundSendCount += Math.Max(1, current.NextRoundSendCount);
            previous.Notes = MergeNotes(previous.Notes, current.Notes);
            return true;
        }

        return false;
    }

    private static bool CanMergeUpgrade(ScriptInstructionDocument previous, ScriptInstructionDocument current)
    {
        return string.Equals(previous.CommandType, ScriptCommandType.UpgradeMonkey.ToString(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(current.CommandType, ScriptCommandType.UpgradeMonkey.ToString(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(previous.TargetMonkeyBindingId, current.TargetMonkeyBindingId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(previous.TargetMonkeyObjectId, current.TargetMonkeyObjectId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(previous.UpgradePath, current.UpgradePath, StringComparison.OrdinalIgnoreCase) &&
               previous.UpgradeDetectionEnabled == current.UpgradeDetectionEnabled &&
               previous.UpgradeDetectionIntervalMilliseconds == current.UpgradeDetectionIntervalMilliseconds &&
               previous.UpgradeOperationIntervalMilliseconds == current.UpgradeOperationIntervalMilliseconds;
    }

    private static bool CanMergeSendNextRound(ScriptInstructionDocument previous, ScriptInstructionDocument current)
    {
        return string.Equals(previous.CommandType, ScriptCommandType.NextRound.ToString(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(current.CommandType, ScriptCommandType.NextRound.ToString(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(previous.NextRoundAction, "SendNextRound", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(current.NextRoundAction, "SendNextRound", StringComparison.OrdinalIgnoreCase);
    }

    private static string MergeNotes(string? previousNotes, string? currentNotes)
    {
        var left = previousNotes?.Trim() ?? string.Empty;
        var right = currentNotes?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(left))
        {
            return right;
        }

        if (string.IsNullOrWhiteSpace(right) || string.Equals(left, right, StringComparison.Ordinal))
        {
            return left;
        }

        return $"{left}{Environment.NewLine}{right}";
    }

    private static ScriptMetadataDocument CloneMetadata(ScriptMetadataDocument metadata)
    {
        return new ScriptMetadataDocument
        {
            ScriptVersion = metadata.ScriptVersion,
            Category = metadata.Category,
            Name = metadata.Name,
            Description = metadata.Description,
            Map = metadata.Map,
            Difficulty = metadata.Difficulty,
            Mode = metadata.Mode,
            Hero = metadata.Hero
        };
    }

    private static ScriptMonkeyObjectDocument CloneMonkeyObject(ScriptMonkeyObjectDocument monkeyObject)
    {
        return new ScriptMonkeyObjectDocument
        {
            BindingId = monkeyObject.BindingId,
            ObjectId = monkeyObject.ObjectId,
            SelectionCode = monkeyObject.SelectionCode,
            PlacementOrder = monkeyObject.PlacementOrder
        };
    }

    private static ScriptInstructionDocument CloneInstruction(ScriptInstructionDocument instruction)
    {
        return new ScriptInstructionDocument
        {
            CommandType = instruction.CommandType,
            SelectedMonkeyTower = instruction.SelectedMonkeyTower,
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
            UpgradePath = instruction.UpgradePath,
            UpgradeCount = instruction.UpgradeCount,
            SwitchDirection = instruction.SwitchDirection,
            SwitchCount = instruction.SwitchCount,
            SelectedAbility = instruction.SelectedAbility,
            RequiresAbilityCoordinate = instruction.RequiresAbilityCoordinate,
            AbilityCoordinateX = instruction.AbilityCoordinateX,
            AbilityCoordinateY = instruction.AbilityCoordinateY,
            WaitColorHex = instruction.WaitColorHex,
            WaitColorTolerance = instruction.WaitColorTolerance,
            CommentContent = instruction.CommentContent,
            IntervalToNextInstructionMs = instruction.IntervalToNextInstructionMs,
            Notes = instruction.Notes
        };
    }
}

