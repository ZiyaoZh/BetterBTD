using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Tests.TestDoubles;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Tests.ScriptExecution.Handlers;

public sealed class UpgradeMonkeyInstructionHandlerTests
{
    [Fact]
    public async Task HandleAsync_HeroUpgrade_PressesHotkeyWithoutDetectionAndClosesPanel()
    {
        var input = new RecordingScriptInputService();
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = new QueueGameStageStateService([])
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
            TargetMonkeyBindingId = "hero-bind",
            TargetMonkeyObjectId = "Hero:Geraldo",
            UpgradeCount = 3
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
        var handler = new UpgradeMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.U, input.PressedHotkeys[0].Key);
        Assert.Empty(input.Clicks);
        Assert.Equal(
        [
            KeyId.Comma,
            KeyId.Comma,
            KeyId.Comma,
            KeyId.Escape
        ], input.PressedKeys);
    }

    [Fact]
    public async Task HandleAsync_TowerUpgrade_PollsUntilPanelVisibleThenUpgradesToTargetLevelAndClosesPanel()
    {
        var input = new RecordingScriptInputService();
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = new QueueGameStageStateService(
            [
                new GameStageStateSnapshot(),
                new GameStageStateSnapshot
                {
                    RightUpgradePanel = new GameStageUpgradePanelState
                    {
                        IsVisible = true,
                        TopPathLevel = 0,
                        MiddlePathLevel = 0,
                        BottomPathLevel = 0
                    }
                },
                new GameStageStateSnapshot
                {
                    RightUpgradePanel = new GameStageUpgradePanelState
                    {
                        IsVisible = true,
                        TopPathLevel = 1,
                        MiddlePathLevel = 0,
                        BottomPathLevel = 0
                    }
                }
            ])
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            TargetMonkeyObjectId = "Tower:DartMonkey",
            UpgradePath = UpgradePathType.Top.ToString(),
            UpgradeCount = 1
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

        var handler = new UpgradeMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.Comma, input.PressedHotkeys[0].Key);
        Assert.Equal([KeyId.Escape], input.PressedKeys);
        Assert.Equal(3, ((QueueGameStageStateService)runtimeServices.GameStageState).CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task HandleAsync_TowerUpgradeDetectionDisabled_ClicksOnceThenPressesUpgradeHotkeyAndClosesPanel()
    {
        var input = new RecordingScriptInputService();
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = new QueueGameStageStateService([])
        };

        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
            TargetMonkeyBindingId = "dart-bind",
            TargetMonkeyObjectId = "Tower:DartMonkey",
            UpgradePath = UpgradePathType.Top.ToString(),
            UpgradeCount = 2,
            UpgradeDetectionEnabled = false,
            UpgradeOperationIntervalMilliseconds = 200
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

        var handler = new UpgradeMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        var click = Assert.Single(input.Clicks);
        Assert.Equal(new WpfPoint(120, 240), click.Coordinate);
        Assert.Empty(input.PressedHotkeys);
        Assert.Equal(
        [
            KeyId.Comma,
            KeyId.Comma,
            KeyId.Escape
        ], input.PressedKeys);
    }

    [Fact]
    public async Task HandleAsync_TowerUpgrade_SameAdjacentMonkeyInstructions_ReusesOpenPanelWithoutClickingOrClosing()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService(
        [
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true,
                    TopPathLevel = 0,
                    MiddlePathLevel = 0,
                    BottomPathLevel = 0
                }
            },
            new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true,
                    TopPathLevel = 1,
                    MiddlePathLevel = 0,
                    BottomPathLevel = 0
                }
            }
        ]);
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };

        var instructions = new[]
        {
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.SwitchMonkeyTarget.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                SwitchDirection = SwitchDirectionType.Right.ToString(),
                SwitchCount = 1
            },
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.UpgradeMonkey.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                TargetMonkeyObjectId = "Tower:DartMonkey",
                UpgradePath = UpgradePathType.Top.ToString(),
                UpgradeCount = 1
            },
            new ScriptInstructionDocument
            {
                CommandType = ScriptCommandType.SetMonkeyAbility.ToString(),
                TargetMonkeyBindingId = "dart-bind",
                SelectedAbility = MonkeyAbilityType.Ability1.ToString()
            }
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

        var context = TestScriptExecutionContextFactory.Create(
            instructions,
            currentStepPosition: 1,
            runtimeServices,
            monkeyObjects);
        context.State.UpsertMonkeyState("dart-bind", "Tower:DartMonkey", "DartMonkey", 1).LastKnownCoordinate =
            new WpfPoint(120, 240);

        var handler = new UpgradeMonkeyInstructionHandler();

        await handler.HandleAsync(context, CancellationToken.None);

        Assert.Empty(input.Clicks);
        var upgradeHotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.Comma, upgradeHotkey.Key);
        Assert.Empty(input.PressedKeys);
        Assert.Equal(2, gameStageState.CaptureSnapshotCallCount);
    }
}
