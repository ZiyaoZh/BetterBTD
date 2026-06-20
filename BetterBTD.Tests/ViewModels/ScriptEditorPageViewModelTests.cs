using System.Reflection;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;
using BetterBTD.Services.MyScripts;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ViewModels;

public sealed class ScriptEditorPageViewModelTests
{
    [Fact]
    public void SaveScriptDocument_RefreshesInstructionSequenceWithOptimizedInstructions_AndImportsIntoLibrary()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-editor-tests-{Guid.NewGuid():N}");
        var managedRootDirectory = Path.Combine(rootDirectory, "managed");
        var filePath = Path.Combine(rootDirectory, "external", "optimized-script.btd");
        var libraryService = new ManagedScriptLibraryService(
            managedRootDirectory,
            ScriptDocumentService.Instance,
            ManagedScriptSlotCatalogService.Instance);
        var viewModel = new ScriptEditorPageViewModel(LocalizationService.Instance, libraryService);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            viewModel.ImportScriptDocument(new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Tags = ["collection", "custom-tag"]
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

            Assert.Equal(4, viewModel.InstructionSequence.Count);

            viewModel.SaveScriptDocument(filePath);
            var saved = ScriptDocumentService.Instance.Load(filePath);

            Assert.Equal(filePath, viewModel.CurrentScriptFilePath);
            Assert.Equal(2, viewModel.InstructionSequence.Count);
            Assert.Equal(3, viewModel.InstructionSequence[0].UpgradeCount);
            Assert.Equal(3, viewModel.InstructionSequence[1].NextRoundSendCount);
            Assert.Equal(["collection", "custom-tag"], saved.Metadata.Tags);

            var snapshot = libraryService.GetSnapshot();
            var importedScript = Assert.Single(snapshot.Scripts);
            Assert.Equal("optimized-script", importedScript.DisplayName);
            Assert.Equal("optimized-script.btd", importedScript.SourceFileName);
            Assert.False(importedScript.HasMissingFile);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadManagedScript_UsesManagedDisplayNameForRuntimeTitle_AndResaveDoesNotDuplicateLibraryEntry()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-editor-tests-{Guid.NewGuid():N}");
        var externalFilePath = Path.Combine(rootDirectory, "external", "friendly-name.btd");
        var managedRootDirectory = Path.Combine(rootDirectory, "managed");
        var libraryService = new ManagedScriptLibraryService(
            managedRootDirectory,
            ScriptDocumentService.Instance,
            ManagedScriptSlotCatalogService.Instance);
        var viewModel = new ScriptEditorPageViewModel(LocalizationService.Instance, libraryService);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(externalFilePath)!);
            ScriptDocumentService.Instance.Save(externalFilePath, new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Description = "before-update"
                }
            });

            var importedScript = libraryService.ImportScript(externalFilePath);

            viewModel.LoadScriptDocument(importedScript.StoredFilePath);

            var resolveDisplayNameMethod = typeof(ScriptEditorPageViewModel).GetMethod(
                "ResolveExecutionScriptDisplayName",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolveDisplayNameMethod);

            var runtimeDisplayName = (string?)resolveDisplayNameMethod!.Invoke(viewModel, null);
            Assert.Equal("friendly-name", runtimeDisplayName);

            viewModel.ScriptDescription = "after-update";
            viewModel.SaveScriptDocument(importedScript.StoredFilePath);

            var snapshot = libraryService.GetSnapshot();
            var script = Assert.Single(snapshot.Scripts);
            Assert.Equal(importedScript.ScriptId, script.ScriptId);
            Assert.Equal("friendly-name", script.DisplayName);
            Assert.Equal("friendly-name.btd", script.SourceFileName);
            Assert.Equal("after-update", script.Description);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveScriptDocument_ToDifferentPath_AssignsNewScriptIdAndDoesNotOverwriteOriginalManagedScript()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-editor-tests-{Guid.NewGuid():N}");
        var originalFilePath = Path.Combine(rootDirectory, "external", "original-script.btd");
        var savedAsFilePath = Path.Combine(rootDirectory, "external", "saved-as-script.btd");
        var managedRootDirectory = Path.Combine(rootDirectory, "managed");
        var libraryService = new ManagedScriptLibraryService(
            managedRootDirectory,
            ScriptDocumentService.Instance,
            ManagedScriptSlotCatalogService.Instance);
        var viewModel = new ScriptEditorPageViewModel(LocalizationService.Instance, libraryService);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalFilePath)!);
            ScriptDocumentService.Instance.Save(originalFilePath, new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Description = "original"
                }
            });

            var originalImported = libraryService.ImportScript(originalFilePath);
            viewModel.LoadScriptDocument(originalImported.StoredFilePath);
            viewModel.ScriptDescription = "saved-as";

            viewModel.SaveScriptDocument(savedAsFilePath);

            var originalDocument = ScriptDocumentService.Instance.Load(originalFilePath);
            var savedAsDocument = ScriptDocumentService.Instance.Load(savedAsFilePath);
            var snapshotAfterSaveAs = libraryService.GetSnapshot();

            Assert.NotEqual(originalDocument.Metadata.ScriptId, savedAsDocument.Metadata.ScriptId);
            Assert.Equal(2, snapshotAfterSaveAs.Scripts.Count);
            Assert.Contains(snapshotAfterSaveAs.Scripts, script =>
                script.ScriptId == originalDocument.Metadata.ScriptId &&
                script.Description == "original");
            Assert.Contains(snapshotAfterSaveAs.Scripts, script =>
                script.ScriptId == savedAsDocument.Metadata.ScriptId &&
                script.Description == "saved-as");

            var reimportedSavedAs = libraryService.ImportScript(savedAsFilePath);
            var snapshotAfterReimport = libraryService.GetSnapshot();

            Assert.Equal(savedAsDocument.Metadata.ScriptId, reimportedSavedAs.ScriptId);
            Assert.Equal(2, snapshotAfterReimport.Scripts.Count);
            Assert.Contains(snapshotAfterReimport.Scripts, script =>
                script.ScriptId == originalDocument.Metadata.ScriptId &&
                script.Description == "original");
            Assert.Contains(snapshotAfterReimport.Scripts, script =>
                script.ScriptId == savedAsDocument.Metadata.ScriptId &&
                script.Description == "saved-as");
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void AddTargetedMonkeyInstruction_DefaultsToLastMonkeyObject()
    {
        var viewModel = new ScriptEditorPageViewModel(LocalizationService.Instance);
        viewModel.ImportScriptDocument(new ScriptDocument
        {
            Instructions =
            [
                new ScriptInstructionDocument
                {
                    CommandType = ScriptCommandType.PlaceMonkey.ToString(),
                    MonkeyBindingId = "first-bind",
                    MonkeyObjectId = "DartMonkey:1",
                    SelectedMonkeyTower = "Tower:DartMonkey"
                },
                new ScriptInstructionDocument
                {
                    CommandType = ScriptCommandType.PlaceMonkey.ToString(),
                    MonkeyBindingId = "second-bind",
                    MonkeyObjectId = "BoomerangMonkey:1",
                    SelectedMonkeyTower = "Tower:BoomerangMonkey"
                }
            ]
        });
        var upgradeTemplate = viewModel.InstructionLibrary.Single(x => x.Type == ScriptCommandType.UpgradeMonkey);

        viewModel.AddInstructionToSequenceCommand.Execute(upgradeTemplate);

        var addedInstruction = viewModel.InstructionSequence.Last();
        Assert.Equal(ScriptCommandType.UpgradeMonkey, addedInstruction.Type);
        Assert.Equal("second-bind", addedInstruction.TargetMonkeyBindingId);
        Assert.Equal("BoomerangMonkey:1", addedInstruction.TargetMonkeyObjectId);
    }

    [Fact]
    public void AddScriptTagCommand_ResolvesBuiltInAliasAndKeepsCustomTags()
    {
        var viewModel = new ScriptEditorPageViewModel(LocalizationService.Instance);

        viewModel.PendingTagInput = "Black Border";
        viewModel.AddScriptTagCommand.Execute(null);

        viewModel.PendingTagInput = "custom-run";
        viewModel.AddScriptTagCommand.Execute(null);

        viewModel.PendingTagInput = "black-border";
        viewModel.AddScriptTagCommand.Execute(null);

        var exported = viewModel.ExportScriptDocument();

        Assert.Equal(["black-border", "custom-run"], exported.Metadata.Tags);
        Assert.Collection(
            viewModel.SelectedTagOptions,
            first =>
            {
                Assert.Equal("black-border", first.Code);
                Assert.Equal(ScriptTagCatalog.GetDisplayName("black-border"), first.DisplayName);
            },
            second =>
            {
                Assert.Equal("custom-run", second.Code);
                Assert.Equal("custom-run", second.DisplayName);
            });
    }
}
