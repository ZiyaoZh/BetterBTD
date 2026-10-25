using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class ScriptEditorSequenceServiceTests
{
    [Fact]
    public void RebuildMonkeyObjectOptions_HeroSoldAndPlacedAgain_PreservesIndependentIdentitiesAndTargets()
    {
        var firstPlace = CreateHeroPlacement("hero-a");
        var sell = new ScriptInstructionInstance(ScriptCommandType.SellMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "hero-a"
        };
        var secondPlace = CreateHeroPlacement("hero-b");
        var upgrade = new ScriptInstructionInstance(ScriptCommandType.UpgradeMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "hero-b"
        };
        var instructions = new List<ScriptInstructionInstance> { firstPlace, sell, secondPlace, upgrade };

        var options = ScriptEditorSequenceService.Instance.RebuildMonkeyObjectOptions(instructions, LocalizationService.Instance);

        Assert.Equal("hero-a", firstPlace.MonkeyBindingId);
        Assert.Equal("hero-b", secondPlace.MonkeyBindingId);
        Assert.Equal("Hero:Quincy:1", firstPlace.MonkeyObjectId);
        Assert.Equal("Hero:Quincy:2", secondPlace.MonkeyObjectId);
        Assert.Equal("hero-a", sell.TargetMonkeyBindingId);
        Assert.Equal("Hero:Quincy:1", sell.TargetMonkeyObjectId);
        Assert.Equal("hero-b", upgrade.TargetMonkeyBindingId);
        Assert.Equal("Hero:Quincy:2", upgrade.TargetMonkeyObjectId);
        Assert.Equal(["hero-a", "hero-b"], options.Select(x => x.Code).ToArray());
        Assert.EndsWith("1", options[0].DisplayName);
        Assert.EndsWith("2", options[1].DisplayName);
        Assert.NotEqual(options[0].DisplayName, options[1].DisplayName);
        Assert.False(upgrade.ShowUpgradePathSelector);
    }

    [Fact]
    public void RebuildMonkeyObjectOptions_ReorderedHeroPlacements_PreservesNumbersAndAllocatesNextNumber()
    {
        var firstPlace = CreateHeroPlacement("hero-a", "Hero:Quincy:1");
        var secondPlace = CreateHeroPlacement("hero-b", "Hero:Quincy:2");
        var newPlace = CreateHeroPlacement(string.Empty);
        var upgrade = new ScriptInstructionInstance(ScriptCommandType.UpgradeMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "hero-b",
            TargetMonkeyObjectId = "Hero:Quincy:2"
        };
        var instructions = new List<ScriptInstructionInstance> { newPlace, secondPlace, firstPlace, upgrade };

        ScriptEditorSequenceService.Instance.RebuildMonkeyObjectOptions(instructions, LocalizationService.Instance);
        ScriptEditorSequenceService.Instance.RebuildMonkeyObjectOptions(instructions, LocalizationService.Instance);

        Assert.Equal("hero-a", firstPlace.MonkeyBindingId);
        Assert.Equal("Hero:Quincy:1", firstPlace.MonkeyObjectId);
        Assert.Equal("hero-b", secondPlace.MonkeyBindingId);
        Assert.Equal("Hero:Quincy:2", secondPlace.MonkeyObjectId);
        Assert.False(string.IsNullOrWhiteSpace(newPlace.MonkeyBindingId));
        Assert.DoesNotContain(newPlace.MonkeyBindingId, new[] { "hero-a", "hero-b" });
        Assert.Equal("Hero:Quincy:3", newPlace.MonkeyObjectId);
        Assert.Equal("hero-b", upgrade.TargetMonkeyBindingId);
        Assert.Equal("Hero:Quincy:2", upgrade.TargetMonkeyObjectId);
    }

    [Theory]
    [InlineData("hero-a")]
    [InlineData("")]
    public void RebuildMonkeyObjectOptions_LegacyHeroObjectKey_UpdatesNumberAndPreservesTarget(string targetBindingId)
    {
        var place = CreateHeroPlacement("hero-a", "Hero:Quincy");
        var upgrade = new ScriptInstructionInstance(ScriptCommandType.UpgradeMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = targetBindingId,
            TargetMonkeyObjectId = "Hero:Quincy"
        };
        var instructions = new List<ScriptInstructionInstance> { place, upgrade };

        ScriptEditorSequenceService.Instance.RebuildMonkeyObjectOptions(instructions, LocalizationService.Instance);

        Assert.Equal("hero-a", place.MonkeyBindingId);
        Assert.Equal("Hero:Quincy:1", place.MonkeyObjectId);
        Assert.Equal("hero-a", upgrade.TargetMonkeyBindingId);
        Assert.Equal("Hero:Quincy:1", upgrade.TargetMonkeyObjectId);
    }

    [Fact]
    public void RebuildMonkeyObjectOptions_RepeatedHeroPlacements_SaveAndReloadDistinctObjectsAndReferences()
    {
        var firstPlace = CreateHeroPlacement("hero-a");
        var secondPlace = CreateHeroPlacement("hero-b");
        var sell = new ScriptInstructionInstance(ScriptCommandType.SellMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "hero-a"
        };
        var upgrade = new ScriptInstructionInstance(ScriptCommandType.UpgradeMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "hero-b"
        };
        var instructions = new List<ScriptInstructionInstance> { firstPlace, sell, secondPlace, upgrade };
        var instructionService = ScriptEditorInstructionService.Instance;
        ScriptEditorSequenceService.Instance.RebuildMonkeyObjectOptions(instructions, LocalizationService.Instance);
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.btd");

        try
        {
            ScriptDocumentService.Instance.Save(filePath, new ScriptDocument
            {
                MonkeyObjects = instructionService.BuildMonkeyObjectDocuments(instructions),
                Instructions = instructionService.BuildInstructionDocuments(instructions)
            });
            var loaded = ScriptDocumentService.Instance.Load(filePath);
            var monkeyObjects = loaded.MonkeyObjects.ToDictionary(x => x.BindingId);
            var templates = instructionService.CreateInstructionLibrary().ToDictionary(x => x.Type);
            var restored = loaded.Instructions.Select(x => instructionService.CreateInstructionInstanceFromDocument(
                x, monkeyObjects, templates, string.Empty, string.Empty, string.Empty)).ToList();
            ScriptEditorSequenceService.Instance.RebuildMonkeyObjectOptions(restored, LocalizationService.Instance);

            Assert.Equal(2, monkeyObjects.Count);
            Assert.Equal("Hero:Quincy:1", monkeyObjects["hero-a"].ObjectId);
            Assert.Equal("Hero:Quincy:2", monkeyObjects["hero-b"].ObjectId);
            Assert.All(monkeyObjects.Values, x => Assert.Equal("Hero:Quincy", x.SelectionCode));
            Assert.Equal("hero-a", restored[0].MonkeyBindingId);
            Assert.Equal("hero-a", restored[1].TargetMonkeyBindingId);
            Assert.Equal("Hero:Quincy:1", restored[1].TargetMonkeyObjectId);
            Assert.Equal("hero-b", restored[2].MonkeyBindingId);
            Assert.Equal("hero-b", restored[3].TargetMonkeyBindingId);
            Assert.Equal("Hero:Quincy:2", restored[3].TargetMonkeyObjectId);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static ScriptInstructionInstance CreateHeroPlacement(string bindingId, string objectId = "")
    {
        return new ScriptInstructionInstance(ScriptCommandType.PlaceMonkey, "name", "desc")
        {
            SelectedMonkeyTower = "Hero:Quincy",
            MonkeyBindingId = bindingId,
            MonkeyObjectId = objectId
        };
    }

    [Fact]
    public void RebuildMonkeyObjectOptions_NewPlacementAdded_PreservesExistingTargetBinding()
    {
        var service = ScriptEditorSequenceService.Instance;
        var localization = LocalizationService.Instance;

        var firstPlace = new ScriptInstructionInstance(ScriptCommandType.PlaceMonkey, "name", "desc")
        {
            SelectedMonkeyTower = "Tower:DartMonkey",
            MonkeyBindingId = "binding-a",
            MonkeyObjectId = "DartMonkey:1"
        };
        var upgrade = new ScriptInstructionInstance(ScriptCommandType.UpgradeMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "binding-a",
            TargetMonkeyObjectId = "DartMonkey:1"
        };
        var secondPlace = new ScriptInstructionInstance(ScriptCommandType.PlaceMonkey, "name", "desc")
        {
            SelectedMonkeyTower = "Tower:SniperMonkey"
        };

        var instructions = new List<ScriptInstructionInstance>
        {
            firstPlace,
            upgrade,
            secondPlace
        };

        var options = service.RebuildMonkeyObjectOptions(instructions, localization);

        Assert.Equal("binding-a", upgrade.TargetMonkeyBindingId);
        Assert.Equal("DartMonkey:1", upgrade.TargetMonkeyObjectId);
        Assert.Equal("binding-a", firstPlace.MonkeyBindingId);
        Assert.Equal("DartMonkey:1", firstPlace.MonkeyObjectId);
        Assert.False(string.IsNullOrWhiteSpace(secondPlace.MonkeyBindingId));
        Assert.Equal("SniperMonkey:1", secondPlace.MonkeyObjectId);
        Assert.Equal(2, options.Count);
    }

    [Fact]
    public void RebuildMonkeyObjectOptions_ReorderedPlacements_PreservesObjectIdentityAndTargets()
    {
        var service = ScriptEditorSequenceService.Instance;
        var localization = LocalizationService.Instance;

        var firstPlace = new ScriptInstructionInstance(ScriptCommandType.PlaceMonkey, "name", "desc")
        {
            SelectedMonkeyTower = "Tower:DartMonkey",
            MonkeyBindingId = "binding-a",
            MonkeyObjectId = "DartMonkey:1"
        };
        var secondPlace = new ScriptInstructionInstance(ScriptCommandType.PlaceMonkey, "name", "desc")
        {
            SelectedMonkeyTower = "Tower:DartMonkey",
            MonkeyBindingId = "binding-b",
            MonkeyObjectId = "DartMonkey:2"
        };
        var upgrade = new ScriptInstructionInstance(ScriptCommandType.UpgradeMonkey, "name", "desc")
        {
            TargetMonkeyBindingId = "binding-b",
            TargetMonkeyObjectId = "DartMonkey:2"
        };

        var instructions = new List<ScriptInstructionInstance>
        {
            secondPlace,
            firstPlace,
            upgrade
        };

        var options = service.RebuildMonkeyObjectOptions(instructions, localization);

        Assert.Equal("binding-b", secondPlace.MonkeyBindingId);
        Assert.Equal("DartMonkey:2", secondPlace.MonkeyObjectId);
        Assert.Equal("binding-a", firstPlace.MonkeyBindingId);
        Assert.Equal("DartMonkey:1", firstPlace.MonkeyObjectId);
        Assert.Equal("binding-b", upgrade.TargetMonkeyBindingId);
        Assert.Equal("DartMonkey:2", upgrade.TargetMonkeyObjectId);
        Assert.Equal(["binding-b", "binding-a"], options.Select(x => x.Code).ToArray());
    }
}
