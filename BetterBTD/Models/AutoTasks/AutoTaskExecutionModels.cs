using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Models.AutoTasks;

public enum AutoTaskKind
{
    Custom,
    Collection,
    BlackBorder,
    Race
}

public enum AutoTaskRunState
{
    Idle,
    Running,
    PauseRequested,
    Paused,
    Completed,
    Cancelled,
    Failed
}

public enum AutoTaskPhase
{
    PreparingStage,
    NavigatingToStage,
    WaitingForLevelLoad,
    ExecutingScript,
    SettlingResult,
    AdvancingObjective,
    Completed,
    Failed
}

public enum AutoTaskExecutionStatus
{
    Completed,
    Cancelled,
    Failed
}

public enum AutoTaskDecisionKind
{
    Wait,
    Navigate,
    StartScriptExecution,
    Complete,
    Fail
}

public enum GameUiStateId
{
    Unknown,
    MainMenu,
    StageChallengeWithHint,
    MapCategorySelect,
    MapSearch,
    MapSearchResults,
    MapGrid,
    DifficultySelect,
    EasyModeSelect,
    MediumModeSelect,
    HardModeSelect,
    ModeSelect,
    HeroSelect,
    EventMenu,
    EventDetails,
    CollectionEvent,
    CollectionEventClaimable,
    StageSettings,
    Loading,
    InLevel,
    StageSettlement,
    Victory,
    Defeat,
    Reward,
    ConfirmDialog,
    ChestOpened,
    TwoChests,
    ThreeChests,
    LevelUp,
    StageHint,
    InstaMonkeyReward,
    RaceResult,
    BossResult,
    Returnable
}

public enum GameUiActionKind
{
    None,
    Wait,
    OpenMapSelection,
    SelectMapCategory,
    SelectMap,
    SelectDifficulty,
    SelectMode,
    ConfirmDialog,
    CollectReward,
    ReturnToHome,
    RetryStage
}

public sealed class StageEntryTarget
{
    public required GameMapType Map { get; init; }

    public required StageDifficulty Difficulty { get; init; }

    public required StageMode Mode { get; init; }
}

public sealed class AutoTaskRequest
{
    public required AutoTaskKind Kind { get; init; }

    public required StageEntryTarget StageTarget { get; init; }

    public int OperationIntervalMs { get; init; } = 200;

    public string PreferredScriptPath { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;
}

public sealed class GameUiSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public GameUiStateId State { get; init; } = GameUiStateId.Unknown;

    public double Confidence { get; init; }

    public GameStageStateSnapshot? StageState { get; init; }

    public IReadOnlyDictionary<string, object?> Facts { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public string Summary { get; init; } = string.Empty;
}

public sealed class GameUiNavigationStep
{
    public required GameUiActionKind ActionKind { get; init; }

    public string Description { get; init; } = string.Empty;

    public int PostActionDelayMs { get; init; } = 400;

    public IReadOnlyList<GameUiStateId> ExpectedNextStates { get; init; } = [];
}

public sealed class GameUiActionExecutionResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public int RecommendedDelayMs { get; init; } = 400;
}

public sealed class AutoTaskScriptQuery
{
    public AutoTaskKind Kind { get; init; }

    public StageEntryTarget? StageTarget { get; init; }

    public string PreferredFilePath { get; init; } = string.Empty;

    public string SlotId { get; init; } = string.Empty;

    public IReadOnlyList<string> RequiredTags { get; init; } = [];

    public string Description { get; init; } = string.Empty;
}

public sealed class AutoTaskScriptResolution
{
    public bool IsResolved { get; init; }

    public string FilePath { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public AutoTaskScriptQuery? Query { get; init; }
}

public sealed class AutoTaskDecision
{
    public required AutoTaskDecisionKind Kind { get; init; }

    public string Description { get; init; } = string.Empty;

    public AutoTaskPhase? NextPhase { get; init; }

    public int DelayMs { get; init; } = 400;

    public AutoTaskScriptQuery? ScriptQuery { get; init; }

    public static AutoTaskDecision Wait(
        string description,
        int delayMs,
        AutoTaskPhase? nextPhase = null)
    {
        return new AutoTaskDecision
        {
            Kind = AutoTaskDecisionKind.Wait,
            Description = description,
            DelayMs = delayMs,
            NextPhase = nextPhase
        };
    }

    public static AutoTaskDecision Navigate(
        string description,
        AutoTaskPhase nextPhase = AutoTaskPhase.NavigatingToStage)
    {
        return new AutoTaskDecision
        {
            Kind = AutoTaskDecisionKind.Navigate,
            Description = description,
            NextPhase = nextPhase
        };
    }

    public static AutoTaskDecision StartScript(
        AutoTaskScriptQuery scriptQuery,
        string description,
        AutoTaskPhase nextPhase = AutoTaskPhase.ExecutingScript)
    {
        ArgumentNullException.ThrowIfNull(scriptQuery);

        return new AutoTaskDecision
        {
            Kind = AutoTaskDecisionKind.StartScriptExecution,
            Description = description,
            ScriptQuery = scriptQuery,
            NextPhase = nextPhase
        };
    }

