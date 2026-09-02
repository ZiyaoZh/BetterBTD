using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services.ChildSession;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ViewModels;

public sealed class CollectionScriptBindingWindowViewModelTests
{
    [Fact]
    public void Constructor_LoadsCollectionModesAndSelectsRequestedMode()
    {
        var rootDirectory = CreateRootDirectory();

        try
        {
            var service = CreateLibraryService(rootDirectory);
            using var viewModel = CreateViewModel(service, "fast-track");

            Assert.Equal(ManagedScriptCollectionModeCatalog.Modes.Count, viewModel.Modes.Count);
            Assert.Equal("fast-track", viewModel.SelectedMode?.ModeKey);
            Assert.All(
                viewModel.Modes,
                mode => Assert.Equal(
                    GameElementCatalog.Maps.Count(map => map.Tier == MapDifficultyTier.Expert),
                    mode.Rows.Count));
            Assert.False(viewModel.IsDirty);
            Assert.False(viewModel.SaveCommand.CanExecute(null));
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void SaveCommand_PersistsAllEditedCollectionBindings()
    {
        var rootDirectory = CreateRootDirectory();

        try
        {
            var service = CreateLibraryService(rootDirectory);
            var imported = ImportCollectionScript(service, rootDirectory, GameMapType.DarkCastle);
            using var viewModel = CreateViewModel(service, "simple");
            var row = Assert.Single(viewModel.SelectedMode!.Rows, item =>
                item.SlotId == ManagedScriptSlotIdFactory.CreateCollectionSlotId("simple", GameMapType.DarkCastle));
            var choice = Assert.Single(row.ScriptChoices, item => item.ScriptId == imported.ScriptId);

            row.SelectedScript = choice;

            Assert.True(viewModel.IsDirty);
            Assert.True(viewModel.SaveCommand.CanExecute(null));

            viewModel.SaveCommand.Execute(null);

            Assert.False(viewModel.IsDirty);
            Assert.False(viewModel.SaveCommand.CanExecute(null));
            Assert.True(service.TryResolveSlotBinding(row.SlotId, out var scriptId, out var filePath));
            Assert.Equal(imported.ScriptId, scriptId);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void DisposeWithoutSaving_DoesNotPersistEditedBinding()
    {
        var rootDirectory = CreateRootDirectory();

        try
        {
            var service = CreateLibraryService(rootDirectory);
            var imported = ImportCollectionScript(service, rootDirectory, GameMapType.DarkCastle);
            var viewModel = CreateViewModel(service, "simple");
            var row = Assert.Single(viewModel.SelectedMode!.Rows, item =>
                item.SlotId == ManagedScriptSlotIdFactory.CreateCollectionSlotId("simple", GameMapType.DarkCastle));

            row.SelectedScript = Assert.Single(row.ScriptChoices, item => item.ScriptId == imported.ScriptId);
            viewModel.Dispose();

            Assert.False(service.TryResolveSlotBinding(row.SlotId, out _, out _));
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void RefreshWritableState_DisablesEditingAfterChildSessionTakesControl()
    {
        var rootDirectory = CreateRootDirectory();

        try
        {
            var service = CreateLibraryService(rootDirectory);
            using var viewModel = CreateViewModel(service, "simple");
            Assert.True(viewModel.CanEdit);

            ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
                BetterBtdInstanceRole.ChildSession,
                null,
                null));
            viewModel.RefreshWritableState();

            Assert.False(viewModel.CanEdit);
            Assert.False(viewModel.CopyModeCommand.CanExecute(null));
            Assert.False(viewModel.ClearModeCommand.CanExecute(null));
            Assert.False(viewModel.SaveCommand.CanExecute(null));
        }
        finally
        {
            ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
                BetterBtdInstanceRole.Primary,
                null,
                null));
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    private static CollectionScriptBindingWindowViewModel CreateViewModel(
        ManagedScriptLibraryService service,
        string initialModeKey)
    {
        return new CollectionScriptBindingWindowViewModel(
            LocalizationService.Instance,
            AppDialogService.Instance,
            service,
            initialModeKey,
            static () => { });
    }

    private static ManagedScriptAssetEntry ImportCollectionScript(
        ManagedScriptLibraryService service,
        string rootDirectory,
        GameMapType map)
    {
        var sourceFilePath = Path.Combine(rootDirectory, "source", $"{map}.btd");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
        ScriptDocumentService.Instance.Save(sourceFilePath, new ScriptDocument
        {
            Metadata = new ScriptMetadataDocument
            {
                Map = map.ToString(),
                Difficulty = StageDifficulty.Hard.ToString(),
                Mode = StageMode.CHIMPS.ToString(),
                Hero = HeroType.Quincy.ToString(),
                Tags = ["collection"]
            }
        });
        return service.ImportScript(sourceFilePath);
    }

    private static ManagedScriptLibraryService CreateLibraryService(string rootDirectory)
    {
        return new ManagedScriptLibraryService(
            Path.Combine(rootDirectory, "managed"),
            ScriptDocumentService.Instance,
            ManagedScriptSlotCatalogService.Instance);
    }

    private static string CreateRootDirectory()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-collection-binding-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);
        return rootDirectory;
    }
}
