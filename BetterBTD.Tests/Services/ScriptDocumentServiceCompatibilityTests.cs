using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

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
                Metadata = new ScriptMetadataDocument
                {
                    Name = "compat-current"
                },
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

            Assert.Equal(ScriptDocumentSourceKind.Current, result.SourceKind);
            Assert.Empty(result.Warnings);
            Assert.Equal("compat-current", result.Document.Metadata.Name);
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
    public void LoadCompatible_LegacyScript_ReturnsLegacySourceKindWithWarnings()
    {
        var result = ScriptDocumentService.Instance.LoadCompatible(GetLegacySampleFilePath());

        Assert.Equal(ScriptDocumentSourceKind.LegacyBtd6, result.SourceKind);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(ScriptDocumentFormat.Schema, result.Document.Schema);
        Assert.NotEmpty(result.Document.Instructions);
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
                Metadata = new ScriptMetadataDocument
                {
                    Name = "optimized-save"
                },
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

    private static string GetLegacySampleFilePath()
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                ".."));

        return Directory.GetFiles(repoRoot, "*.btd6", SearchOption.TopDirectoryOnly).Single();
    }
}
