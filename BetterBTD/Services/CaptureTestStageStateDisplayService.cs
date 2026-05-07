using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Services;

public sealed class CaptureTestStageStateDisplayService
{
    private static readonly Lazy<CaptureTestStageStateDisplayService> InstanceHolder = new(() => new CaptureTestStageStateDisplayService());

    private CaptureTestStageStateDisplayService()
    {
    }

    public static CaptureTestStageStateDisplayService Instance => InstanceHolder.Value;

    public CaptureTestStageStateDisplayModel Build(
        LocalizationService localizationService,
        bool isAvailable,
        bool failed,
        string? failureMessage,
        GameStageStateSnapshot? snapshot,
        double averageReadMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        var summaryText = BuildSummaryText(localizationService, isAvailable, failed, failureMessage, averageReadMilliseconds);
        var detailsText = BuildDetailsText(localizationService, snapshot);

        return new CaptureTestStageStateDisplayModel
        {
            SummaryText = summaryText,
            DetailsText = detailsText
        };
    }

    private static string BuildSummaryText(
        LocalizationService localizationService,
        bool isAvailable,
        bool failed,
        string? failureMessage,
        double averageReadMilliseconds)
    {
        var stageStateLabel = localizationService.T("CaptureTest.StageState");
        var averageLabel = localizationService.T("CaptureTest.AvgStageState");
        if (!isAvailable)
        {
            return $"{stageStateLabel}: {localizationService.T("CaptureTest.OcrUnavailable")}";
        }

        if (failed)
        {
            return $"{stageStateLabel}: {failureMessage ?? localizationService.T("CaptureTest.OcrFailedRecent")} | " +
                   $"{averageLabel}: {averageReadMilliseconds:F2} ms";
        }

        return $"{stageStateLabel}: {localizationService.T("CaptureTest.StatusOk")} | {averageLabel}: {averageReadMilliseconds:F2} ms";
    }

    private static string BuildDetailsText(LocalizationService localizationService, GameStageStateSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return string.Join(" | ",
        [
            $"{localizationService.T("CaptureTest.InLevel")}: {FormatNullableBool(localizationService, snapshot?.IsInLevel)}",
            $"{localizationService.T("CaptureTest.Gold")}: {FormatNullableInt(localizationService, snapshot?.Gold)}",
            $"{localizationService.T("CaptureTest.Round")}: {FormatNullableInt(localizationService, snapshot?.Round)}",
            $"{localizationService.T("CaptureTest.RightUpgradeVisible")}: {FormatNullableBool(localizationService, snapshot?.RightUpgradePanel.IsVisible)}",
            $"{localizationService.T("CaptureTest.RightUpgradeLevels")}: {FormatPanelLevels(localizationService, snapshot?.RightUpgradePanel)}",
            $"{localizationService.T("CaptureTest.LeftUpgradeVisible")}: {FormatNullableBool(localizationService, snapshot?.LeftUpgradePanel.IsVisible)}",
            $"{localizationService.T("CaptureTest.LeftUpgradeLevels")}: {FormatPanelLevels(localizationService, snapshot?.LeftUpgradePanel)}",
            $"{localizationService.T("CaptureTest.IsPlacingMonkey")}: {FormatNullableBool(localizationService, snapshot?.IsPlacingMonkey)}",
            $"{localizationService.T("CaptureTest.CanPlaceHero")}: {FormatNullableBool(localizationService, snapshot?.CanPlaceHero)}",
            $"{localizationService.T("CaptureTest.StageTarget")}: {FormatText(localizationService, snapshot?.StageTarget)}"
        ]);
    }

    private static string FormatPanelLevels(LocalizationService localizationService, GameStageUpgradePanelState? panelState)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return $"{localizationService.T("CaptureTest.PathTop")}={FormatNullableInt(localizationService, panelState?.TopPathLevel)}/" +
               $"{localizationService.T("CaptureTest.PathMiddle")}={FormatNullableInt(localizationService, panelState?.MiddlePathLevel)}/" +
               $"{localizationService.T("CaptureTest.PathBottom")}={FormatNullableInt(localizationService, panelState?.BottomPathLevel)}";
    }

    private static string FormatNullableBool(LocalizationService localizationService, bool? value)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        if (!value.HasValue)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        return value.Value
            ? localizationService.T("CaptureTest.BoolTrue")
            : localizationService.T("CaptureTest.BoolFalse");
    }

    private static string FormatNullableInt(LocalizationService localizationService, int? value)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return value?.ToString() ?? localizationService.T("CaptureTest.UnknownValue");
    }

    private static string FormatText(LocalizationService localizationService, string? value)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return string.IsNullOrWhiteSpace(value) ? localizationService.T("CaptureTest.UnknownValue") : value;
    }
}
