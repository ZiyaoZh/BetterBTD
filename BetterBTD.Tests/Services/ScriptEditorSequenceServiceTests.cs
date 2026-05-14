using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class ScriptEditorSequenceServiceTests
{
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
