using System;
using System.Collections.Generic;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    private static Dictionary<string, string> BuildZhCnShellResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Main.AppSubtitle"] = "Automation Workspace",
        ["Main.ConfigCenter"] = "配置中心",

        ["Nav.Start.Title"] = "开始",
        ["Nav.Start.Description"] = "启动游戏与截图工具",
        ["Nav.Tasks.Title"] = "自动任务",
        ["Nav.Tasks.Description"] = "按模式执行自动任务",
        ["Nav.Editor.Title"] = "脚本编辑器",
        ["Nav.Editor.Description"] = "编辑与调试指令",
        ["Nav.Library.Title"] = "我的脚本",
        ["Nav.Library.Description"] = "分类管理脚本库",
        ["Nav.Settings.Title"] = "选项设置",
        ["Nav.Settings.Description"] = "设置中心",
        ["Nav.Logs.Title"] = "运行日志",
        ["Nav.Logs.Description"] = "执行反馈与排错",

        ["Header.Start.Title"] = "开始页面",
        ["Header.Start.Subtitle"] = "快速启动游戏与截图工具",
        ["Header.Tasks.Title"] = "自动任务",
        ["Header.Tasks.Subtitle"] = "自定义模式、刷收集、刷黑框、刷竞速"
    };

    private static Dictionary<string, string> BuildEnUsShellResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Main.AppSubtitle"] = "Automation Workspace",
        ["Main.ConfigCenter"] = "Configuration Center",

        ["Nav.Start.Title"] = "Start",
        ["Nav.Start.Description"] = "Launch game and capture tool",
        ["Nav.Tasks.Title"] = "Auto Tasks",
        ["Nav.Tasks.Description"] = "Run by task mode",
        ["Nav.Editor.Title"] = "Script Editor",
        ["Nav.Editor.Description"] = "Edit and debug commands",
        ["Nav.Library.Title"] = "My Scripts",
        ["Nav.Library.Description"] = "Categorized script library",
        ["Nav.Settings.Title"] = "Options",
        ["Nav.Settings.Description"] = "Settings center",
        ["Nav.Logs.Title"] = "Logs",
        ["Nav.Logs.Description"] = "Execution feedback and diagnostics",

        ["Header.Start.Title"] = "Start",
        ["Header.Start.Subtitle"] = "Quick launch for game and capture utility",
        ["Header.Tasks.Title"] = "Auto Tasks",
        ["Header.Tasks.Subtitle"] = "Custom, Collection, Black Border, Race"
    };
}
