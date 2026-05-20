using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class ManagedAutoTaskScriptResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesManagedSlotBindingWhenAvailable()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-resolver-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "resolver-script.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, new ScriptDocument
            {
                Metadata = new ScriptMetadataDocument
                {
                    Map = GameMapType.MonkeyMeadow.ToString(),
                    Difficulty = StageDifficulty.Easy.ToString(),
                    Mode = StageMode.Standard.ToString(),
                    Hero = HeroType.Quincy.ToString(),
                    Tags = ["black-border"]
                }
            });

            var libraryService = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);
            var imported = libraryService.ImportScript(sourceFilePath);
            var slotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);
            libraryService.SetBinding(slotId, imported.ScriptId);

            var resolver = new ManagedAutoTaskScriptResolver(libraryService);
            var result = await resolver.ResolveAsync(
                new AutoTaskScriptQuery
                {
                    Kind = AutoTaskKind.BlackBorder,
                    SlotId = slotId,
                    StageTarget = new StageEntryTarget
                    {
                        Map = GameMapType.MonkeyMeadow,
                        Difficulty = StageDifficulty.Easy,
                        Mode = StageMode.Standard
                    }
                },
                new AutoTaskRuntimeState(new AutoTaskRequest
                {
                    Kind = AutoTaskKind.BlackBorder,
                    StageTarget = new StageEntryTarget
                    {
                        Map = GameMapType.MonkeyMeadow,
                        Difficulty = StageDifficulty.Easy,
                        Mode = StageMode.Standard
                    }
                }));

            Assert.True(result.IsResolved);
            Assert.EndsWith(".btd", result.FilePath, StringComparison.OrdinalIgnoreCase);
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
