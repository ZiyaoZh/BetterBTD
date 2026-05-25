using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Services.Start.Capture;

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
        double averageReadMilliseconds,
        GameUiSnapshot? gameUiSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        var summaryText = BuildSummaryText(localizationService, isAvailable, failed, failureMessage, averageReadMilliseconds);
        var detailsText = BuildDetailsText(localizationService, snapshot, gameUiSnapshot);

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

    private static string BuildDetailsText(
        LocalizationService localizationService,
        GameStageStateSnapshot? snapshot,
        GameUiSnapshot? gameUiSnapshot)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        var shouldDisplayInLevelValues = snapshot?.IsInLevel == true;
        var details = new List<string>
        {
            $"{localizationService.T("CaptureTest.GameUi")}: {FormatGameUiState(localizationService, gameUiSnapshot?.State)}",
            $"{localizationService.T("CaptureTest.InLevel")}: {FormatNullableBool(localizationService, snapshot?.IsInLevel)}"
        };

        if (shouldDisplayInLevelValues)
        {
            details.Add($"{localizationService.T("CaptureTest.Gold")}: {FormatNullableInt(localizationService, snapshot?.Gold)}");
            details.Add($"{localizationService.T("CaptureTest.Round")}: {FormatNullableInt(localizationService, snapshot?.Round)}");
            details.Add($"{localizationService.T("CaptureTest.RightUpgradeVisible")}: {FormatNullableBool(localizationService, snapshot?.RightUpgradePanel.IsVisible)}");
            details.Add($"{localizationService.T("CaptureTest.RightUpgradeLevels")}: {FormatPanelLevels(localizationService, snapshot?.RightUpgradePanel)}");
            details.Add($"{localizationService.T("CaptureTest.LeftUpgradeVisible")}: {FormatNullableBool(localizationService, snapshot?.LeftUpgradePanel.IsVisible)}");
            details.Add($"{localizationService.T("CaptureTest.LeftUpgradeLevels")}: {FormatPanelLevels(localizationService, snapshot?.LeftUpgradePanel)}");
            details.Add($"{localizationService.T("CaptureTest.IsPlacingMonkey")}: {FormatNullableBool(localizationService, snapshot?.IsPlacingMonkey)}");
            details.Add($"{localizationService.T("CaptureTest.CanPlaceHero")}: {FormatNullableBool(localizationService, snapshot?.CanPlaceHero)}");
            details.Add($"{localizationService.T("CaptureTest.StageTarget")}: {FormatText(localizationService, snapshot?.StageTarget)}");
        }

        var mapRecognitionText = FormatCollectionMapRecognition(localizationService, gameUiSnapshot);
        if (!string.IsNullOrWhiteSpace(mapRecognitionText))
        {
            details.Add($"{localizationService.T("CaptureTest.MapRecognition")}: {mapRecognitionText}");
        }

        return string.Join(" | ", details);
    }

    private static string FormatCollectionMapRecognition(
        LocalizationService localizationService,
        GameUiSnapshot? gameUiSnapshot)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        if (gameUiSnapshot?.State != GameUiStateId.MapSearchResults)
        {
            return string.Empty;
        }

        if (!gameUiSnapshot.Facts.TryGetValue("collectionMapMatches", out var rawMatches) ||
            rawMatches is not IReadOnlyList<MapTemplateMatchResult> matches ||
            matches.Count == 0)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        GameMapType? recognizedMap = null;
        if (gameUiSnapshot.Facts.TryGetValue("collectionMap", out var rawMap) && rawMap is GameMapType map)
        {
            recognizedMap = map;
        }

        var recognizedMatch = recognizedMap.HasValue
            ? matches.FirstOrDefault(match => match.MapType == recognizedMap.Value)
            : null;
        var recognizedText = recognizedMap.HasValue
            ? recognizedMatch is not null
                ? $"{GameElementCatalog.GetMapDisplayName(recognizedMap.Value)} ({recognizedMatch.MatchInfo.Score:P2})"
                : GameElementCatalog.GetMapDisplayName(recognizedMap.Value)
            : localizationService.T("CaptureTest.UnknownValue");

        var candidateText = string.Join(
            ", ",
            matches.Select(match => $"{GameElementCatalog.GetMapDisplayName(match.MapType)} {match.MatchInfo.Score:P2}"));

        return string.IsNullOrWhiteSpace(candidateText)
            ? recognizedText
            : $"{recognizedText} -> {candidateText}";
    }

    private static string FormatGameUiState(LocalizationService localizationService, GameUiStateId? state)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        if (!state.HasValue)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        var localizationKey = $"CaptureTest.GameUiState.{state.Value}";
        var localizedText = localizationService.T(localizationKey);
        return string.Equals(localizedText, localizationKey, StringComparison.Ordinal)
            ? state.Value.ToString()
            : localizedText;
    }

    private static string FormatPanelLevels(LocalizationService localizationService, GameStageUpgradePanelState? panelState, bool shouldDisplayValue = true)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return $"{localizationService.T("CaptureTest.PathTop")}={FormatNullableInt(localizationService, panelState?.TopPathLevel, shouldDisplayValue)}/" +
               $"{localizationService.T("CaptureTest.PathMiddle")}={FormatNullableInt(localizationService, panelState?.MiddlePathLevel, shouldDisplayValue)}/" +
               $"{localizationService.T("CaptureTest.PathBottom")}={FormatNullableInt(localizationService, panelState?.BottomPathLevel, shouldDisplayValue)}";
    }

    private static string FormatNullableBool(LocalizationService localizationService, bool? value, bool shouldDisplayValue = true)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        if (!shouldDisplayValue)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        if (!value.HasValue)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        return value.Value
            ? localizationService.T("CaptureTest.BoolTrue")
            : localizationService.T("CaptureTest.BoolFalse");
    }

    private static string FormatNullableInt(LocalizationService localizationService, int? value, bool shouldDisplayValue = true)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        if (!shouldDisplayValue)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        return value?.ToString() ?? localizationService.T("CaptureTest.UnknownValue");
    }

    private static string FormatText(LocalizationService localizationService, string? value, bool shouldDisplayValue = true)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        if (!shouldDisplayValue)
        {
            return localizationService.T("CaptureTest.UnknownValue");
        }

        return string.IsNullOrWhiteSpace(value) ? localizationService.T("CaptureTest.UnknownValue") : value;
    }
}

