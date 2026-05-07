using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;
using InputMouseButton = Fischless.WindowsInput.MouseButton;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.ScriptExecution;

public static class ScriptExecutionRuntimeServiceFactory
{
    public static ScriptExecutionRuntimeServices CreateDefault()
    {
        return new ScriptExecutionRuntimeServices
        {
            Capture = new ScriptCaptureServiceAdapter(GameCaptureService.Instance),
            Input = new ScriptInputServiceAdapter(ScriptInputSimulationService.Instance),
            GameStageState = GameStageStateService.Instance
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
        return _gameCaptureService.TryCaptureFrame(out windowInfo, out frame);
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
        _scriptInputSimulationService.MoveMouseToScriptCoordinate(scriptCoordinate);
    }

    public void ClickMouseAtScriptCoordinate(
        WpfPoint scriptCoordinate,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = 50)
    {
        _scriptInputSimulationService.ClickMouseAtScriptCoordinate(scriptCoordinate, button, clickCount, holdMilliseconds);
    }

    public void PressHotkey(HotkeyBinding hotkey)
    {
        _scriptInputSimulationService.PressHotkey(hotkey);
    }

    public void PressKey(KeyId key)
    {
        _scriptInputSimulationService.PressKey(key);
    }
}
