using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Tests.TestDoubles;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Tests.ScriptExecution.Handlers;

public sealed class ScriptInstructionHandlerSupportTests
{
    [Fact]
    public void BuildPlacementSearchCoordinates_ExpandsAroundCenterInEightDirections()
    {
        var coordinates = ScriptInstructionHandlerSupport
            .BuildPlacementSearchCoordinates(new WpfPoint(100, 200))
            .Take(17)
            .ToArray();

        Assert.Equal(
        [
            new WpfPoint(100, 200),
            new WpfPoint(101, 200),
            new WpfPoint(101, 201),
            new WpfPoint(100, 201),
            new WpfPoint(99, 201),
            new WpfPoint(99, 200),
            new WpfPoint(99, 199),
            new WpfPoint(100, 199),
            new WpfPoint(101, 199),
            new WpfPoint(102, 200),
            new WpfPoint(102, 202),
            new WpfPoint(100, 202),
            new WpfPoint(98, 202),
            new WpfPoint(98, 200),
            new WpfPoint(98, 198),
            new WpfPoint(100, 198),
            new WpfPoint(102, 198)
        ], coordinates);
    }

    [Fact]
    public void BuildPlacementSearchCoordinates_StopsAfterTwentyRings()
    {
        var coordinates = ScriptInstructionHandlerSupport
            .BuildPlacementSearchCoordinates(new WpfPoint(0, 0))
            .ToArray();

        Assert.Equal(161, coordinates.Length);
        Assert.Contains(new WpfPoint(20, 20), coordinates);
        Assert.Contains(new WpfPoint(-20, -20), coordinates);
        Assert.DoesNotContain(new WpfPoint(21, 0), coordinates);
        Assert.DoesNotContain(new WpfPoint(0, -21), coordinates);
    }

    [Fact]
    public async Task ExecuteSellMonkeyAsync_SellDetectionEnabled_RetriesUntilPanelCloses()
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
            CommandType = ScriptCommandType.SellMonkey.ToString()
        };
        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices);

        await ScriptInstructionHandlerSupport.ExecuteSellMonkeyAsync(
            context,
            "Tower:DartMonkey",
            new HotkeyBinding
            {
                Key = KeyId.Backspace
            },
            sellDetectionEnabled: true,
            timeoutMilliseconds: 1000,
            detectionIntervalMilliseconds: 0,
            CancellationToken.None);

        Assert.Equal(2, input.PressedHotkeys.Count);
        Assert.All(input.PressedHotkeys, hotkey => Assert.Equal(KeyId.Backspace, hotkey.Key));
        Assert.Equal(2, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task ExecuteSellMonkeyAsync_SellDetectionDisabled_PressesOnceWithoutSnapshotPolling()
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
            CommandType = ScriptCommandType.SellMonkey.ToString()
        };
        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices);

        await ScriptInstructionHandlerSupport.ExecuteSellMonkeyAsync(
            context,
            "Tower:DartMonkey",
            new HotkeyBinding
            {
                Key = KeyId.Backspace
            },
            sellDetectionEnabled: false,
            timeoutMilliseconds: 1000,
            detectionIntervalMilliseconds: 0,
            CancellationToken.None);

        var hotkey = Assert.Single(input.PressedHotkeys);
        Assert.Equal(KeyId.Backspace, hotkey.Key);
        Assert.Equal(0, gameStageState.CaptureSnapshotCallCount);
    }
}
