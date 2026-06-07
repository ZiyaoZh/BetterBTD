using Newtonsoft.Json.Linq;

namespace BetterBTD.Models.Tools;

public sealed class Btd6SaveDocument
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required long FileSizeBytes { get; init; }

    public required int PlatformId { get; init; }

    public required string PlatformName { get; init; }

    public required int? SavedBySkuId { get; init; }

    public required string SavedBySkuName { get; init; }

    public required string SavedByGameVersion { get; init; }

    public required string Rank { get; init; }

    public required string Xp { get; init; }

    public required string MonkeyMoney { get; init; }

    public required string Trophies { get; init; }

    public required string OwnerId { get; init; }

    public required string TimeStamp { get; init; }

    public required int JsonSizeBytes { get; init; }

    public required string FormattedJson { get; init; }

    public required JToken Root { get; init; }
}

public sealed class Btd6SaveSummaryItem
{
    public required string Label { get; init; }

    public required string Value { get; init; }
}
