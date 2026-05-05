using BetterBTD.Core.Config;

namespace BetterBTD.Models;

public sealed class AppConfiguration
{
    public string MaskWindowTargetTitle { get; set; } = "testWindow";

    public string LanguageCode { get; set; } = "zh-CN";

    public string ThemeMode { get; set; } = "Dark";

    public string GameLanguageCode { get; set; } = "zh-CN";

    public string StartHotkey { get; set; } = "F1";

    public string StopHotkey { get; set; } = "F2";

    public string GameStartHotkey { get; set; } = "F5";

    public string GameStopHotkey { get; set; } = "F6";

    public KeyBindingsConfig KeyBindings { get; set; } = new();
}
