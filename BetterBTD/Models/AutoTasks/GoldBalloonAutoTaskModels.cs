using BetterBTD.Models.GameElements;

namespace BetterBTD.Models.AutoTasks;

public enum GoldBalloonAutoTaskScriptRunState
{
    NotStarted,
    Running,
    FinishedCurrentStage
}

public sealed class GoldBalloonAutoTaskScriptContext
{
    public required GameMapType Map { get; init; }

    public required StageDifficulty Difficulty { get; init; }

    public required StageMode Mode { get; init; }

    public required HeroType Hero { get; init; }

    public required string FilePath { get; init; }
}

public static class GoldBalloonAutoTaskStateKeys
{
    public const string ResolvedScriptContext = "GoldBalloon.ResolvedScriptContext";
    public const string RecognizedMap = "GoldBalloon.RecognizedMap";
    public const string HeroSelected = "GoldBalloon.HeroSelected";
    public const string MapSearchAttempts = "GoldBalloon.MapSearchAttempts";
    public const string MapSearchPixelBaseline = "GoldBalloon.MapSearchPixelBaseline";
    public const string MapSearchPixelChangedSince = "GoldBalloon.MapSearchPixelChangedSince";
    public const string ScriptRunState = "GoldBalloon.ScriptRunState";
}
