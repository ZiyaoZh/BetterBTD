using System.Text.Json.Serialization;

namespace BetterBTD.Models.Rounds;

public enum RoundBloonType
{
    Red,
    Blue,
    Green,
    Yellow,
    Pink,
    Black,
    White,
    Purple,
    Zebra,
    Lead,
    Rainbow,
    Ceramic,
    Moab,
    Bfb,
    Zomg,
    Ddt,
    Bad
}

public sealed class RoundCatalog
{
    public const string FormatId = "betterbtd.round-catalog";

    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("rounds")]
    public IReadOnlyList<RoundDefinition> Rounds { get; init; } = [];

    [JsonIgnore]
    public int MaxRound => Rounds.Count == 0 ? 0 : Rounds.Max(x => x.Round);
}

public sealed class RoundDefinition
{
    [JsonPropertyName("round")]
    public int Round { get; init; }

    [JsonPropertyName("cashReward")]
    public double CashReward { get; init; }

    [JsonPropertyName("experience")]
    public int Experience { get; init; }

    [JsonPropertyName("rbe")]
    public int Rbe { get; init; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("bloons")]
    public IReadOnlyList<RoundBloonEntry> Bloons { get; init; } = [];
}

public sealed class RoundBloonEntry
{
    [JsonPropertyName("type")]
    public RoundBloonType Type { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("groupCount")]
    public int GroupCount { get; init; } = 1;

    [JsonPropertyName("startSeconds")]
    public double StartSeconds { get; init; }

    [JsonPropertyName("endSeconds")]
    public double EndSeconds { get; init; }

    [JsonPropertyName("isCamo")]
    public bool IsCamo { get; init; }

    [JsonPropertyName("isRegrow")]
    public bool IsRegrow { get; init; }

    [JsonPropertyName("isFortified")]
    public bool IsFortified { get; init; }

    [JsonIgnore]
    public long TotalCount => (long)Count * Math.Max(1, GroupCount);
}

public sealed class RoundRangeSummary
{
    public required int StartRound { get; init; }

    public required int EndRound { get; init; }

    public required int RoundCount { get; init; }

    public required double TotalCashReward { get; init; }

    public required long TotalExperience { get; init; }

    public required long TotalRbe { get; init; }

    public required double TotalDurationSeconds { get; init; }

    public required RoundMetricPeak PeakCashRewardRound { get; init; }

    public required RoundMetricPeak PeakRbeRound { get; init; }

    public required RoundMetricPeak PeakDurationRound { get; init; }

    public required IReadOnlyList<RoundBloonAggregate> BloonTotals { get; init; }
}

public sealed class RoundMetricPeak
{
    public required int Round { get; init; }

    public required double Value { get; init; }
}

public sealed class RoundBloonAggregate
{
    public required RoundBloonType Type { get; init; }

    public required bool IsCamo { get; init; }

    public required bool IsRegrow { get; init; }

    public required bool IsFortified { get; init; }

    public required long TotalCount { get; init; }
}
