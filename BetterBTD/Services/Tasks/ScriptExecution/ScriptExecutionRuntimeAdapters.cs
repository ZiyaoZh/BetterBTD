using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;
using InputMouseButton = Fischless.WindowsInput.MouseButton;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.ScriptExecution;

public static class ScriptExecutionRuntimeServiceFactory
{
    public static ScriptExecutionRuntimeServices CreateDefault()
    {
        return new ScriptExecutionRuntimeServices
        {
            Capture = new ScriptCaptureServiceAdapter(GameCaptureService.Instance),
            Input = new ScriptInputServiceAdapter(ScriptInputSimulationService.Instance),
            GameStageState = ScriptObservationService.Instance
        };
    }
}

public sealed class ScriptCaptureServiceAdapter : IScriptCaptureService
{
    private readonly GameCaptureService _gameCaptureService;

    public ScriptCaptureServiceAdapter(GameCaptureService gameCaptureService)
    {
        _gameCaptureService = gameCaptureService ?? throw new ArgumentNullException(nameof(gameCaptureService));
    }

    public bool IsRunning => _gameCaptureService.IsRunning;

    public bool TryGetCurrentWindowInfo(out GameWindowInfo windowInfo)
    {
        return _gameCaptureService.TryGetCurrentWindowInfo(out windowInfo);
    }

    public bool TryCaptureFrame(out GameWindowInfo windowInfo, out Mat frame)
    {
        var succeeded = _gameCaptureService.TryCaptureFrame(out windowInfo, out frame);
        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Capture,
            succeeded
                ? $"Shared frame acquired | size={frame.Width}x{frame.Height}."
                : "Shared frame acquisition failed.",
            aggregationKey: "capture:shared-frame",
            replaceExisting: true);
        return succeeded;
    }
}

public sealed class ScriptInputServiceAdapter : IScriptInputService
{
    private readonly ScriptInputSimulationService _scriptInputSimulationService;

    public ScriptInputServiceAdapter(ScriptInputSimulationService scriptInputSimulationService)
    {
        _scriptInputSimulationService = scriptInputSimulationService ?? throw new ArgumentNullException(nameof(scriptInputSimulationService));
    }

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        return _scriptInputSimulationService.TryGetTargetWindowInfo(out windowInfo);
    }

    public WpfPoint ConvertScriptToScreenCoordinate(WpfPoint scriptCoordinate)
    {
        return _scriptInputSimulationService.ConvertScriptToScreenCoordinate(scriptCoordinate);
    }

    public void MoveMouseToScriptCoordinate(WpfPoint scriptCoordinate)
    {
        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Input adapter move mouse to {scriptCoordinate.X:0.##},{scriptCoordinate.Y:0.##}.",
            aggregationKey: "action:move",
            replaceExisting: true);
        _scriptInputSimulationService.MoveMouseToScriptCoordinate(scriptCoordinate);
    }

    public void ClickMouseAtScriptCoordinate(
        WpfPoint scriptCoordinate,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = 50)
    {
        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Input adapter click at {scriptCoordinate.X:0.##},{scriptCoordinate.Y:0.##} | button={button} | clickCount={clickCount} | hold={holdMilliseconds} ms.");
        _scriptInputSimulationService.ClickMouseAtScriptCoordinate(scriptCoordinate, button, clickCount, holdMilliseconds);
    }

    public void PressHotkey(HotkeyBinding hotkey)
    {
        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Input adapter press hotkey '{hotkey.DisplayName}'.");
        _scriptInputSimulationService.PressHotkey(hotkey);
    }

    public void PressKey(KeyId key)
    {
        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Input adapter press key '{key}'.");
        _scriptInputSimulationService.PressKey(key);
    }

    public void KeyDown(KeyId key)
    {
        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Input adapter key down '{key}'.",
            aggregationKey: $"action:key-down:{key}",
            replaceExisting: true);
        _scriptInputSimulationService.KeyDown(key);
    }

    public void KeyUp(KeyId key)
    {
        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Input adapter key up '{key}'.",
            aggregationKey: $"action:key-up:{key}",
            replaceExisting: true);
        _scriptInputSimulationService.KeyUp(key);
    }

    public void ReleaseAllKeys()
    {
        _scriptInputSimulationService.ReleaseAllKeys();
    }
}

