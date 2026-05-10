using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services;

namespace BetterBTD.Tests.Services;

public sealed class ScriptEditorInstructionServiceTests
{
    [Fact]
    public void CreateInstructionInstance_PlaceMonkey_UsesPlacementAdvancedDefaults()
    {
        var service = ScriptEditorInstructionService.Instance;
        var template = service.CreateInstructionLibrary().Single(x => x.Type == ScriptCommandType.PlaceMonkey);

        var instruction = service.CreateInstructionInstance(template, string.Empty, string.Empty, string.Empty);

        Assert.True(instruction.PlacementDetectionEnabled);
        Assert.True(instruction.PlacementFailureAdjustmentEnabled);
        Assert.Equal(200, instruction.PlacementAttemptIntervalMilliseconds);
        Assert.Equal(200, instruction.PlacementAdjustmentAttemptIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstanceFromDocument_PlaceMonkey_MissingAdvancedFields_UsesDefaults()
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.PlaceMonkey.ToString(),
            SelectedMonkeyTower = "DartMonkey"
        };

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.True(instruction.PlacementDetectionEnabled);
        Assert.True(instruction.PlacementFailureAdjustmentEnabled);
        Assert.Equal(200, instruction.PlacementAttemptIntervalMilliseconds);
        Assert.Equal(200, instruction.PlacementAdjustmentAttemptIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstance_UpgradeMonkey_UsesUpgradeAdvancedDefaults()
    {
        var service = ScriptEditorInstructionService.Instance;
        var template = service.CreateInstructionLibrary().Single(x => x.Type == ScriptCommandType.UpgradeMonkey);

        var instruction = service.CreateInstructionInstance(template, string.Empty, string.Empty, string.Empty);

        Assert.True(instruction.UpgradeDetectionEnabled);
        Assert.Equal(200, instruction.UpgradeAttemptIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstanceFromDocument_UpgradeMonkey_MissingAdvancedFields_UsesDefaults()
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
            UpgradePath = UpgradePathType.Top.ToString(),
            UpgradeCount = 1
        };

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.True(instruction.UpgradeDetectionEnabled);
        Assert.Equal(200, instruction.UpgradeAttemptIntervalMilliseconds);
    }

    [Theory]
    [InlineData(ScriptCommandType.SwitchMonkeyTarget)]
    [InlineData(ScriptCommandType.SetMonkeyAbility)]
    [InlineData(ScriptCommandType.SellMonkey)]
    public void CreateInstructionInstance_MonkeyPanelInteraction_UsesAdvancedDefaults(ScriptCommandType commandType)
    {
        var service = ScriptEditorInstructionService.Instance;
        var template = service.CreateInstructionLibrary().Single(x => x.Type == commandType);

        var instruction = service.CreateInstructionInstance(template, string.Empty, string.Empty, string.Empty);

        Assert.True(instruction.MonkeyPanelDetectionEnabled);
        Assert.Equal(200, instruction.MonkeyPanelOperationIntervalMilliseconds);

        if (commandType == ScriptCommandType.SellMonkey)
        {
            Assert.True(instruction.SellDetectionEnabled);
        }
    }

    [Theory]
    [InlineData(ScriptCommandType.SwitchMonkeyTarget)]
    [InlineData(ScriptCommandType.SetMonkeyAbility)]
    [InlineData(ScriptCommandType.SellMonkey)]
    public void CreateInstructionInstanceFromDocument_MonkeyPanelInteraction_MissingAdvancedFields_UsesDefaults(ScriptCommandType commandType)
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = commandType.ToString()
        };

        if (commandType == ScriptCommandType.SwitchMonkeyTarget)
        {
            document.SwitchDirection = SwitchDirectionType.Right.ToString();
            document.SwitchCount = 1;
        }
        else if (commandType == ScriptCommandType.SetMonkeyAbility)
        {
            document.SelectedAbility = MonkeyAbilityType.Ability1.ToString();
        }

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.True(instruction.MonkeyPanelDetectionEnabled);
        Assert.Equal(200, instruction.MonkeyPanelOperationIntervalMilliseconds);

        if (commandType == ScriptCommandType.SellMonkey)
        {
            Assert.True(instruction.SellDetectionEnabled);
        }
    }
}
