using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class ScriptInstructionOptimizationServiceTests
{
    [Fact]
    public void OptimizeInstructions_ConsecutiveUpgradeInstructions_MergesUpgradeCount()
    {
        var instructions = new[]
        {
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                TargetMonkeyObjectId = "DartMonkey:1",
                UpgradePath = UpgradePathType.Top.ToString(),
                UpgradeCount = 1,
                IntervalToNextInstructionMs = 0,
                Notes = "first"
            },
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                TargetMonkeyObjectId = "DartMonkey:1",
                UpgradePath = UpgradePathType.Top.ToString(),
                UpgradeCount = 1,
                IntervalToNextInstructionMs = 0,
                Notes = "second"
            },
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                TargetMonkeyObjectId = "DartMonkey:1",
                UpgradePath = UpgradePathType.Top.ToString(),
                UpgradeCount = 1,
                IntervalToNextInstructionMs = 0,
                Notes = "third"
            }
        };

        var optimized = ScriptInstructionOptimizationService.Instance.OptimizeInstructions(instructions);

        var merged = Assert.Single(optimized);
        Assert.Equal(3, merged.UpgradeCount);
        Assert.Contains("first", merged.Notes);
        Assert.Contains("second", merged.Notes);
        Assert.Contains("third", merged.Notes);
    }

    [Fact]
    public void OptimizeInstructions_ConsecutiveSendNextRoundInstructions_MergesSendCount()
    {
        var instructions = new[]
        {
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.NextRound.ToString(),
                NextRoundAction = "SendNextRound",
                NextRoundSendCount = 1,
                IntervalToNextInstructionMs = 0
            },
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.NextRound.ToString(),
                NextRoundAction = "SendNextRound",
                NextRoundSendCount = 2,
                IntervalToNextInstructionMs = 0
            }
        };

        var optimized = ScriptInstructionOptimizationService.Instance.OptimizeInstructions(instructions);

        var merged = Assert.Single(optimized);
        Assert.Equal(3, merged.NextRoundSendCount);
    }

    [Fact]
    public void OptimizeInstructions_NonZeroInterval_DoesNotMerge()
    {
        var instructions = new[]
        {
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                TargetMonkeyObjectId = "DartMonkey:1",
                UpgradePath = UpgradePathType.Top.ToString(),
                UpgradeCount = 1,
                IntervalToNextInstructionMs = 100
            },
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                TargetMonkeyObjectId = "DartMonkey:1",
                UpgradePath = UpgradePathType.Top.ToString(),
                UpgradeCount = 1,
                IntervalToNextInstructionMs = 0
            }
        };

        var optimized = ScriptInstructionOptimizationService.Instance.OptimizeInstructions(instructions);

        Assert.Equal(2, optimized.Count);
    }
}
