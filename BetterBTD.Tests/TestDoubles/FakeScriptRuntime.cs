using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;
using InputMouseButton = Fischless.WindowsInput.MouseButton;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Tests.TestDoubles;

internal sealed class NullScriptCaptureService : IScriptCaptureService
{
    public bool IsRunning => true;

    public bool TryGetCurrentWindowInfo(out GameWindowInfo windowInfo)
    {
        windowInfo = default!;
        return false;
    }

    public bool TryCaptureFrame(out GameWindowInfo windowInfo, out Mat frame)
    {
        windowInfo = default!;
        frame = new Mat();
        return false;
    }
}

internal sealed class RecordingScriptInputService : IScriptInputService
{
    public List<WpfPoint> MovedCoordinates { get; } = [];

    public List<RecordedClick> Clicks { get; } = [];

    public List<HotkeyBinding> PressedHotkeys { get; } = [];

    public List<KeyId> PressedKeys { get; } = [];

    public List<KeyId> KeyDownEvents { get; } = [];

    public List<KeyId> KeyUpEvents { get; } = [];

    public int ReleaseAllKeysCallCount { get; private set; }

    public Exception? ReleaseAllKeysException { get; set; }

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        windowInfo = default!;
        return false;
    }

    public WpfPoint ConvertScriptToScreenCoordinate(WpfPoint scriptCoordinate)
    {
        return scriptCoordinate;
    }

    public void MoveMouseToScriptCoordinate(WpfPoint scriptCoordinate)
    {
        MovedCoordinates.Add(scriptCoordinate);
    }

    public void ClickMouseAtScriptCoordinate(
        WpfPoint scriptCoordinate,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = 50)
    {
        Clicks.Add(new RecordedClick(scriptCoordinate, button, clickCount, holdMilliseconds));
    }

    public void PressHotkey(HotkeyBinding hotkey)
    {
        PressedHotkeys.Add(Clone(hotkey));
    }

    public void PressKey(KeyId key)
    {
        PressedKeys.Add(key);
    }

    public void KeyDown(KeyId key)
    {
        KeyDownEvents.Add(key);
    }

    public void KeyUp(KeyId key)
    {
        KeyUpEvents.Add(key);
    }

    public void ReleaseAllKeys()
    {
        ReleaseAllKeysCallCount++;
        if (ReleaseAllKeysException is not null)
        {
            throw ReleaseAllKeysException;
        }
    }

    private static HotkeyBinding Clone(HotkeyBinding hotkey)
    {
        return new HotkeyBinding
        {
            Modifiers = hotkey.Modifiers,
            Key = hotkey.Key
        };
    }
}

internal sealed record RecordedClick(WpfPoint Coordinate, InputMouseButton Button, int ClickCount, int HoldMilliseconds);

internal sealed class QueueGameStageStateService : IGameStageStateService
{
    private readonly Queue<GameStageStateSnapshot?> _snapshots;
    private GameStageStateSnapshot? _lastSnapshot;

    public QueueGameStageStateService(IEnumerable<GameStageStateSnapshot?> snapshots)
    {
        _snapshots = new Queue<GameStageStateSnapshot?>(snapshots);
        _lastSnapshot = _snapshots.Count > 0 ? _snapshots.Peek() : new GameStageStateSnapshot();
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

        if (_snapshots.Count > 0)
        {
            _lastSnapshot = _snapshots.Dequeue();
        }

        return Task.FromResult(_lastSnapshot);
    }
}
