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
