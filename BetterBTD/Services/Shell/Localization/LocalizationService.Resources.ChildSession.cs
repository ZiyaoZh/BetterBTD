using System;
using System.Collections.Generic;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    private static void AddChildSessionResources(
        Dictionary<string, Dictionary<string, string>> resources)
    {
        var zh = resources["zh-CN"];
        zh["ChildSession.Title"] = "BetterBTD 桌面分身";
        zh["ChildSession.Status.NotConnected"] = "桌面分身未连接。";
        zh["ChildSession.Status.Connected"] = "桌面分身已连接。";
        zh["ChildSession.Status.Starting"] = "正在启动桌面分身...";
        zh["ChildSession.Status.Reconnecting"] = "正在重新连接现有桌面分身...";
        zh["ChildSession.Status.ConnectionTimeout"] = "桌面分身未能在 60 秒内完成 RDP 登录。";
        zh["ChildSession.Status.PrimaryUnavailable"] = "无法通过 Child Session 管道连接主 BetterBTD。";
        zh["ChildSession.Status.Hidden"] = "桌面分身已隐藏；RDP 连接仍保持。";
        zh["ChildSession.Status.AudioMuted"] = "桌面分身音频已静音。";
        zh["ChildSession.Status.AudioEnabled"] = "桌面分身音频已启用。";
        zh["ChildSession.Status.NoActiveSession"] = "没有活动的桌面分身会话。";
        zh["ChildSession.Status.LoggedOff"] = "桌面分身会话 {0} 已注销。";
        zh["ChildSession.Status.Summary"] = "{0} | RDP：{1} | 会话：{2} | 已启用：{3}";
        zh["ChildSession.Status.None"] = "无";
        zh["ChildSession.Status.Yes"] = "是";
        zh["ChildSession.Status.No"] = "否";
        zh["ChildSession.Status.LoginCompleted"] = "桌面分身登录完成。";
        zh["ChildSession.Status.ChildLaunched"] = "已在会话 {0} 启动 BetterBTD 分身。";
        zh["ChildSession.Status.ChildLaunchFailed"] = "启动 BetterBTD 分身失败：{0}";
        zh["ChildSession.Status.PrimaryBlocked"] = "BetterBTD 分身已就绪；主实例控制已禁用。";
        zh["ChildSession.Status.ChildExited"] = "BetterBTD 分身已退出。";
        zh["ChildSession.Status.ChildDisconnected"] = "BetterBTD 分身已断开；桌面分身仍可用。";
        zh["ChildSession.Status.Btd6LaunchFailed"] = "BTD6 启动失败：{0}";
        zh["ChildSession.Status.CaptureUnavailable"] = "BTD6 正在运行，但找不到可捕获的游戏窗口。";
        zh["ChildSession.Status.ChildReady"] = "BetterBTD 分身已就绪；BTD6 正在运行。";
        zh["ChildSession.Status.StartupFailed"] = "分身实例启动失败：{0}";

        var en = resources["en-US"];
        en["ChildSession.Title"] = "BetterBTD Desktop Clone";
        en["ChildSession.Status.NotConnected"] = "Desktop clone is not connected.";
        en["ChildSession.Status.Connected"] = "Desktop clone is connected.";
        en["ChildSession.Status.Starting"] = "Starting desktop clone...";
        en["ChildSession.Status.Reconnecting"] = "Reconnecting to the existing desktop clone...";
        en["ChildSession.Status.ConnectionTimeout"] = "The desktop clone did not complete RDP login within 60 seconds.";
        en["ChildSession.Status.PrimaryUnavailable"] = "Primary BetterBTD was not reachable through the Child Session pipe.";
        en["ChildSession.Status.Hidden"] = "Desktop clone hidden; RDP connection remains active.";
        en["ChildSession.Status.AudioMuted"] = "Desktop clone audio muted.";
        en["ChildSession.Status.AudioEnabled"] = "Desktop clone audio enabled.";
        en["ChildSession.Status.NoActiveSession"] = "No active desktop clone session.";
        en["ChildSession.Status.LoggedOff"] = "Desktop clone session {0} logged off.";
        en["ChildSession.Status.Summary"] = "{0} | RDP: {1} | Session: {2} | Enabled: {3}";
        en["ChildSession.Status.None"] = "none";
        en["ChildSession.Status.Yes"] = "yes";
        en["ChildSession.Status.No"] = "no";
        en["ChildSession.Status.LoginCompleted"] = "Desktop clone login completed.";
        en["ChildSession.Status.ChildLaunched"] = "BetterBTD child instance launched in session {0}.";
        en["ChildSession.Status.ChildLaunchFailed"] = "Child BetterBTD launch failed: {0}";
        en["ChildSession.Status.PrimaryBlocked"] = "Child BetterBTD is ready; primary controls are disabled.";
        en["ChildSession.Status.ChildExited"] = "Child BetterBTD exited.";
        en["ChildSession.Status.ChildDisconnected"] = "Child BetterBTD disconnected; the desktop clone remains available.";
        en["ChildSession.Status.Btd6LaunchFailed"] = "BTD6 launch failed: {0}";
        en["ChildSession.Status.CaptureUnavailable"] = "BTD6 is running, but its window was not available for capture.";
        en["ChildSession.Status.ChildReady"] = "Child instance is ready; BTD6 is running.";
        en["ChildSession.Status.StartupFailed"] = "Child instance startup failed: {0}";
    }
}
