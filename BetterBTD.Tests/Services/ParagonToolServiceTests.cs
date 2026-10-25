using BetterBTD.Models.Tools;
using BetterBTD.Services.Tools;

namespace BetterBTD.Tests.Services;

public sealed class ParagonToolServiceTests
{
    [Fact]
    public void Calculate_TotemCapReached_ResolvesDegree100()
    {
        var service = ParagonToolService.Instance;

        var result = service.Calculate(new ParagonToolRequest
        {
            MonkeyCode = "DartMonkey",
            DifficultyCode = "Medium",
            TotalPops = 0,
            GeneratedCash = 0,
            CashSpent = 0,
            SliderCashInvestment = 0,
            TierFiveCount = 3,
            TotemCount = 100
        });

        Assert.Equal(150000, result.ActualCost);
        Assert.Equal(630000, result.SliderMaximum);
        Assert.Equal(200000d, result.TotemEnergy);
        Assert.Equal(200000d, result.TotalPower);
        Assert.Equal(100, result.Degree);
        Assert.Null(result.NextDegreePower);
    }

    [Fact]
    public void Calculate_MixedInputs_ComputesExpectedPowerAndDegree()
    {
        var service = ParagonToolService.Instance;

        var result = service.Calculate(new ParagonToolRequest
        {
            MonkeyCode = "DartMonkey",
            DifficultyCode = "Medium",
            TotalPops = 180000,
            GeneratedCash = 0,
            CashSpent = 150000,
            SliderCashInvestment = 0,
            TierFiveCount = 3,
            TotemCount = 0
        });

        Assert.Equal(1000d, result.PopEnergy);
        Assert.Equal(20000d, result.CashEnergy);
        Assert.Equal(21000d, result.TotalPower);
        Assert.Equal(31, result.Degree);
        Assert.Equal(689d, result.NextDegreePower);
    }

    [Theory]
    [InlineData(22500000, 0)]
    [InlineData(0, 5625000)]
    [InlineData(18000000, 1125000)]
    [InlineData(45000000, 11250000)]
    public void Calculate_DamageAndIncome_ShareNewPowerCap(double pops, double income)
    {
        var result = ParagonToolService.Instance.Calculate(CreateRequest(pops: pops, income: income));

        Assert.Equal(125000d, result.PopEnergy);
        Assert.Equal(81, result.Degree);
    }

    [Theory]
    [InlineData(600000, 0)]
    [InlineData(0, 630000)]
    [InlineData(300000, 315000)]
    [InlineData(1500000, 1500000)]
    public void Calculate_DirectCashAndSlider_ShareNewPowerCap(double cash, double slider)
    {
        var result = ParagonToolService.Instance.Calculate(CreateRequest(cash: cash, slider: slider));

        Assert.Equal(80000d, result.CashEnergy);
        Assert.Equal(65, result.Degree);
    }

    [Fact]
    public void Calculate_MaxCashAnd21600000Damage_Reach100WithoutExtraTowersOrTotems()
    {
        var result = ParagonToolService.Instance.Calculate(CreateRequest(pops: 21600000, slider: 630000));

        Assert.Equal(120000d, result.PopEnergy);
        Assert.Equal(80000d, result.CashEnergy);
        Assert.Equal(0d, result.TierFiveEnergy);
        Assert.Equal(0d, result.TotemEnergy);
        Assert.Equal(100, result.Degree);
        Assert.Null(result.NextDegreePower);
    }

    [Theory]
    [InlineData(0, 1, 2000)]
    [InlineData(1999, 1, 1)]
    [InlineData(2000, 2, 324)]
    [InlineData(11031, 19, 1)]
    [InlineData(11032, 20, 729)]
    [InlineData(192120, 99, 7880)]
    [InlineData(199999, 99, 1)]
    public void Calculate_DegreeBoundaries_ReportRemainingPower(double power, int degree, double remaining)
    {
        // A medium Dart paragon grants 1 power per $7.50 of non-tier-5 sacrifices.
        var result = ParagonToolService.Instance.Calculate(CreateRequest(
            cash: power > 80000 ? 600000 : power * 7.5,
            pops: power > 80000 ? (power - 80000) * 180 : 0));

        Assert.Equal(degree, result.Degree);
        Assert.Equal(remaining, result.NextDegreePower);
    }

    [Fact]
    public void BuildResult_FractionalPowerRemaining_DoesNotDisplayZero()
    {
        var request = CreateRequest(cash: 14999.25);
        var result = ParagonToolService.Instance.Calculate(request);
        var text = ParagonToolService.Instance.BuildResult(request);

        Assert.Equal(1, result.Degree);
        Assert.Equal(0.1d, result.NextDegreePower.GetValueOrDefault(), precision: 6);
        Assert.Contains(string.Format(
            BetterBTD.Services.Shell.Localization.LocalizationService.Instance.T("Tools.Paragon.Result.NextDegreePower"),
            "1"), text);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 6000)]
    [InlineData(11, 48000)]
    [InlineData(12, 50000)]
    public void Calculate_ExtraTierFives_KeepExistingContribution(int count, double power)
    {
        var result = ParagonToolService.Instance.Calculate(CreateRequest(tierFives: count));

        Assert.Equal(power, result.TierFiveEnergy);
    }

    [Theory]
    [InlineData("DartMonkey", "Easy", 127500, 535500)]
    [InlineData("DartMonkey", "Hard", 162000, 680400)]
    [InlineData("DartMonkey", "Impoppable", 180000, 756000)]
    [InlineData("BombShooter", "Medium", 650000, 2730000)]
    [InlineData("EngineerMonkey", "Medium", 600000, 2520000)]
    [InlineData("SniperMonkey", "Medium", 800000, 3360000)]
    public void Catalog_CurrentCostsAndDifficulty_UseUpdatedSliderCap(
        string monkey, string difficulty, int cost, int sliderCap)
    {
        var service = ParagonToolService.Instance;

        Assert.Equal(cost, service.GetActualCost(monkey, difficulty));
        Assert.Equal(sliderCap, service.GetSliderMaximum(monkey, difficulty));
        var options = ToolsOptionService.Instance.BuildParagonMonkeyOptions(monkey);
        Assert.Equal(monkey, options.SelectedOption?.Code);
    }

    private static ParagonToolRequest CreateRequest(
        double pops = 0, double income = 0, double cash = 0, double slider = 0, int tierFives = 3)
    {
        return new ParagonToolRequest
        {
            MonkeyCode = "DartMonkey",
            DifficultyCode = "Medium",
            TotalPops = pops,
            GeneratedCash = income,
            CashSpent = cash,
            SliderCashInvestment = slider,
            TierFiveCount = tierFives,
            TotemCount = 0
        };
    }
}
