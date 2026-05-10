using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Tests.TestDoubles;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Tests.ScriptExecution.Handlers;

public sealed class MonkeyPanelInteractionHandlerTests
{
    [Fact]
    public async Task SwitchMonkeyTarget_OpensPanelThenSwitchesAndClosesWithEscape()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService(
        [
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true
                }
            }
        ]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            SwitchDirection = SwitchDirectionType.Right.ToString(),
            SwitchCount = 1
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SwitchMonkeyTargetInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        Assert.Equal([KeyId.Tab, KeyId.Escape], input.PressedKeys);
        Assert.Equal(1, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SetMonkeyAbility_OpensPanelThenUsesAbilityAndClosesWithEscape()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService(
        [
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true
                }
            }
        ]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SetMonkeyAbility.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            SelectedAbility = MonkeyAbilityType.Ability1.ToString(),
            RequiresAbilityCoordinate = false
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SetMonkeyAbilityInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        var abilityHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.PageDown, abilityHotkey.Key);
        Assert.Equal([KeyId.Escape], input.PressedKeys);
        Assert.Equal(1, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SwitchMonkeyTarget_PanelDetectionDisabled_ClicksOnceThenSwitchesAndClosesWithEscape()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService([]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            SwitchDirection = SwitchDirectionType.Right.ToString(),
            SwitchCount = 2,
            MonkeyPanelDetectionEnabled = false,
            MonkeyPanelOperationIntervalMilliseconds = 100
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SwitchMonkeyTargetInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        Assert.Equal(
        [
            KeyId.Tab,
            KeyId.Tab,
            KeyId.Escape
        ], input.PressedKeys);
        Assert.Equal(0, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SetMonkeyAbility_PanelDetectionDisabled_ClicksOnceThenUsesAbilityAndClosesWithEscape()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService([]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SetMonkeyAbility.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            SelectedAbility = MonkeyAbilityType.Ability1.ToString(),
            RequiresAbilityCoordinate = false,
            MonkeyPanelDetectionEnabled = false,
            MonkeyPanelOperationIntervalMilliseconds = 100
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SetMonkeyAbilityInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        var abilityHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.PageDown, abilityHotkey.Key);
        Assert.Equal([KeyId.Escape], input.PressedKeys);
        Assert.Equal(0, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SwitchMonkeyTarget_Hero_SkipsPanelDetectionEvenWhenEnabled()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService([]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
            TargetMonkeyBindingId = "hero-bind",
            SwitchDirection = SwitchDirectionType.Right.ToString(),
            SwitchCount = 1
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "hero-bind",
                ObjectId = "Hero:Geraldo",
                SelectionCode = "Hero:Geraldo",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("hero-bind", "Hero:Geraldo", "Hero:Geraldo", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SwitchMonkeyTargetInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        Assert.Equal([KeyId.Tab, KeyId.Escape], input.PressedKeys);
        Assert.Equal(0, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SetMonkeyAbility_Hero_SkipsPanelDetectionEvenWhenEnabled()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService([]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SetMonkeyAbility.ToString(),
            TargetMonkeyBindingId = "hero-bind",
            SelectedAbility = MonkeyAbilityType.Ability1.ToString(),
            RequiresAbilityCoordinate = false
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "hero-bind",
                ObjectId = "Hero:Geraldo",
                SelectionCode = "Hero:Geraldo",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("hero-bind", "Hero:Geraldo", "Hero:Geraldo", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SetMonkeyAbilityInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        var abilityHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.PageDown, abilityHotkey.Key);
        Assert.Equal([KeyId.Escape], input.PressedKeys);
        Assert.Equal(0, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SellMonkey_SellDetectionEnabled_RetriesUntilPanelCloses()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService(
        [
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true
                }
            },
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true
                }
            },
            new GameStageStateSnapshot()
        ]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SellMonkey.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            SellDetectionEnabled = true
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SellMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        Assert.Equal(2, input.PressedHotkeys.Count);
        Assert.All(input.PressedHotkeys, hotkey => Assert.Equal(KeyId.Backspace, hotkey.Key));
        Assert.Empty(input.PressedKeys);
        Assert.Equal(3, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SellMonkey_Hero_SkipsAllDetectionEvenWhenEnabled()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService([]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SellMonkey.ToString(),
            TargetMonkeyBindingId = "hero-bind",
            SellDetectionEnabled = true
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "hero-bind",
                ObjectId = "Hero:Geraldo",
                SelectionCode = "Hero:Geraldo",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("hero-bind", "Hero:Geraldo", "Hero:Geraldo", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SellMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        var sellHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.Backspace, sellHotkey.Key);
        Assert.Empty(input.PressedKeys);
        Assert.Equal(0, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SellMonkey_PanelDetectionDisabled_ClicksOnceThenSells()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService([]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SellMonkey.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            MonkeyPanelDetectionEnabled = false,
            MonkeyPanelOperationIntervalMilliseconds = 200
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SellMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        var sellHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.Backspace, sellHotkey.Key);
        Assert.Empty(input.PressedKeys);
        Assert.Equal(1, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task SellMonkey_SellDetectionDisabled_PressesOnceWithoutPanelCloseVerification()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService(
        [
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true
                }
            }
        ]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.SellMonkey.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            SellDetectionEnabled = false
        };

        var monkeyObjects = new[]
        {
            new ScriptMonkeyObjectDocument
            {
                BindingId = "dart-bind",
                ObjectId = "Tower:DartMonkey",
                SelectionCode = "DartMonkey",
                PlacementOrder = 1
            }
        };

        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices, monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new SellMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        var sellHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.Backspace, sellHotkey.Key);
        Assert.Empty(input.PressedKeys);
        Assert.Equal(1, gameStageState.CaptureSnapshotCallCount);
    }
}
