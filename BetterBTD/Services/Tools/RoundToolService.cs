using System.Globalization;
using System.IO;
using BetterBTD.Models.Rounds;
using BetterBTD.Models.Tools;
using BetterBTD.Services.Shared;

namespace BetterBTD.Services.Tools;

public sealed class RoundToolService
{
    private static readonly Lazy<RoundToolService> InstanceHolder = new(
        () => new RoundToolService(LocalizationService.Instance, RoundCatalogService.Instance));

    private readonly LocalizationService _localizationService;
    private readonly RoundCatalogService _roundCatalogService;

    internal RoundToolService(
        LocalizationService localizationService,
        RoundCatalogService roundCatalogService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _roundCatalogService = roundCatalogService ?? throw new ArgumentNullException(nameof(roundCatalogService));
    }

    public static RoundToolService Instance => InstanceHolder.Value;

    public int TryGetMaxRound()
    {
        try
        {
            return _roundCatalogService.GetMaxRound();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return 0;
        }
    }

    public int NormalizeRound(int value, int maxRound)
    {
        if (maxRound <= 0)
        {
            return Math.Max(1, value);
        }

        return Math.Clamp(value, 1, maxRound);
    }

    public string BuildResult(RoundToolRequest request, int maxRound)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (maxRound <= 0)
        {
            return string.Format(
                _localizationService.T("Tools.Round.Result.LoadFailed"),
                _localizationService.T("Tools.Round.Result.LoadFailedReason"));
        }

        if (request.StartRound > request.EndRound)
        {
            return string.Format(
                _localizationService.T("Tools.Round.Result.InvalidRange"),
                maxRound);
        }

        try
        {
            var summary = _roundCatalogService.CalculateRange(request.StartRound, request.EndRound);
            return BuildSummaryText(summary);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return string.Format(
                _localizationService.T("Tools.Round.Result.LoadFailed"),
                ex.Message);
        }
    }

    private string BuildSummaryText(RoundRangeSummary summary)
    {
        var topBloons = summary.BloonTotals
            .Take(5)
            .Select(bloon => string.Format(
                _localizationService.T("Tools.Round.Result.TopBloonItem"),
                GetBloonDisplayName(bloon.Type, bloon.IsCamo, bloon.IsRegrow, bloon.IsFortified),
                FormatInteger(bloon.TotalCount)))
            .ToArray();

        return string.Join(
            Environment.NewLine,
            [
                string.Format(
                    _localizationService.T("Tools.Round.Result.Range"),
                    summary.StartRound,
                    summary.EndRound,
                    summary.RoundCount),
                string.Format(
                    _localizationService.T("Tools.Round.Result.TotalCashReward"),
                    FormatDecimal(summary.TotalCashReward)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.TotalExperience"),
                    FormatInteger(summary.TotalExperience)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.TotalRbe"),
                    FormatInteger(summary.TotalRbe)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.TotalDuration"),
                    FormatDuration(summary.TotalDurationSeconds)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.AveragePerRound"),
                    FormatDecimal(summary.TotalCashReward / summary.RoundCount),
                    FormatDecimal(summary.TotalExperience / (double)summary.RoundCount),
                    FormatDecimal(summary.TotalRbe / (double)summary.RoundCount),
                    FormatDuration(summary.TotalDurationSeconds / summary.RoundCount)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.PeakCashReward"),
                    summary.PeakCashRewardRound.Round,
                    FormatDecimal(summary.PeakCashRewardRound.Value)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.PeakRbe"),
                    summary.PeakRbeRound.Round,
                    FormatInteger(summary.PeakRbeRound.Value)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.PeakDuration"),
                    summary.PeakDurationRound.Round,
                    FormatDuration(summary.PeakDurationRound.Value)),
                string.Format(
                    _localizationService.T("Tools.Round.Result.TopBloons"),
                    topBloons.Length == 0
                        ? _localizationService.T("Tools.Round.Result.NoBloons")
                        : string.Join(", ", topBloons))
            ]);
    }

    private string GetBloonDisplayName(RoundBloonType type, bool isCamo, bool isRegrow, bool isFortified)
    {
        var baseName = _localizationService.T($"Tools.Round.BloonType.{type}");

        if (_localizationService.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            var zhPrefixes = new List<string>(3);
            if (isCamo)
            {
                zhPrefixes.Add(_localizationService.T("Tools.Round.BloonModifier.Camo"));
            }

            if (isFortified)
            {
                zhPrefixes.Add(_localizationService.T("Tools.Round.BloonModifier.Fortified"));
            }

            if (isRegrow)
            {
                zhPrefixes.Add(_localizationService.T("Tools.Round.BloonModifier.Regrow"));
            }

            return string.Concat(zhPrefixes) + baseName;
        }

        var enPrefixes = new List<string>(3);
        if (isFortified)
        {
            enPrefixes.Add(_localizationService.T("Tools.Round.BloonModifier.Fortified"));
        }

        if (isCamo)
        {
            enPrefixes.Add(_localizationService.T("Tools.Round.BloonModifier.Camo"));
        }

        if (isRegrow)
        {
            enPrefixes.Add(_localizationService.T("Tools.Round.BloonModifier.Regrow"));
        }

        enPrefixes.Add(baseName);
        return string.Join(" ", enPrefixes);
    }

    private static string FormatDecimal(double value)
    {
        return value.ToString("N1", CultureInfo.CurrentCulture);
    }

    private static string FormatInteger(double value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static string FormatInteger(long value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }

    private string FormatDuration(double seconds)
    {
        var normalizedSeconds = Math.Max(0d, seconds);
        var duration = TimeSpan.FromSeconds(normalizedSeconds);
        if (normalizedSeconds >= 60d)
        {
            return string.Format(
                _localizationService.T("Tools.Round.Result.DurationMinutesSeconds"),
                (int)duration.TotalMinutes,
                duration.Seconds,
                duration.Milliseconds / 10);
        }

        return string.Format(
            _localizationService.T("Tools.Round.Result.DurationSeconds"),
            normalizedSeconds.ToString("N2", CultureInfo.CurrentCulture));
    }
}
