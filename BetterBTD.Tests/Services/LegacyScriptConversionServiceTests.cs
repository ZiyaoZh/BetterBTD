using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class LegacyScriptConversionServiceTests
{
    [Fact]
    public void Convert_HeroSoldAndPlacedAgain_PreservesDistinctInstancesAndTargets()
    {
        var legacyDocument = new LegacyScriptModel
        {
            Metadata = new LegacyScriptMetadata { SelectedHero = (int)LegacyHeroType.Quincy },
            InstructionsList =
            [
                [(int)LegacyActionType.PlaceHero, 0, 0, -1, -1, -1, -1, -1, 1000000, 2000000, 0, 0],
                [(int)LegacyActionType.UpgradeHero, 0, 0, -1, -1, -1, -1, -1, -1, -1, 0, 0],
                [(int)LegacyActionType.SellHero, 0, 0, -1, -1, -1, -1, -1, -1, -1, 0, 0],
                [(int)LegacyActionType.PlaceHero, 0, 0, -1, -1, -1, -1, -1, 3000000, 4000000, 0, 0],
                [(int)LegacyActionType.UpgradeHero, 0, 0, -1, -1, -1, -1, -1, -1, -1, 0, 0],
                [(int)LegacyActionType.SwitchHeroTarget, 0, (int)LegacyTargetType.Right, -1, -1, -1, -1, -1, -1, -1, 0, 0],
                [(int)LegacyActionType.SetHeroAbility, 0, (int)LegacyMonkeyFunctionType.Function1, -1, -1, -1, -1, -1, -1, -1, 0, 0],
                [(int)LegacyActionType.SellHero, 0, 0, -1, -1, -1, -1, -1, -1, -1, 0, 0]
            ]
        };

        var result = LegacyScriptConversionService.Instance.Convert(legacyDocument);
        var objects = result.Document.MonkeyObjects;
        var instructions = result.Document.Instructions;

        Assert.Empty(result.Warnings);
        Assert.Equal(2, objects.Count);
        Assert.NotEqual(objects[0].BindingId, objects[1].BindingId);
        Assert.Equal(["Hero:Quincy:1", "Hero:Quincy:2"], objects.Select(x => x.ObjectId).ToArray());
        Assert.All(objects, x => Assert.Equal("Hero:Quincy", x.SelectionCode));
        Assert.Equal(8, instructions.Count);
        Assert.Equal(objects[0].BindingId, instructions[0].MonkeyBindingId);
        Assert.Equal(objects[0].ObjectId, instructions[0].MonkeyObjectId);
        Assert.Equal(objects[1].BindingId, instructions[3].MonkeyBindingId);
        Assert.Equal(objects[1].ObjectId, instructions[3].MonkeyObjectId);
        Assert.Equal("Hero:Quincy", instructions[0].SelectedMonkeyTower);
        Assert.Equal("Hero:Quincy", instructions[3].SelectedMonkeyTower);
        Assert.All(instructions.Skip(1).Take(2), instruction =>
        {
            Assert.Equal(objects[0].BindingId, instruction.TargetMonkeyBindingId);
            Assert.Equal(objects[0].ObjectId, instruction.TargetMonkeyObjectId);
        });
        Assert.All(instructions.Skip(4), instruction =>
        {
            Assert.Equal(objects[1].BindingId, instruction.TargetMonkeyBindingId);
            Assert.Equal(objects[1].ObjectId, instruction.TargetMonkeyObjectId);
        });
    }

    [Fact]
    public void Convert_AbsoluteUpgradeLevel_SplitsIntoCurrentUpgradeInstructions()
    {
        var legacyDocument = new LegacyScriptModel
        {
            Metadata = new LegacyScriptMetadata
            {
                Version = "1.1",
                ScriptName = "split-upgrade",
                SelectedHero = 0
            },
            InstructionsList =
            [
                [0, 23, 0, -1, -1, -1, -1, 123, 1000000, 2000000, 0, 0],
                [1, 123, 0, -1, -1, 502, 0, 0, -1, -1, 0, 0]
            ]
        };

        var result = LegacyScriptConversionService.Instance.Convert(legacyDocument);
        var upgrades = result.Document.Instructions
            .Where(x => x.CommandType == ScriptCommandType.UpgradeMonkey.ToString())
            .ToList();

        Assert.Equal(2, upgrades.Count);
        Assert.Equal(UpgradePathType.Top.ToString(), upgrades[0].UpgradePath);
        Assert.Equal(5, upgrades[0].UpgradeCount);
        Assert.Equal(UpgradePathType.Bottom.ToString(), upgrades[1].UpgradePath);
        Assert.Equal(2, upgrades[1].UpgradeCount);
    }

    [Fact]
    public void Convert_InstructionTriggers_InsertsWaitInstructionsBeforeAction()
    {
        var legacyDocument = new LegacyScriptModel
        {
            Metadata = new LegacyScriptMetadata
            {
                Version = "1.1",
                ScriptName = "triggered-action",
                SelectedHero = 0
            },
            InstructionsList =
            [
                [4, 1, 0, -1, -1, -1, -1, -1, -1, -1, 12, 500]
            ]
        };

        var result = LegacyScriptConversionService.Instance.Convert(legacyDocument);

        Assert.Collection(
            result.Document.Instructions,
            instruction =>
            {
                Assert.Equal(ScriptCommandType.Wait.ToString(), instruction.CommandType);
                Assert.Equal(WaitModeType.Round.ToString(), instruction.WaitMode);
                Assert.Equal(12, instruction.WaitRoundCount);
            },
            instruction =>
            {
                Assert.Equal(ScriptCommandType.Wait.ToString(), instruction.CommandType);
                Assert.Equal(WaitModeType.Gold.ToString(), instruction.WaitMode);
                Assert.Equal(500, instruction.WaitGoldAmount);
            },
            instruction =>
            {
                Assert.Equal(ScriptCommandType.NextRound.ToString(), instruction.CommandType);
                Assert.Equal("SendNextRound", instruction.NextRoundAction);
            });
    }
}
