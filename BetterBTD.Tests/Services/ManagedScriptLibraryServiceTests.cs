using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Tests.Services;

public sealed class ManagedScriptLibraryServiceTests
{
    [Fact]
    public void ImportBindExportFlow_StoresManagedScriptAndResolvesSlotBinding()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "sample-script.btd");
        var exportFilePath = Path.Combine(rootDirectory, "export", "sample-script-copy.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, CreateDocument(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard,
                ["black-border", "custom-tag"]));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportScript(sourceFilePath);
            var blackBorderSlotId = ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
                GameMapType.MonkeyMeadow,
                StageDifficulty.Easy,
                StageMode.Standard);

            service.SetBinding(blackBorderSlotId, imported.ScriptId);
            service.ExportScript(imported.ScriptId, exportFilePath);

            var snapshot = service.GetSnapshot();
            var script = Assert.Single(snapshot.Scripts);
            var slot = snapshot.Slots.First(x => x.Definition.SlotId == blackBorderSlotId);

            Assert.Equal("sample-script", script.DisplayName);
            Assert.Equal(GameMapType.MonkeyMeadow, script.Map);
            Assert.Equal(StageDifficulty.Easy, script.Difficulty);
            Assert.Equal(StageMode.Standard, script.Mode);
            Assert.Equal(1, script.BindingCount);
            Assert.False(script.HasMissingFile);
            Assert.Equal(imported.ScriptId, slot.BoundScriptId);
            Assert.NotNull(slot.BoundScript);
            Assert.True(File.Exists(exportFilePath));
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
    public void RemoveScript_ClearsExistingBindings()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"betterbtd-library-{Guid.NewGuid():N}");
        var sourceFilePath = Path.Combine(rootDirectory, "source", "custom-script.btd");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFilePath)!);
            ScriptDocumentService.Instance.Save(sourceFilePath, CreateDocument(
                GameMapType.DarkCastle,
                StageDifficulty.Hard,
                StageMode.CHIMPS,
                ["black-border"]));

            var service = new ManagedScriptLibraryService(
                Path.Combine(rootDirectory, "managed"),
                ScriptDocumentService.Instance,
                ManagedScriptSlotCatalogService.Instance);

            var imported = service.ImportScript(sourceFilePath);
            var slotId = ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId();
            service.SetBinding(slotId, imported.ScriptId);

            var removed = service.RemoveScript(imported.ScriptId);
            var snapshot = service.GetSnapshot();
            var slot = snapshot.Slots.First(x => x.Definition.SlotId == slotId);

            Assert.True(removed);
            Assert.Empty(snapshot.Scripts);
            Assert.False(slot.HasBinding);
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
    public void SlotCatalog_ContainsExpectedFrameworkSlots()
    {
        var catalog = ManagedScriptSlotCatalogService.Instance;
        var slots = catalog.GetAll();

        var blackBorderCount = GameElementCatalog.Maps.Count * 14;
        var collectionCount = 3 * 13;
        var expectedCount = blackBorderCount + collectionCount + 2;

        Assert.Equal(expectedCount, slots.Count);
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId());
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateRaceCurrentSlotId());
        Assert.Contains(slots, x => x.SlotId == ManagedScriptSlotIdFactory.CreateBlackBorderSlotId(
            GameMapType.MonkeyMeadow,
            StageDifficulty.Easy,
            StageMode.Standard));
    }

    private static ScriptDocument CreateDocument(
        GameMapType map,
        StageDifficulty difficulty,
        StageMode mode,
        IReadOnlyList<string> tags)
    {
        return new ScriptDocument
        {
            Metadata = new ScriptMetadataDocument
            {
                Map = map.ToString(),
                Difficulty = difficulty.ToString(),
                Mode = mode.ToString(),
                Hero = HeroType.Quincy.ToString(),
                Tags = [.. tags]
            }
        };
    }
}
