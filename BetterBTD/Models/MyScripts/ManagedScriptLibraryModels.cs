using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;

namespace BetterBTD.Models.MyScripts;

public sealed class ManagedScriptLibraryDocument
{
    public int Version { get; set; } = 1;

    public List<ManagedScriptAssetRecord> Scripts { get; set; } = [];

    public List<ManagedScriptSlotBindingRecord> Bindings { get; set; } = [];
}

public sealed class ManagedScriptAssetRecord
{
    public string ScriptId { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

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

public sealed class ManagedScriptAssetEntry
{
    public required string ScriptId { get; init; }

    public required string DisplayName { get; init; }

    public required string SourceFileName { get; init; }

    public required string StoredFilePath { get; init; }

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

    public static string CreateCollectionSlotId(int modeIndex, int scriptIndex)
    {
        return $"collection/mode-{modeIndex:00}/slot-{scriptIndex:00}";
    }

    public static string CreateRaceCurrentSlotId()
    {
        return "race/current";
    }
}
