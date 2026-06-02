using BetterBTD.Models.Rounds;
using BetterBTD.Services.Shared;

namespace BetterBTD.Tests.Services;

public sealed class RoundCatalogServiceTests
{
    [Fact]
    public void LoadCatalog_NormalizedCatalog_PreservesStructuredSpecialCases()
    {
        var service = new RoundCatalogService(GetCatalogPath());

        var round86 = service.GetRound(86);
        var round122 = service.GetRound(122);

        var fortifiedBfb = Assert.Single(round86.Bloons);
        Assert.Equal(RoundBloonType.Bfb, fortifiedBfb.Type);
        Assert.True(fortifiedBfb.IsFortified);
        Assert.Equal(-0.15d, fortifiedBfb.StartSeconds, 3);
        Assert.Equal(20.85d, fortifiedBfb.EndSeconds, 3);

        var fortifiedLead = Assert.Single(round122.Bloons, x => x.Type == RoundBloonType.Lead);
        Assert.True(fortifiedLead.IsFortified);
        Assert.Equal(75, fortifiedLead.Count);
        Assert.Equal(3, fortifiedLead.GroupCount);
        Assert.Equal(225L, fortifiedLead.TotalCount);
    }

    [Fact]
    public void CalculateRange_AggregatesRoundTotalsAndBloons()
    {
        var service = new RoundCatalogService(GetCatalogPath());

        var summary = service.CalculateRange(1, 3);

        Assert.Equal(1, summary.StartRound);
        Assert.Equal(3, summary.EndRound);
        Assert.Equal(3, summary.RoundCount);
        Assert.Equal(396d, summary.TotalCashReward, 3);
        Assert.Equal(180L, summary.TotalExperience);
        Assert.Equal(90L, summary.TotalRbe);
        Assert.Equal(53.2225d, summary.TotalDurationSeconds, 4);
        Assert.Equal(3, summary.PeakCashRewardRound.Round);
        Assert.Equal(138d, summary.PeakCashRewardRound.Value, 3);

        Assert.Collection(
            summary.BloonTotals.Take(2),
            red =>
            {
                Assert.Equal(RoundBloonType.Red, red.Type);
                Assert.Equal(80L, red.TotalCount);
            },
            blue =>
            {
                Assert.Equal(RoundBloonType.Blue, blue.Type);
                Assert.Equal(5L, blue.TotalCount);
            });
    }

    private static string GetCatalogPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "Rounds", "default.json");
    }
}
