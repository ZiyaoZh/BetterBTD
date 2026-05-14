using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class LegacyScriptConversionServiceTests
{
    [Fact]
    public void Load_LegacySampleFile_ParsesLegacyDocument()
    {
        var service = LegacyScriptDocumentService.Instance;

        var document = service.Load(GetLegacySampleFilePath());

        Assert.Equal("1.1", document.Metadata.Version);
        Assert.Equal(4, document.Metadata.SelectedMap);
        Assert.Equal(1, document.Metadata.SelectedDifficulty);
        Assert.Equal(0, document.Metadata.SelectedMode);
        Assert.Equal(0, document.Metadata.SelectedHero);
        Assert.NotEmpty(document.InstructionsList);
        Assert.Equal(15, document.MonkeyIds.Count);
    }

    [Fact]
    public void Convert_LegacySampleFile_BuildsCurrentScriptDocument()
    {
        var legacyDocument = LegacyScriptDocumentService.Instance.Load(GetLegacySampleFilePath());

        var result = LegacyScriptConversionService.Instance.Convert(legacyDocument);

        Assert.Equal(ScriptDocumentFormat.Schema, result.Document.Schema);
        Assert.Equal("1.1", result.Document.Metadata.ScriptVersion);
        Assert.Equal(GameMapType.TreeStump.ToString(), result.Document.Metadata.Map);
        Assert.Equal(StageDifficulty.Medium.ToString(), result.Document.Metadata.Difficulty);
        Assert.Equal(StageMode.Standard.ToString(), result.Document.Metadata.Mode);
        Assert.Equal(HeroType.Quincy.ToString(), result.Document.Metadata.Hero);
        Assert.Contains(result.Warnings, warning => warning.Contains("Legacy anchor coordinate"));
        Assert.Contains(result.Document.MonkeyObjects, x => x.ObjectId == "Hero:Quincy");
        Assert.Contains(result.Document.MonkeyObjects, x => x.ObjectId == "Alchemist:1");
        Assert.Contains(result.Document.Instructions, x => x.CommandType == ScriptCommandType.NextRound.ToString() &&
                                                           x.NextRoundAction == "SendNextRound");
        Assert.Contains(result.Document.Instructions, x => x.CommandType == ScriptCommandType.Wait.ToString() &&
                                                           x.WaitMode == WaitModeType.Gold.ToString() &&
                                                           x.WaitGoldAmount == 800);
        Assert.Contains(result.Document.Instructions, x => x.CommandType == ScriptCommandType.Wait.ToString() &&
                                                           x.WaitMode == WaitModeType.Gold.ToString() &&
                                                           x.WaitGoldAmount == 2000);
        Assert.Contains(result.Document.Instructions, x => x.CommandType == ScriptCommandType.SetMonkeyAbility.ToString() &&
                                                           x.SelectedAbility == MonkeyAbilityType.Ability1.ToString() &&
                                                           x.RequiresAbilityCoordinate &&
                                                           x.AbilityCoordinateX == 605.625 &&
                                                           x.AbilityCoordinateY == 399.375);
        Assert.Contains(result.Document.Instructions, x => x.CommandType == ScriptCommandType.SetMonkeyAbility.ToString() &&
                                                           x.SelectedAbility == MonkeyAbilityType.Ability2.ToString() &&
                                                           x.RequiresAbilityCoordinate &&
                                                           x.AbilityCoordinateX == 609.375 &&
                                                           x.AbilityCoordinateY == 536.25);
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

    private static string GetLegacySampleFilePath()
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                ".."));

        return Directory.GetFiles(repoRoot, "*.btd6", SearchOption.TopDirectoryOnly).Single();
    }
}
