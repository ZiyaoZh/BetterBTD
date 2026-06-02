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
            UpgradeCount = 0,
            TotemCount = 100
        });

        Assert.Equal(150000, result.ActualCost);
        Assert.Equal(472501, result.SliderMaximum);
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
            UpgradeCount = 10,
            TotemCount = 0
        });

        Assert.Equal(1000d, result.PopEnergy);
        Assert.Equal(20000d, result.CashEnergy);
        Assert.Equal(1000d, result.UpgradeEnergy);
        Assert.Equal(22000d, result.TotalPower);
        Assert.Equal(32, result.Degree);
        Assert.Equal(1089d, result.NextDegreePower);
    }
}
