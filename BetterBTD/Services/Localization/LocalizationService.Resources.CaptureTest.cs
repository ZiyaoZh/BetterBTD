using System;
using System.Collections.Generic;

namespace BetterBTD.Services;

public sealed partial class LocalizationService
{
    private static Dictionary<string, string> BuildZhCnCaptureTestResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["CaptureTest.WindowTitle"] = "\u6d4b\u8bd5\u56fe\u50cf\u6355\u83b7",
        ["CaptureTest.TargetWindow"] = "\u76ee\u6807\u7a97\u53e3",
        ["CaptureTest.Mode"] = "\u6a21\u5f0f",
        ["CaptureTest.StartFailed"] = "\u542f\u52a8\u5931\u8d25",
        ["CaptureTest.WaitingOrFailed"] = "\u7b49\u5f85\u6355\u83b7\u5e27... / \u6355\u83b7\u5931\u8d25",
        ["CaptureTest.CaptureFailedRecent"] = "\u6700\u8fd1\u4e00\u6b21\u6355\u83b7\u5931\u8d25",
        ["CaptureTest.FrameSize"] = "\u5e27\u5c3a\u5bf8",
        ["CaptureTest.AvgCapture"] = "\u5e73\u5747\u622a\u56fe",
        ["CaptureTest.AvgDisplay"] = "\u5e73\u5747\u663e\u793a",
        ["CaptureTest.AvgTotal"] = "\u5e73\u5747\u603b\u8017\u65f6",
        ["CaptureTest.SampleCount"] = "\u6837\u672c\u6570",
        ["CaptureTest.Ocr"] = "OCR",
        ["CaptureTest.Gold"] = "\u91d1\u5e01",
        ["CaptureTest.Round"] = "\u56de\u5408",
        ["CaptureTest.AvgOcr"] = "\u5e73\u5747 OCR",
        ["CaptureTest.OcrUnavailable"] = "OCR \u6a21\u677f\u4e0d\u53ef\u7528",
        ["CaptureTest.OcrFailedRecent"] = "\u6700\u8fd1\u4e00\u6b21 OCR \u8bc6\u522b\u5931\u8d25",
        ["CaptureTest.RegionGold"] = "\u91d1\u5e01\u533a\u57df",
        ["CaptureTest.RegionRound"] = "\u56de\u5408\u533a\u57df",
        ["CaptureTest.StageState"] = "\u5173\u5361\u72b6\u6001",
        ["CaptureTest.AvgStageState"] = "\u5e73\u5747\u72b6\u6001\u8bfb\u53d6",
        ["CaptureTest.StatusOk"] = "\u6b63\u5e38",
        ["CaptureTest.InLevel"] = "\u5173\u5361\u5185",
        ["CaptureTest.RightUpgradeVisible"] = "\u53f3\u4fa7\u5347\u7ea7\u53ef\u89c1",
        ["CaptureTest.RightUpgradeLevels"] = "\u53f3\u4fa7\u5347\u7ea7\u7b49\u7ea7",
        ["CaptureTest.LeftUpgradeVisible"] = "\u5de6\u4fa7\u5347\u7ea7\u53ef\u89c1",
        ["CaptureTest.LeftUpgradeLevels"] = "\u5de6\u4fa7\u5347\u7ea7\u7b49\u7ea7",
        ["CaptureTest.IsPlacingMonkey"] = "\u653e\u7f6e\u7334\u5b50",
        ["CaptureTest.CanPlaceHero"] = "\u82f1\u96c4\u53ef\u653e",
        ["CaptureTest.StageTarget"] = "\u5173\u5361\u76ee\u6807",
        ["CaptureTest.PathTop"] = "\u4e0a",
        ["CaptureTest.PathMiddle"] = "\u4e2d",
        ["CaptureTest.PathBottom"] = "\u4e0b",
        ["CaptureTest.BoolTrue"] = "\u662f",
        ["CaptureTest.BoolFalse"] = "\u5426",
        ["CaptureTest.UnknownValue"] = "--"
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
        ["CaptureTest.SampleCount"] = "Samples",
        ["CaptureTest.Ocr"] = "OCR",
        ["CaptureTest.Gold"] = "Gold",
        ["CaptureTest.Round"] = "Round",
        ["CaptureTest.AvgOcr"] = "Avg OCR",
        ["CaptureTest.OcrUnavailable"] = "OCR templates are unavailable",
        ["CaptureTest.OcrFailedRecent"] = "Most recent OCR attempt failed",
        ["CaptureTest.RegionGold"] = "Gold",
        ["CaptureTest.RegionRound"] = "Round",
        ["CaptureTest.StageState"] = "Stage State",
        ["CaptureTest.AvgStageState"] = "Avg State Read",
        ["CaptureTest.StatusOk"] = "OK",
        ["CaptureTest.InLevel"] = "In Level",
        ["CaptureTest.RightUpgradeVisible"] = "Right Upgrade Visible",
        ["CaptureTest.RightUpgradeLevels"] = "Right Upgrade Levels",
        ["CaptureTest.LeftUpgradeVisible"] = "Left Upgrade Visible",
        ["CaptureTest.LeftUpgradeLevels"] = "Left Upgrade Levels",
        ["CaptureTest.IsPlacingMonkey"] = "Placing Monkey",
        ["CaptureTest.CanPlaceHero"] = "Can Place Hero",
        ["CaptureTest.StageTarget"] = "Stage Target",
        ["CaptureTest.PathTop"] = "Top",
        ["CaptureTest.PathMiddle"] = "Middle",
        ["CaptureTest.PathBottom"] = "Bottom",
        ["CaptureTest.BoolTrue"] = "Yes",
        ["CaptureTest.BoolFalse"] = "No",
        ["CaptureTest.UnknownValue"] = "--"
    };
}
