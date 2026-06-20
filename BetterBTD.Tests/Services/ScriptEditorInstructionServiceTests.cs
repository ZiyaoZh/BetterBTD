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
        Assert.Equal(200, instruction.UpgradeOperationIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstance_NextRound_UsesAdvancedDefaults()
    {
        var service = ScriptEditorInstructionService.Instance;
        var template = service.CreateInstructionLibrary().Single(x => x.Type == ScriptCommandType.NextRound);

        var instruction = service.CreateInstructionInstance(template, string.Empty, string.Empty, string.Empty);

        Assert.Equal(200, instruction.NextRoundOperationIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstanceFromDocument_NextRound_MissingAdvancedFields_UsesDefaults()
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.NextRound.ToString(),
            NextRoundAction = "SendNextRound",
            NextRoundSendCount = 1
        };

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.Equal(200, instruction.NextRoundOperationIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstanceFromDocument_NextRound_LoadsAdvancedInterval()
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.NextRound.ToString(),
            NextRoundAction = "SendNextRound",
            NextRoundSendCount = 2,
            NextRoundOperationIntervalMilliseconds = 340
        };

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.Equal(340, instruction.NextRoundOperationIntervalMilliseconds);
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
        Assert.Equal(200, instruction.UpgradeOperationIntervalMilliseconds);
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

    [Fact]
    public void CreateInstructionInstance_SwitchMonkeyTarget_DefaultsToLeft()
    {
        var service = ScriptEditorInstructionService.Instance;
        var template = service.CreateInstructionLibrary().Single(x => x.Type == ScriptCommandType.SwitchMonkeyTarget);

        var instruction = service.CreateInstructionInstance(template, string.Empty, string.Empty, string.Empty);

        Assert.Equal(SwitchDirectionType.Left, instruction.SwitchDirection);
        Assert.Equal(1, instruction.SwitchCount);
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

    [Fact]
    public void CreateInstructionInstanceFromDocument_SwitchMonkeyTarget_LoadsMonkeyPanelOperationInterval()
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
            SwitchDirection = SwitchDirectionType.Right.ToString(),
            SwitchCount = 1,
            MonkeyPanelOperationIntervalMilliseconds = 340
        };

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.Equal(340, instruction.MonkeyPanelOperationIntervalMilliseconds);
    }

    [Fact]
    public void CreateInstructionInstanceFromDocument_SwitchMonkeyTarget_PreservesStoredRightDirection()
    {
        var service = ScriptEditorInstructionService.Instance;
        var templates = service.CreateInstructionLibrary().ToDictionary(x => x.Type);
        var document = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
            SwitchDirection = SwitchDirectionType.Right.ToString(),
            SwitchCount = 2
        };

        var instruction = service.CreateInstructionInstanceFromDocument(
            document,
            new Dictionary<string, ScriptMonkeyObjectDocument>(),
            templates,
            string.Empty,
            string.Empty,
            string.Empty);

        Assert.Equal(SwitchDirectionType.Right, instruction.SwitchDirection);
        Assert.Equal(2, instruction.SwitchCount);
    }
}
