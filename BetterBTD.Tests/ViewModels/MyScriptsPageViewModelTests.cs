using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ViewModels;

public sealed class MyScriptsPageViewModelTests
{
    [Fact]
    public async Task EnsureInitializedAsync_LoadsScriptsWithoutBlockingConstructor()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-my-scripts-tests-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "async-load-script.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Map = GameMapType.MonkeyMeadow.ToString(),
                    Difficulty = StageDifficulty.Easy.ToString(),
                    Mode = StageMode.Standard.ToString()
                }
            });

            var libraryService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            libraryService.ImportScript(sourceFilePath);

            var viewModel = new MyScriptsPageViewModel(LocalizationService.Instance, libraryService);

            Assert.False(viewModel.HasScripts);
            Assert.Empty(viewModel.Scripts);
            Assert.True(viewModel.IsLoadingScripts);

            await viewModel.EnsureInitializedAsync();

            var script = Assert.Single(viewModel.Scripts);
            Assert.True(viewModel.HasScripts);
            Assert.False(viewModel.IsLoadingScripts);
            Assert.Equal("async-load-script", script.DisplayName);
            Assert.True(viewModel.LoadingProgressMaximum >= 1d);
            Assert.True(viewModel.LoadingProgressValue >= 0d);
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
    public async Task BatchManagement_SelectsVisibleScriptsAndClearsSelectionWhenDone()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-my-scripts-tests-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");

        try
        {
            Directory.CreateDirectory(sourceDirectory);
            var firstSourceFilePath = Path.Combine(sourceDirectory, "first-script.btd");
            var secondSourceFilePath = Path.Combine(sourceDirectory, "second-script.btd");
            ScriptDocumentService.Instance.Save(firstSourceFilePath, new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Map = GameMapType.MonkeyMeadow.ToString(),
                    Difficulty = StageDifficulty.Easy.ToString(),
                    Mode = StageMode.Standard.ToString(),
                    Tags = ["first"]
                }
            });
            ScriptDocumentService.Instance.Save(secondSourceFilePath, new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Map = GameMapType.DarkCastle.ToString(),
                    Difficulty = StageDifficulty.Hard.ToString(),
                    Mode = StageMode.CHIMPS.ToString(),
                    Tags = ["second"]
                }
            });

            var libraryService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            libraryService.ImportScript(firstSourceFilePath);
            libraryService.ImportScript(secondSourceFilePath);
            var viewModel = new MyScriptsPageViewModel(LocalizationService.Instance, libraryService);
            await viewModel.EnsureInitializedAsync();

            viewModel.EnterBatchManagementCommand.Execute(null);
            viewModel.ScriptSearchText = "first-script";
            viewModel.AreAllVisibleScriptsSelected = true;

            Assert.True(viewModel.IsBatchManagementMode);
            Assert.Single(viewModel.Scripts);
            Assert.Equal(1, viewModel.BatchSelectedScriptCount);
            Assert.True(viewModel.HasBatchSelection);
            Assert.True(viewModel.RemoveBatchSelectionCommand.CanExecute(null));

            viewModel.ScriptSearchText = string.Empty;
            Assert.False(viewModel.AreAllVisibleScriptsSelected);
            viewModel.AreAllVisibleScriptsSelected = true;
            Assert.Equal(2, viewModel.BatchSelectedScriptCount);

            viewModel.ExitBatchManagementCommand.Execute(null);

            Assert.False(viewModel.IsBatchManagementMode);
            Assert.Equal(0, viewModel.BatchSelectedScriptCount);
            Assert.All(viewModel.Scripts, script => Assert.False(script.IsSelectedForBatch));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }
}
