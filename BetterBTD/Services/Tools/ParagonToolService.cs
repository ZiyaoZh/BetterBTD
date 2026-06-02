using System.Globalization;
using BetterBTD.Models.Tools;

namespace BetterBTD.Services.Tools;

public sealed class ParagonToolService
{
    private static readonly Lazy<ParagonToolService> InstanceHolder = new(() => new ParagonToolService(LocalizationService.Instance));
    private static readonly double[] DegreeRequirements = BuildDegreeRequirements();

    private readonly LocalizationService _localizationService;

    internal ParagonToolService(LocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public static ParagonToolService Instance => InstanceHolder.Value;

    public int GetActualCost(string? monkeyCode, string? difficultyCode)
    {
        var baseCost = ParagonToolCatalog.GetBaseCostOrDefault(monkeyCode);
        var difficultyFactor = GetDifficultyFactor(difficultyCode);
        return (int)Math.Round(baseCost * difficultyFactor, 0, MidpointRounding.AwayFromZero);
    }

    public int GetSliderMaximum(string? monkeyCode, string? difficultyCode)
    {
        var actualCost = GetActualCost(monkeyCode, difficultyCode);
        return (int)Math.Floor(actualCost * 3.15d) + 1;
    }

    public ParagonDegreeToolResult Calculate(ParagonToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actualCost = GetActualCost(request.MonkeyCode, request.DifficultyCode);
        var sliderMaximum = GetSliderMaximum(request.MonkeyCode, request.DifficultyCode);
        var totalPops = ClampNonNegative(request.TotalPops);
        var generatedCash = ClampNonNegative(request.GeneratedCash);
        var cashSpent = ClampNonNegative(request.CashSpent);
        var sliderInvestment = Math.Clamp(request.SliderCashInvestment, 0d, sliderMaximum);
        var tierFiveCount = Math.Clamp(request.TierFiveCount, 3, 12);
        var upgradeCount = Math.Clamp(request.UpgradeCount, 0, 100);
        var totemCount = Math.Clamp(request.TotemCount, 0, 100);

        var popEnergy = Math.Min(90000d, (totalPops + generatedCash * 4d) / 180d);
        var directSpentEnergy = cashSpent / (actualCost / 20000d);
        var sliderSpentEnergy = Math.Ceiling(sliderInvestment / (actualCost * 1.05d / 20000d));
        var cashEnergy = Math.Min(60000d, directSpentEnergy + sliderSpentEnergy);
        var tierFiveEnergy = Math.Min(50000d, (tierFiveCount - 3) * 6000d);
        var upgradeEnergy = upgradeCount * 100d;
        var totemEnergy = totemCount * 2000d;
        var totalPower = Math.Min(200000d, popEnergy + cashEnergy + tierFiveEnergy + upgradeEnergy + totemEnergy);
        var degree = ResolveDegree(totalPower);
        double? nextDegreePower = degree < 100
            ? DegreeRequirements[degree] - DegreeRequirements[degree - 1]
            : null;

        return new ParagonDegreeToolResult
        {
            ActualCost = actualCost,
            SliderMaximum = sliderMaximum,
            PopEnergy = popEnergy,
            CashEnergy = cashEnergy,
            DirectSpentEnergy = directSpentEnergy,
            SliderSpentEnergy = sliderSpentEnergy,
            TierFiveEnergy = tierFiveEnergy,
            UpgradeEnergy = upgradeEnergy,
            TotemEnergy = totemEnergy,
            TotalPower = totalPower,
            Degree = degree,
            NextDegreePower = nextDegreePower
        };
    }

    public string BuildResult(ParagonToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = Calculate(request);
        return string.Join(
            Environment.NewLine,
            [
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.ActualCost"),
                    FormatInteger(result.ActualCost)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.SliderMax"),
                    FormatInteger(result.SliderMaximum)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.PopEnergy"),
                    FormatFixed(result.PopEnergy, 2)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.CashEnergy"),
                    FormatFixed(result.CashEnergy, 2),
                    FormatFixed(result.DirectSpentEnergy, 2),
                    FormatFixed(result.SliderSpentEnergy, 2)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.TierFiveEnergy"),
                    FormatFixed(result.TierFiveEnergy, 2)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.UpgradeEnergy"),
                    FormatFixed(result.UpgradeEnergy, 2)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.TotemEnergy"),
                    FormatFixed(result.TotemEnergy, 2)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.TotalPower"),
                    FormatFixed(result.TotalPower, 2)),
                string.Format(
                    _localizationService.T("Tools.Paragon.Result.Degree"),
                    result.Degree),
                result.NextDegreePower is null
                    ? _localizationService.T("Tools.Paragon.Result.NextDegreeUnavailable")
                    : string.Format(
                        _localizationService.T("Tools.Paragon.Result.NextDegreePower"),
                        FormatFixed(result.NextDegreePower.Value, 0))
            ]);
    }

    private static double ClampNonNegative(double value)
    {
        return Math.Max(0d, value);
    }

    private static int ResolveDegree(double totalPower)
    {
        for (var i = 1; i < DegreeRequirements.Length; i++)
        {
            if (totalPower < DegreeRequirements[i])
            {
                return i;
            }
        }

        return 100;
    }

    private static double[] BuildDegreeRequirements()
    {
        var requirements = new double[101];
        requirements[0] = 0d;

        for (var degree = 2; degree <= 99; degree++)
        {
            requirements[degree - 1] = Math.Round(
                (50d * Math.Pow(degree, 3) + 5025d * Math.Pow(degree, 2) + 168324d * degree + 843000d) / 600d,
                0,
                MidpointRounding.AwayFromZero);
        }

        requirements[99] = 200000d;
        requirements[100] = double.PositiveInfinity;
        return requirements;
    }

    private static double GetDifficultyFactor(string? difficultyCode)
    {
        return difficultyCode switch
        {
            "Easy" => 0.85d,
            "Hard" => 1.08d,
            "Impoppable" => 1.2d,
            _ => 1d
        };
    }

    private static string FormatFixed(double value, int decimals)
    {
        return value.ToString($"N{decimals}", CultureInfo.CurrentCulture);
    }

    private static string FormatInteger(double value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }
}

public sealed class ParagonDegreeToolResult
{
    public required int ActualCost { get; init; }

    public required int SliderMaximum { get; init; }

    public required double PopEnergy { get; init; }

    public required double CashEnergy { get; init; }

    public required double DirectSpentEnergy { get; init; }

    public required double SliderSpentEnergy { get; init; }

    public required double TierFiveEnergy { get; init; }

    public required double UpgradeEnergy { get; init; }

    public required double TotemEnergy { get; init; }

    public required double TotalPower { get; init; }

    public required int Degree { get; init; }

    public required double? NextDegreePower { get; init; }
}
