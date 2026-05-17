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

    [Fact]
    public async Task WaitForUpgradePanelVisibleAsync_PollsMultipleTimesBeforeRetryingSelection()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new QueueGameStageStateService(
        [
            new GameStageStateSnapshot(),
            new GameStageStateSnapshot(),
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
            CommandType = ScriptCommandType.UpgradeMonkey.ToString()
        };
        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices);

        var snapshot = await ScriptInstructionHandlerSupport.WaitForUpgradePanelVisibleAsync(
            context,
            new WpfPoint(120, 240),
            timeoutMilliseconds: 1000,
            panelPollIntervalMilliseconds: 10,
            CancellationToken.None);

        Assert.NotNull(ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(snapshot));
        Assert.Single(input.Clicks);
        Assert.Equal(3, gameStageState.CaptureSnapshotCallCount);
    }

    [Fact]
    public async Task WaitForUpgradePanelVisibleAsync_RetriesSelectionAfterAttemptTimeout()
    {
        var input = new RecordingScriptInputService();
        var gameStageState = new ClickAwareGameStageStateService(
            input,
            () => new GameStageStateSnapshot(),
            () => new GameStageStateSnapshot
            {
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true
                }
            });
        var runtimeServices = new ScriptExecutionRuntimeServices
        {
            Capture = new NullScriptCaptureService(),
            Input = input,
            GameStageState = gameStageState
        };
        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.UpgradeMonkey.ToString()
        };
        var context = TestScriptExecutionContextFactory.Create(instruction, runtimeServices);

        var snapshot = await ScriptInstructionHandlerSupport.WaitForUpgradePanelVisibleAsync(
            context,
            new WpfPoint(120, 240),
            timeoutMilliseconds: 5500,
            panelPollIntervalMilliseconds: 50,
            CancellationToken.None);

        Assert.NotNull(ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(snapshot));
        Assert.Equal(2, input.Clicks.Count);
        Assert.True(gameStageState.CaptureSnapshotCallCount >= 2);
    }
}

internal sealed class ClickAwareGameStageStateService : IGameStageStateService
{
    private readonly RecordingScriptInputService _input;
    private readonly Func<GameStageStateSnapshot> _beforeSecondClickSnapshotFactory;
    private readonly Func<GameStageStateSnapshot> _afterSecondClickSnapshotFactory;
    private GameStageStateSnapshot? _lastSnapshot;

    public ClickAwareGameStageStateService(
        RecordingScriptInputService input,
        Func<GameStageStateSnapshot> beforeSecondClickSnapshotFactory,
        Func<GameStageStateSnapshot> afterSecondClickSnapshotFactory)
    {
        _input = input;
        _beforeSecondClickSnapshotFactory = beforeSecondClickSnapshotFactory;
        _afterSecondClickSnapshotFactory = afterSecondClickSnapshotFactory;
    }

    public bool IsAvailable => true;

    public int CaptureSnapshotCallCount { get; private set; }

    public Task<bool?> GetIsInLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.IsInLevel);
    }

    public Task<int?> GetGoldAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.Gold);
    }

    public Task<int?> GetRoundAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.Round);
    }

    public Task<bool?> GetRightUpgradeVisibleAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.RightUpgradePanel.IsVisible);
    }

    public Task<int?> GetRightTopUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.RightUpgradePanel.TopPathLevel);
    }

    public Task<int?> GetRightMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.RightUpgradePanel.MiddlePathLevel);
    }

    public Task<int?> GetRightBottomUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.RightUpgradePanel.BottomPathLevel);
    }

    public Task<bool?> GetLeftUpgradeVisibleAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.LeftUpgradePanel.IsVisible);
    }

    public Task<int?> GetLeftTopUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.LeftUpgradePanel.TopPathLevel);
    }

    public Task<int?> GetLeftMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.LeftUpgradePanel.MiddlePathLevel);
    }

    public Task<int?> GetLeftBottomUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.LeftUpgradePanel.BottomPathLevel);
    }

    public Task<bool?> GetIsPlacingMonkeyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.IsPlacingMonkey);
    }

    public Task<bool?> GetCanPlaceHeroAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.CanPlaceHero);
    }

    public Task<bool> IsCoordinateColorMatchAsync(
        WpfPoint scriptCoordinate,
        int expectedR,
        int expectedG,
        int expectedB,
        int tolerance,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<string> GetStageTargetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_lastSnapshot?.StageTarget ?? string.Empty);
    }

    public Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        CaptureSnapshotCallCount++;

        _lastSnapshot = _input.Clicks.Count >= 2
            ? _afterSecondClickSnapshotFactory()
            : _beforeSecondClickSnapshotFactory();

        return Task.FromResult<GameStageStateSnapshot?>(_lastSnapshot);
    }
}
