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
}
