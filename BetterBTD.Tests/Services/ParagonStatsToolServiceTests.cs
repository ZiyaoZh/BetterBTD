using BetterBTD.Models.Tools;
using BetterBTD.Services.Tools;

namespace BetterBTD.Tests.Services;

public sealed class ParagonStatsToolServiceTests
{
    [Fact]
    public void Calculate_Degree100Defaults_MatchesExpectedFinalStats()
    {
        var service = ParagonStatsToolService.Instance;

        var result = service.Calculate(new ParagonStatsToolRequest
        {
            Degree = 100,
            AttackIntervalSeconds = 0.5d,
            Pierce = 200d,
            BaseDamage = 15d,
            MoabDamageBonus = 0d,
            BossDamageBonus = 0d,
            OtherDamageBonus1 = 0d,
            OtherDamageBonus2 = 0d,
            OtherDamageBonus3 = 0d
        });

        Assert.Equal(100, result.Degree);
        Assert.Equal(0.2934d, result.FinalAttackInterval, 4);
        Assert.Equal(410d, result.FinalPierce, 3);
        Assert.Equal(40d, result.FinalBaseDamage, 3);
        Assert.Equal(90d, result.BossTotalDamage, 3);
        Assert.Equal(180d, result.EliteBossTotalDamage, 3);
    }

    [Fact]
    public void Calculate_MixedBonuses_AppliesAllParagonModifiers()
    {
        var service = ParagonStatsToolService.Instance;

        var result = service.Calculate(new ParagonStatsToolRequest
        {
            Degree = 20,
            AttackIntervalSeconds = 1d,
            Pierce = 100d,
            BaseDamage = 10d,
            MoabDamageBonus = 5d,
            BossDamageBonus = 3d,
            OtherDamageBonus1 = 2d,
            OtherDamageBonus2 = 1d,
            OtherDamageBonus3 = 4d
        });

        Assert.Equal(0.7645d, result.FinalAttackInterval, 4);
        Assert.Equal(120.9d, result.FinalPierce, 3);
        Assert.Equal(12.9d, result.FinalBaseDamage, 3);
        Assert.Equal(5.95d, result.FinalMoabDamageBonus, 3);
        Assert.Equal(3.57d, result.FinalBossDamageBonus, 3);
        Assert.Equal(2.38d, result.FinalOtherDamageBonus1, 3);
        Assert.Equal(1.19d, result.FinalOtherDamageBonus2, 3);
        Assert.Equal(4.76d, result.FinalOtherDamageBonus3, 3);
        Assert.Equal(38.4375d, result.BossTotalDamage, 4);
        Assert.Equal(76.875d, result.EliteBossTotalDamage, 4);
    }
}
