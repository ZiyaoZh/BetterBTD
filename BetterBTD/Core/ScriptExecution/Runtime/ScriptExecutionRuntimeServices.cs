using BetterBTD.Core.Config;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;
using InputMouseButton = Fischless.WindowsInput.MouseButton;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Runtime;

public interface IScriptCaptureService
{
    bool IsRunning { get; }

    bool TryGetCurrentWindowInfo(out GameWindowInfo windowInfo);

    bool TryCaptureFrame(out GameWindowInfo windowInfo, out Mat frame);
}

public interface IScriptInputService
{
    bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo);

    WpfPoint ConvertScriptToScreenCoordinate(WpfPoint scriptCoordinate);

    void MoveMouseToScriptCoordinate(WpfPoint scriptCoordinate);

    void ClickMouseAtScriptCoordinate(
        WpfPoint scriptCoordinate,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = 50);

    void PressHotkey(HotkeyBinding hotkey);

    void PressKey(KeyId key);

    void KeyDown(KeyId key);

    void KeyUp(KeyId key);

    void ReleaseAllKeys();
}

public interface IGameStageStateService
{
    bool IsAvailable { get; }

    Task<bool?> GetIsInLevelAsync(CancellationToken cancellationToken = default);

    Task<int?> GetGoldAsync(CancellationToken cancellationToken = default);

    Task<int?> GetRoundAsync(CancellationToken cancellationToken = default);

    Task<bool?> GetRightUpgradeVisibleAsync(CancellationToken cancellationToken = default);

    Task<int?> GetRightTopUpgradeLevelAsync(CancellationToken cancellationToken = default);

    Task<int?> GetRightMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default);

    Task<int?> GetRightBottomUpgradeLevelAsync(CancellationToken cancellationToken = default);

    Task<bool?> GetLeftUpgradeVisibleAsync(CancellationToken cancellationToken = default);

    Task<int?> GetLeftTopUpgradeLevelAsync(CancellationToken cancellationToken = default);

    Task<int?> GetLeftMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default);

    Task<int?> GetLeftBottomUpgradeLevelAsync(CancellationToken cancellationToken = default);

    Task<bool?> GetIsPlacingMonkeyAsync(CancellationToken cancellationToken = default);

    Task<bool?> GetCanPlaceHeroAsync(CancellationToken cancellationToken = default);

    Task<bool> IsCoordinateColorMatchAsync(
        WpfPoint scriptCoordinate,
        int expectedR,
        int expectedG,
        int expectedB,
        int tolerance,
        CancellationToken cancellationToken = default);

    Task<string> GetStageTargetAsync(CancellationToken cancellationToken = default);

    Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class ScriptExecutionRuntimeServices
{
    public required IScriptCaptureService Capture { get; init; }

    public required IScriptInputService Input { get; init; }

    public required IGameStageStateService GameStageState { get; init; }
}
