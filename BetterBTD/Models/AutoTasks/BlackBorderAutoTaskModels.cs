using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Models.AutoTasks;

public enum BlackBorderAutoTaskScriptRunState
{
    NotStarted,
    Running,
    FinishedCurrentStage
}

public sealed class BlackBorderAutoTaskStageTask
{
    public required BlackBorderMapCategory Category { get; init; }

    public required StageEntryTarget Target { get; init; }
}

public sealed class BlackBorderAutoTaskScriptContext
{
    public required BlackBorderMapCategory Category { get; init; }

    public required StageEntryTarget Target { get; init; }

    public required HeroType Hero { get; init; }

    public required string FilePath { get; init; }
}

public static class BlackBorderAutoTaskStateKeys
{
    public const string TaskQueue = "BlackBorder.TaskQueue";
    public const string CurrentTaskIndex = "BlackBorder.CurrentTaskIndex";
    public const string ResolvedScriptContext = "BlackBorder.ResolvedScriptContext";
    public const string HeroSelected = "BlackBorder.HeroSelected";
    public const string MapLocateAttempts = "BlackBorder.MapLocateAttempts";
    public const string MapSearchSignature = "BlackBorder.MapSearchSignature";
    public const string MapSearchCategorySelected = "BlackBorder.MapSearchCategorySelected";
    public const string MapSearchPageIndex = "BlackBorder.MapSearchPageIndex";
    public const string ScriptRunState = "BlackBorder.ScriptRunState";
    public const string SkipCurrentTaskRequested = "BlackBorder.SkipCurrentTaskRequested";
}
