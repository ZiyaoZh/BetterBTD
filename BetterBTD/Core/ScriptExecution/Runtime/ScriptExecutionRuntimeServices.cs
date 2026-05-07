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
}

public interface IGameStageStateService
{
    bool IsAvailable { get; }

    Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class ScriptExecutionRuntimeServices
{
    public required IScriptCaptureService Capture { get; init; }

    public required IScriptInputService Input { get; init; }

    public required IGameStageStateService GameStageState { get; init; }
}
