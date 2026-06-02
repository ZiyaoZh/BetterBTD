using System.Globalization;
using BetterBTD.Models.Tools;

namespace BetterBTD.Services.Tools;

public sealed class ParagonStatsToolService
{
    private static readonly Lazy<ParagonStatsToolService> InstanceHolder = new(() => new ParagonStatsToolService(LocalizationService.Instance));

    private readonly LocalizationService _localizationService;

    internal ParagonStatsToolService(LocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public static ParagonStatsToolService Instance => InstanceHolder.Value;

    public ParagonStatsToolResult Calculate(ParagonStatsToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var degree = Math.Clamp(request.Degree, 1, 100);
        var interval = Math.Max(0d, request.AttackIntervalSeconds);
        var pierce = Math.Max(0d, request.Pierce);
        var baseDamage = Math.Max(0d, request.BaseDamage);
        var moabDamageBonus = Math.Max(0d, request.MoabDamageBonus);
        var bossDamageBonus = Math.Max(0d, request.BossDamageBonus);
        var otherDamageBonus1 = Math.Max(0d, request.OtherDamageBonus1);
        var otherDamageBonus2 = Math.Max(0d, request.OtherDamageBonus2);
        var otherDamageBonus3 = Math.Max(0d, request.OtherDamageBonus3);

        var attackSpeedReductionPercent = Math.Round(
            Math.Sqrt(50d * (degree - 1)),
            1,
            MidpointRounding.AwayFromZero);
        var percentBonus = (degree - 1) + (degree == 100 ? 1 : 0);
        var fixedPierceBonus = percentBonus * 0.1d;
        var fixedDamageBonus = Math.Floor((degree - 1) / 10d) + (degree == 100 ? 1d : 0d);
        var bossBonus = Math.Floor(degree / 20d) * 0.25d;

        var finalAttackInterval = interval / (1d + attackSpeedReductionPercent * 0.01d);
        var finalPierce = Math.Floor(pierce * (1d + percentBonus * 0.01d)) + fixedPierceBonus;
        var finalBaseDamage = baseDamage * (1d + percentBonus * 0.01d) + fixedDamageBonus;
        var finalMoabDamageBonus = moabDamageBonus * (1d + percentBonus * 0.01d);
        var finalBossDamageBonus = bossDamageBonus * (1d + percentBonus * 0.01d);
        var finalOtherDamageBonus1 = otherDamageBonus1 * (1d + percentBonus * 0.01d);
        var finalOtherDamageBonus2 = otherDamageBonus2 * (1d + percentBonus * 0.01d);
        var finalOtherDamageBonus3 = otherDamageBonus3 * (1d + percentBonus * 0.01d);
        var bossTotalDamage =
            (finalBaseDamage + finalMoabDamageBonus + finalBossDamageBonus +
             finalOtherDamageBonus1 + finalOtherDamageBonus2 + finalOtherDamageBonus3) *
            (1d + bossBonus);

        return new ParagonStatsToolResult
        {
            Degree = degree,
            FinalAttackInterval = finalAttackInterval,
            FinalPierce = finalPierce,
            FinalBaseDamage = finalBaseDamage,
            FinalMoabDamageBonus = finalMoabDamageBonus,
            FinalBossDamageBonus = finalBossDamageBonus,
            FinalOtherDamageBonus1 = finalOtherDamageBonus1,
            FinalOtherDamageBonus2 = finalOtherDamageBonus2,
            FinalOtherDamageBonus3 = finalOtherDamageBonus3,
            BossTotalDamage = bossTotalDamage,
            EliteBossTotalDamage = bossTotalDamage * 2d
        };
    }

    public string BuildResult(ParagonStatsToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = Calculate(request);
        return string.Join(
            Environment.NewLine,
            [
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.Degree"),
                    result.Degree),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.AttackInterval"),
                    FormatFixed(result.FinalAttackInterval, 4)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.Pierce"),
                    FormatFixed(result.FinalPierce, 1)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.BaseDamage"),
                    FormatFixed(result.FinalBaseDamage, 2)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.MoabDamage"),
                    FormatFixed(result.FinalMoabDamageBonus, 2)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.BossDamage"),
                    FormatFixed(result.FinalBossDamageBonus, 2)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.OtherDamage1"),
                    FormatFixed(result.FinalOtherDamageBonus1, 2)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.OtherDamage2"),
                    FormatFixed(result.FinalOtherDamageBonus2, 2)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.OtherDamage3"),
                    FormatFixed(result.FinalOtherDamageBonus3, 2)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.BossTotalDamage"),
                    FormatFixed(result.BossTotalDamage, 4)),
                string.Format(
                    _localizationService.T("Tools.ParagonStats.Result.EliteBossTotalDamage"),
                    FormatFixed(result.EliteBossTotalDamage, 4))
            ]);
    }

    private static string FormatFixed(double value, int decimals)
    {
        return value.ToString($"N{decimals}", CultureInfo.CurrentCulture);
    }
}

public sealed class ParagonStatsToolResult
{
    public required int Degree { get; init; }

    public required double FinalAttackInterval { get; init; }

    public required double FinalPierce { get; init; }

    public required double FinalBaseDamage { get; init; }

    public required double FinalMoabDamageBonus { get; init; }

    public required double FinalBossDamageBonus { get; init; }

    public required double FinalOtherDamageBonus1 { get; init; }

    public required double FinalOtherDamageBonus2 { get; init; }

    public required double FinalOtherDamageBonus3 { get; init; }

    public required double BossTotalDamage { get; init; }

    public required double EliteBossTotalDamage { get; init; }
}
