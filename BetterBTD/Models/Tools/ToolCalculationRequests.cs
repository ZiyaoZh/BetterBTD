namespace BetterBTD.Models.Tools;

public sealed class RoundToolRequest
{
    public required int StartRound { get; init; }

    public required int EndRound { get; init; }
}

public sealed class HeroToolRequest
{
    public string? HeroDisplayName { get; init; }

    public required int PlacementRound { get; init; }

    public string TargetRound { get; init; } = string.Empty;

    public string TargetLevel { get; init; } = string.Empty;
}

public sealed class ParagonToolRequest
{
    public string? MonkeyDisplayName { get; init; }

    public string? MonkeyCode { get; init; }

    public string DifficultyCode { get; init; } = "Medium";

    public required double TotalPops { get; init; }

    public required double GeneratedCash { get; init; }

    public required double CashSpent { get; init; }

    public required double SliderCashInvestment { get; init; }

    public required int TierFiveCount { get; init; }

    public required int UpgradeCount { get; init; }

    public required int TotemCount { get; init; }
}

public sealed class ParagonStatsToolRequest
{
    public required int Degree { get; init; }

    public required double AttackIntervalSeconds { get; init; }

    public required double Pierce { get; init; }

    public required double BaseDamage { get; init; }

    public required double MoabDamageBonus { get; init; }

    public required double BossDamageBonus { get; init; }

    public required double OtherDamageBonus1 { get; init; }

    public required double OtherDamageBonus2 { get; init; }

    public required double OtherDamageBonus3 { get; init; }
}
