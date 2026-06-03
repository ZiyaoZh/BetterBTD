using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;

namespace BetterBTD.Models.MyScripts;

public sealed class ManagedScriptLibraryDocument
{
    public int Version { get; set; } = 1;

    public List<ManagedScriptAssetRecord> Scripts { get; set; } = [];

    public List<ManagedScriptSlotBindingRecord> Bindings { get; set; } = [];
}

public sealed class ManagedScriptTaskBindingDocument
{
    public int Version { get; set; } = 1;

    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> StageBindings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ManagedScriptAssetRecord
{
    public string ScriptId { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Map { get; set; } = GameMapType.MonkeyMeadow.ToString();

    public string Difficulty { get; set; } = StageDifficulty.Medium.ToString();

    public string Mode { get; set; } = StageMode.Standard.ToString();

    public string Hero { get; set; } = HeroType.Quincy.ToString();

    public List<string> Tags { get; set; } = [];

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManagedScriptSlotBindingRecord
{
    public string SlotId { get; set; } = string.Empty;

    public string ScriptId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManagedScriptLibrarySnapshot
{
    public required IReadOnlyList<ManagedScriptAssetEntry> Scripts { get; init; }

    public required IReadOnlyList<ManagedScriptSlotEntry> Slots { get; init; }
}

public sealed class ManagedScriptLibraryLoadProgress
{
    public required int TotalScriptCount { get; init; }

    public required int ProcessedScriptCount { get; init; }
}

public sealed class ManagedScriptAssetEntry
{
    public required string ScriptId { get; init; }

    public required string DisplayName { get; init; }

    public required string SourceFileName { get; init; }

    public required string StoredFilePath { get; init; }

    public required string Description { get; init; }

    public required GameMapType Map { get; init; }

    public required StageDifficulty Difficulty { get; init; }

    public required StageMode Mode { get; init; }

    public required HeroType Hero { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public required DateTimeOffset ImportedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int BindingCount { get; init; }

    public bool HasMissingFile { get; init; }

    public bool HasMetadataIssue { get; init; }
}

public sealed class ManagedScriptSlotDefinition
{
    public required string SlotId { get; init; }

    public required AutoTaskKind TaskKind { get; init; }

    public required string GroupName { get; init; }

    public required string DisplayName { get; init; }

    public StageEntryTarget? StageTarget { get; init; }

    public IReadOnlyDictionary<string, string> Qualifiers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> SuggestedTags { get; init; } = [];

    public bool IsPlaceholder { get; init; }
}

public sealed class ManagedScriptSlotEntry
{
    public required ManagedScriptSlotDefinition Definition { get; init; }

    public required string BoundScriptId { get; init; }

    public ManagedScriptAssetEntry? BoundScript { get; init; }

    public bool HasBinding => BoundScriptId.Length > 0;

    public bool IsBrokenBinding => HasBinding && BoundScript is null;
}

public sealed class ManagedScriptCollectionModeDefinition
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<string> Aliases { get; init; }
}

public enum BlackBorderMapCategory
{
    Beginner,
    Intermediate,
    Advanced,
    Expert
}

public enum BlackBorderSubscriptionExportType
{
    BeginnerMaps,
    IntermediateMaps,
    AdvancedMaps,
    ExpertMaps,
    SingleMap
}

public static class BlackBorderMapCategoryExtensions
{
    public static MapDifficultyTier ToMapDifficultyTier(this BlackBorderMapCategory category)
    {
        return category switch
        {
            BlackBorderMapCategory.Beginner => MapDifficultyTier.Beginner,
            BlackBorderMapCategory.Intermediate => MapDifficultyTier.Intermediate,
            BlackBorderMapCategory.Advanced => MapDifficultyTier.Advanced,
            BlackBorderMapCategory.Expert => MapDifficultyTier.Expert,
            _ => throw new InvalidOperationException($"Unsupported black border map category '{category}'.")
        };
    }
}

public static class BlackBorderTaskCatalog
{
    public static IReadOnlyList<BlackBorderMapCategory> Categories { get; } =
    [
        BlackBorderMapCategory.Beginner,
        BlackBorderMapCategory.Intermediate,
        BlackBorderMapCategory.Advanced,
        BlackBorderMapCategory.Expert
    ];

    public static IReadOnlyList<StageDifficulty> Difficulties { get; } =
    [
        StageDifficulty.Easy,
        StageDifficulty.Medium,
        StageDifficulty.Hard
    ];

    public static IReadOnlyList<StageMode> GetModesForDifficulty(StageDifficulty difficulty)
    {
        return difficulty switch
        {
            StageDifficulty.Easy =>
            [
                StageMode.Standard,
                StageMode.Deflation,
                StageMode.PrimaryOnly
            ],
            StageDifficulty.Medium =>
            [
                StageMode.Standard,
                StageMode.MilitaryOnly,
                StageMode.Apopalypse,
                StageMode.Reverse
            ],
            StageDifficulty.Hard =>
            [
                StageMode.Standard,
                StageMode.MagicOnly,
                StageMode.DoubleHpMoabs,
                StageMode.HalfCash,
                StageMode.AlternateBloonsRounds,
                StageMode.Impoppable,
                StageMode.CHIMPS
            ],
            _ => [StageMode.Standard]
        };
    }

    public static IEnumerable<MapDefinition> GetMapsByCategory(BlackBorderMapCategory category)
    {
        var tier = category.ToMapDifficultyTier();
        return GameElementCatalog.Maps.Where(map => map.Tier == tier);
    }
}

public static class ManagedScriptCollectionModeCatalog
{
    public static IReadOnlyList<ManagedScriptCollectionModeDefinition> Modes { get; } =
    [
        new ManagedScriptCollectionModeDefinition
        {
            Key = "simple",
            DisplayName = "Simple Collection",
            Aliases = ["simple", "simple-collection", "basic", "简单收集"]
        },
        new ManagedScriptCollectionModeDefinition
        {
            Key = "double-cash",
            DisplayName = "Double Cash Collection",
            Aliases = ["double-cash", "doublecash", "double-gold", "双金收集", "双金"]
        },
        new ManagedScriptCollectionModeDefinition
        {
            Key = "fast-track",
            DisplayName = "Fast Track Collection",
            Aliases = ["fast-track", "fasttrack", "quick-path", "快速路径收集", "快速路径"]
        },
        new ManagedScriptCollectionModeDefinition
        {
            Key = "double-cash-fast-track",
            DisplayName = "Double Cash Fast Track Collection",
            Aliases =
            [
                "double-cash-fast-track",
                "doublecashfasttrack",
                "double-gold-fast-track",
                "双金-快速路径收集",
                "双金快速路径收集",
                "双金-快速路径",
                "双金快速路径"
            ]
        }
    ];

    public static bool TryNormalizeKey(string? value, out string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = string.Empty;
            return false;
        }

        var candidate = value.Trim();
        foreach (var mode in Modes)
        {
            if (string.Equals(mode.Key, candidate, StringComparison.OrdinalIgnoreCase) ||
                mode.Aliases.Any(alias => string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                key = mode.Key;
                return true;
            }
        }

        key = string.Empty;
        return false;
    }

    public static string NormalizeKey(string value)
    {
        if (TryNormalizeKey(value, out var key))
        {
            return key;
        }

        throw new InvalidOperationException($"Unsupported collection mode key '{value}'.");
    }

    public static string GetDisplayName(string key)
    {
        var normalizedKey = NormalizeKey(key);
        return Modes.First(mode => string.Equals(mode.Key, normalizedKey, StringComparison.OrdinalIgnoreCase)).DisplayName;
    }
}

public static class ManagedScriptSlotIdFactory
{
    public static string CreateCustomDefaultSlotId()
    {
        return "custom/default";
    }

    public static string CreateBlackBorderSlotId(
        GameMapType map,
        StageDifficulty difficulty,
        StageMode mode)
    {
        return $"blackborder/{map}/{difficulty}/{mode}";
    }

    public static string CreateCollectionSlotId(string modeKey, GameMapType map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeKey);
        return $"collection/{ManagedScriptCollectionModeCatalog.NormalizeKey(modeKey)}/{map}";
    }

    public static string CreateGoldBalloonSlotId(GameMapType map)
    {
        return $"goldballoon/{map}";
    }

    public static string CreateRaceCurrentSlotId()
    {
        return "race/current";
    }
}
