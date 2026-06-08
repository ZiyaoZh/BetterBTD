using BetterBTD.Core.Config;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Models;

public sealed class AppConfiguration
{
    public string MaskWindowTargetTitle { get; set; } = "BloonsTD6";

    public string CaptureModeName { get; set; } = "WindowsGraphicsCapture";

    public int CaptureIntervalMs { get; set; } = 50;

    public bool AutoFixWin11BitBlt { get; set; }

    public bool LaunchGameWithCapturer { get; set; }

    public string GameInstallPath { get; set; } = string.Empty;

    public string LanguageCode { get; set; } = "zh-CN";

    public string ThemeMode { get; set; } = "Dark";

    public string GameLanguageCode { get; set; } = "zh-CN";

    public string KeyboardMouseSimulationModeName { get; set; } = KeyboardMouseSimulationModeExtensions.StandardConfigurationValue;

    public string StartHotkey { get; set; } = "F1";

    public string StopHotkey { get; set; } = "F2";

    public string GameStartHotkey { get; set; } = "F5";

    public string GameStopHotkey { get; set; } = "F6";

    public string ScriptExecutionIntervalStrategyName { get; set; } =
        nameof(ScriptExecutionOperationIntervalStrategy.InstructionCustom);

    public int ScriptExecutionCommonOperationIntervalMs { get; set; } = 200;

    public KeyBindingsConfig KeyBindings { get; set; } = new();
}