    public static AutoTaskDecision Complete(
        string description,
        AutoTaskPhase nextPhase = AutoTaskPhase.Completed)
    {
        return new AutoTaskDecision
        {
            Kind = AutoTaskDecisionKind.Complete,
            Description = description,
            NextPhase = nextPhase
        };
    }

    public static AutoTaskDecision Fail(
        string description,
        AutoTaskPhase nextPhase = AutoTaskPhase.Failed)
    {
        return new AutoTaskDecision
        {
            Kind = AutoTaskDecisionKind.Fail,
            Description = description,
            NextPhase = nextPhase
        };
    }
}

public sealed class AutoTaskExecutionOptions
{
    public int MaxLoopIterations { get; init; } = 2000;

    public int MaxConsecutiveNavigationFailures { get; init; } = 5;

    public int DefaultDecisionDelayMs { get; init; } = 400;

    public AutoTaskRuntimeServices? RuntimeServices { get; init; }
}

public sealed class AutoTaskProgressSnapshot
{
    public string TaskKey { get; set; } = string.Empty;

    public AutoTaskKind TaskKind { get; set; }

    public AutoTaskRunState RunState { get; set; } = AutoTaskRunState.Idle;

    public AutoTaskPhase Phase { get; set; } = AutoTaskPhase.PreparingStage;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int LoopIteration { get; set; }

    public GameUiStateId CurrentUiState { get; set; } = GameUiStateId.Unknown;

    public string CurrentCheckpoint { get; set; } = string.Empty;

    public int CurrentAttempt { get; set; }

    public bool IsPauseRequested { get; set; }

    public string ActiveScriptPath { get; set; } = string.Empty;

    public int ConsecutiveNavigationFailures { get; set; }

    public string Message { get; set; } = string.Empty;

    public AutoTaskProgressSnapshot Clone()
    {
        return new AutoTaskProgressSnapshot
        {
            TaskKey = TaskKey,
            TaskKind = TaskKind,
            RunState = RunState,
            Phase = Phase,
            StartedAt = StartedAt,
            LastUpdatedAt = LastUpdatedAt,
            LoopIteration = LoopIteration,
            CurrentUiState = CurrentUiState,
            CurrentCheckpoint = CurrentCheckpoint,
            CurrentAttempt = CurrentAttempt,
            IsPauseRequested = IsPauseRequested,
            ActiveScriptPath = ActiveScriptPath,
            ConsecutiveNavigationFailures = ConsecutiveNavigationFailures,
            Message = Message
        };
    }
}

public sealed class AutoTaskFailureDetails
{
    public AutoTaskPhase Phase { get; init; } = AutoTaskPhase.Failed;

    public GameUiStateId UiState { get; init; } = GameUiStateId.Unknown;

    public string Checkpoint { get; init; } = string.Empty;

    public int Attempt { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class AutoTaskExecutionResult
{
    public required AutoTaskExecutionStatus Status { get; init; }

    public required AutoTaskProgressSnapshot FinalProgress { get; init; }

    public Exception? Exception { get; init; }

    public AutoTaskFailureDetails? Failure { get; init; }
}

public sealed class AutoTaskRuntimeState
{
    public AutoTaskRuntimeState(AutoTaskRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Phase = AutoTaskPhase.PreparingStage;
    }

    public AutoTaskRequest Request { get; }

    public AutoTaskPhase Phase { get; set; }

    public int LoopIteration { get; private set; }

    public int ConsecutiveNavigationFailures { get; private set; }

    public GameUiSnapshot? LastUiSnapshot { get; private set; }

    public AutoTaskScriptResolution? ActiveScript { get; private set; }

    public ScriptExecutionResult? LastScriptExecutionResult { get; private set; }

    public bool HasPendingScriptOutcome { get; private set; }

    public void IncrementLoopIteration()
    {
        LoopIteration++;
    }

    public void RecordUiSnapshot(GameUiSnapshot snapshot)
    {
        LastUiSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public void RecordNavigationFailure()
    {
        ConsecutiveNavigationFailures++;
    }

    public void ResetNavigationFailures()
    {
        ConsecutiveNavigationFailures = 0;
    }

    public void RecordScriptResolution(AutoTaskScriptResolution resolution)
    {
        ActiveScript = resolution ?? throw new ArgumentNullException(nameof(resolution));
    }

    public void RecordScriptExecutionResult(ScriptExecutionResult result)
    {
        LastScriptExecutionResult = result ?? throw new ArgumentNullException(nameof(result));
        HasPendingScriptOutcome = true;
    }

    public void ClearPendingScriptOutcome()
    {
        HasPendingScriptOutcome = false;
    }

    public void ClearActiveScript()
    {
        ActiveScript = null;
    }
}

public static class AutoTaskKindExtensions
{
    public static string ToKey(this AutoTaskKind kind)
    {
        return kind switch
        {
            AutoTaskKind.Custom => "custom",
            AutoTaskKind.Collection => "collection",
            AutoTaskKind.BlackBorder => "blackborder",
            AutoTaskKind.Race => "race",
            _ => kind.ToString().ToLowerInvariant()
        };
    }
}
