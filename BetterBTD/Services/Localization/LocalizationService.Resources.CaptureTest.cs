using System;
using System.Collections.Generic;

namespace BetterBTD.Services;

public sealed partial class LocalizationService
{
    private static Dictionary<string, string> BuildZhCnCaptureTestResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["CaptureTest.WindowTitle"] = "测试图像捕获",
        ["CaptureTest.TargetWindow"] = "目标窗口",
        ["CaptureTest.Mode"] = "模式",
        ["CaptureTest.StartFailed"] = "启动失败",
        ["CaptureTest.WaitingOrFailed"] = "等待捕获帧... / 捕获失败",
        ["CaptureTest.CaptureFailedRecent"] = "最近一次捕获失败",
        ["CaptureTest.FrameSize"] = "帧尺寸",
        ["CaptureTest.AvgCapture"] = "平均截图",
        ["CaptureTest.AvgDisplay"] = "平均显示",
        ["CaptureTest.AvgTotal"] = "平均总耗时",
        ["CaptureTest.SampleCount"] = "样本数"
    };

    private static Dictionary<string, string> BuildEnUsCaptureTestResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["CaptureTest.WindowTitle"] = "Capture Test",
        ["CaptureTest.TargetWindow"] = "Target Window",
        ["CaptureTest.Mode"] = "Mode",
        ["CaptureTest.StartFailed"] = "Start failed",
        ["CaptureTest.WaitingOrFailed"] = "Waiting for frame... / Capture failed",
        ["CaptureTest.CaptureFailedRecent"] = "Most recent capture failed",
        ["CaptureTest.FrameSize"] = "Frame Size",
        ["CaptureTest.AvgCapture"] = "Avg Capture",
        ["CaptureTest.AvgDisplay"] = "Avg Display",
        ["CaptureTest.AvgTotal"] = "Avg Total",
        ["CaptureTest.SampleCount"] = "Samples"
    };
}
