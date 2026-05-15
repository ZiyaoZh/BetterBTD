using System;
using System.Collections.Generic;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    private static Dictionary<string, string> BuildZhCnTasksResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tasks.PageTitle"] = "自动任务",
        ["Tasks.PageDescription"] = "每个任务独立配置读取间隔和操作间隔。",
        ["Tasks.OperationInterval"] = "操作间隔",
        ["Tasks.OperationIntervalDesc"] = "执行按键和点击动作的间隔（毫秒）",
        ["Tasks.MapLabel"] = "地图",
        ["Tasks.MapDescription"] = "为当前任务选择目标地图。",
        ["Tasks.DifficultyLabel"] = "难度",
        ["Tasks.DifficultyDescription"] = "独立配置任务难度。",
        ["Tasks.ModeLabel"] = "模式",
        ["Tasks.ModeDescription"] = "为任务选择执行模式。",
        ["Tasks.Map.BeginnersTrack"] = "新手赛道",
        ["Tasks.Map.MonkeyMeadow"] = "猴子草地",
        ["Tasks.Map.DarkCastle"] = "黑暗城堡",
        ["Tasks.Difficulty.Easy"] = "简单",
        ["Tasks.Difficulty.Medium"] = "中等",
        ["Tasks.Difficulty.Hard"] = "困难",
        ["Tasks.Mode.Standard"] = "标准",
        ["Tasks.Mode.Deflation"] = "放气",
        ["Tasks.Mode.Chimps"] = "CHIMPS",
        ["Tasks.Tutorial"] = "打开教程",
        ["Tasks.TutorialUrl"] = "https://wpfui.lepo.co/documentation/",
        ["Tasks.Start"] = "启动",
        ["Tasks.Stop"] = "停止",
        ["Tasks.custom.Title"] = "自定义模式",
        ["Tasks.custom.Description"] = "按当前脚本进行自动化执行。",
        ["Tasks.collection.Title"] = "刷收集",
        ["Tasks.collection.Description"] = "活动收集循环任务。",
        ["Tasks.blackborder.Title"] = "刷黑框",
        ["Tasks.blackborder.Description"] = "黑框地图专用任务流程。",
        ["Tasks.race.Title"] = "刷竞速",
        ["Tasks.race.Description"] = "竞速模式自动任务。"
    };

    private static Dictionary<string, string> BuildEnUsTasksResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tasks.PageTitle"] = "Auto Tasks",
        ["Tasks.PageDescription"] = "Each task has independent read/action interval settings.",
        ["Tasks.OperationInterval"] = "Action interval",
        ["Tasks.OperationIntervalDesc"] = "Interval between key/mouse actions (ms)",
        ["Tasks.MapLabel"] = "Map",
        ["Tasks.MapDescription"] = "Select target map for this task.",
        ["Tasks.DifficultyLabel"] = "Difficulty",
        ["Tasks.DifficultyDescription"] = "Configure task difficulty independently.",
        ["Tasks.ModeLabel"] = "Mode",
        ["Tasks.ModeDescription"] = "Select execution mode for this task.",
        ["Tasks.Map.BeginnersTrack"] = "Beginner's Track",
        ["Tasks.Map.MonkeyMeadow"] = "Monkey Meadow",
        ["Tasks.Map.DarkCastle"] = "Dark Castle",
        ["Tasks.Difficulty.Easy"] = "Easy",
        ["Tasks.Difficulty.Medium"] = "Medium",
        ["Tasks.Difficulty.Hard"] = "Hard",
        ["Tasks.Mode.Standard"] = "Standard",
        ["Tasks.Mode.Deflation"] = "Deflation",
        ["Tasks.Mode.Chimps"] = "CHIMPS",
        ["Tasks.Tutorial"] = "Open tutorial",
        ["Tasks.TutorialUrl"] = "https://wpfui.lepo.co/documentation/",
        ["Tasks.Start"] = "Start",
        ["Tasks.Stop"] = "Stop",
        ["Tasks.custom.Title"] = "Custom Mode",
        ["Tasks.custom.Description"] = "Automation based on selected script.",
        ["Tasks.collection.Title"] = "Collection Farming",
        ["Tasks.collection.Description"] = "Loop workflow for event collection.",
        ["Tasks.blackborder.Title"] = "Black Border Farming",
        ["Tasks.blackborder.Description"] = "Task flow for black border maps.",
        ["Tasks.race.Title"] = "Race Farming",
        ["Tasks.race.Description"] = "Automated tasks for race mode."
    };
}
