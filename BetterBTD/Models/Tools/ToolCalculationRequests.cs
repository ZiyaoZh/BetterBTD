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

    public required double TotalPops { get; init; }

    public required int UpgradeCount { get; init; }

    public required double ExtraCash { get; init; }
}
