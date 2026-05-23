using BetterBTD.Models.GameElements;

namespace BetterBTD.Models.AutoTasks;

public sealed class CollectionAutoTaskScriptContext
{
    public required GameMapType Map { get; init; }

    public required StageDifficulty Difficulty { get; init; }

    public required StageMode Mode { get; init; }

    public required HeroType Hero { get; init; }

    public required string FilePath { get; init; }
}

public static class CollectionAutoTaskStateKeys
{
    public const string ResolvedScriptContext = "Collection.ResolvedScriptContext";
    public const string RecognizedMap = "Collection.RecognizedMap";
    public const string HeroSelected = "Collection.HeroSelected";
    public const string MapSearchAttempts = "Collection.MapSearchAttempts";
}
