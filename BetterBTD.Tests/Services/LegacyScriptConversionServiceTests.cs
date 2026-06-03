using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class LegacyScriptConversionServiceTests
{
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
