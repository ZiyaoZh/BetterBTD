using System;
using System.Collections.Generic;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    private static Dictionary<string, string> BuildZhCnEditorPlacementResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Editor.Property.PlacementDetection"] = "\u653e\u7f6e\u68c0\u6d4b",
        ["Editor.Property.PlacementFailureAdjustment"] = "\u5931\u8d25\u5fae\u8c03",
        ["Editor.Property.PlacementAttemptIntervalMilliseconds"] = "\u653e\u7f6e\u5c1d\u8bd5\u95f4\u9694\uff08ms\uff09",
        ["Editor.Property.PlacementAdjustmentAttemptIntervalMilliseconds"] = "\u5fae\u8c03\u5c1d\u8bd5\u95f4\u9694\uff08ms\uff09",
        ["Editor.Property.UpgradeDetection"] = "\u5347\u7ea7\u68c0\u6d4b",
        ["Editor.Property.UpgradeDetectionIntervalMilliseconds"] = "\u68c0\u6d4b\u95f4\u9694\uff08ms\uff09",
        ["Editor.Property.UpgradeOperationIntervalMilliseconds"] = "\u64cd\u4f5c\u95f4\u9694\uff08ms\uff09",
        ["Editor.Property.MonkeyPanelDetection"] = "\u9762\u677f\u68c0\u6d4b",
        ["Editor.Property.MonkeyPanelDetectionIntervalMilliseconds"] = "\u68c0\u6d4b\u95f4\u9694\uff08ms\uff09",
        ["Editor.Property.MonkeyPanelOperationIntervalMilliseconds"] = "\u64cd\u4f5c\u95f4\u9694\uff08ms\uff09",
        ["Editor.Property.SellDetection"] = "\u51fa\u552e\u68c0\u6d4b"
    };

    private static Dictionary<string, string> BuildEnUsEditorPlacementResources() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Editor.Property.PlacementDetection"] = "Placement Detection",
        ["Editor.Property.PlacementFailureAdjustment"] = "Failure Adjustment",
        ["Editor.Property.PlacementAttemptIntervalMilliseconds"] = "Placement Attempt Interval (ms)",
        ["Editor.Property.PlacementAdjustmentAttemptIntervalMilliseconds"] = "Adjustment Attempt Interval (ms)",
        ["Editor.Property.UpgradeDetection"] = "Upgrade Detection",
        ["Editor.Property.UpgradeDetectionIntervalMilliseconds"] = "Detection Interval (ms)",
        ["Editor.Property.UpgradeOperationIntervalMilliseconds"] = "Operation Interval (ms)",
        ["Editor.Property.MonkeyPanelDetection"] = "Panel Detection",
        ["Editor.Property.MonkeyPanelDetectionIntervalMilliseconds"] = "Detection Interval (ms)",
        ["Editor.Property.MonkeyPanelOperationIntervalMilliseconds"] = "Operation Interval (ms)",
        ["Editor.Property.SellDetection"] = "Sell Detection"
    };
}
