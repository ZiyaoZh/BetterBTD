using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Tests.Services;

public sealed class ScriptDocumentServiceCompatibilityTests
{
    [Fact]
    public void LoadCompatible_CurrentScript_ReturnsCurrentSourceKindWithoutWarnings()
    {
        var service = ScriptDocumentService.Instance;
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.btd");

        try
        {
            service.Save(filePath, new ScriptDocument
            {
                Instructions =
                [
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.Comment.ToString(),
                        CommentContent = "ok"
                    }
                ]
            });

            var result = service.LoadCompatible(filePath);
            var json = File.ReadAllText(filePath);

            Assert.Equal(ScriptDocumentSourceKind.Current, result.SourceKind);
            Assert.Empty(result.Warnings);
            Assert.DoesNotContain("\"name\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"category\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void Save_ConsecutiveOptimizableInstructions_PersistsOptimizedScript()
    {
        var service = ScriptDocumentService.Instance;
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.btd");

        try
        {
            service.Save(filePath, new ScriptDocument
            {
                Instructions =
                [
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                        TargetMonkeyBindingId = "dart-bind",
                        TargetMonkeyObjectId = "DartMonkey:1",
                        UpgradePath = UpgradePathType.Top.ToString(),
                        UpgradeCount = 1,
                        IntervalToNextInstructionMs = 0
                    },
                    new ScriptInstructionDocument
                    {
                        CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                        TargetMonkeyBindingId = "dart-bind",
                        TargetMonkeyObjectId = "DartMonkey:1",
                        UpgradePath = UpgradePathType.Top.ToString(),
                        UpgradeCount = 2,
                        IntervalToNextInstructionMs = 0
                    },
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
                ]
            });

            var loaded = service.Load(filePath);

            Assert.Equal(2, loaded.Instructions.Count);
            Assert.Equal(3, loaded.Instructions[0].UpgradeCount);
            Assert.Equal(3, loaded.Instructions[1].NextRoundSendCount);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}

