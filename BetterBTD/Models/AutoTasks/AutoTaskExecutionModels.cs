using System.Text.Json.Serialization;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Models.AutoTasks;

public enum AutoTaskKind
{
    Custom,
    Collection,
    GoldBalloon,
    BlackBorder,
    LoopStage,
    Odyssey,
    Race
}

public enum LoopStageRunMode
{
    Standard,
    FreeplayUntilRound
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

public enum AutoTaskActivityKind
{
    None,
    Preparing,
    CapturingUi,
    Waiting,
    Navigating,
    ResolvingScript,
    ExecutingScript,
    HandlingResult
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
    MapGrid = 6,
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
    OdysseyStart,
    OdysseyCrew,
    OdysseyLoading,
    Loading,
    InLevel,
    OdysseyStageVictory,
    OdysseySettlement,
    OdysseyReward,
    StageSettlement,
    Victory,
    FreeplayPrompt,
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
    Returnable,
    NetworkUnavailableDialog
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

    public string VariantKey { get; init; } = string.Empty;

    public int OperationIntervalMs { get; init; } = 200;

    public string PreferredScriptPath { get; init; } = string.Empty;

    public IReadOnlyList<string> PreferredScriptPaths { get; init; } = [];

    public IReadOnlyList<string> RequiredScriptSlotIds { get; init; } = [];

    public LoopStageRunMode LoopStageRunMode { get; init; } = LoopStageRunMode.Standard;

    public int ExitAfterRound { get; init; }

    public string Key { get; init; } = string.Empty;
}

public sealed class GameUiSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public GameUiStateId State { get; init; } = GameUiStateId.Unknown;

    public double Confidence { get; init; }

    [JsonIgnore]
    public ulong? VisualFingerprint { get; init; }

    public GameStageStateSnapshot? StageState { get; init; }

    public IReadOnlyDictionary<string, object?> Facts { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public string Summary { get; init; } = string.Empty;
}

public readonly record struct GameUiPixelSample(int Red, int Green, int Blue, int Tolerance)
{
    public bool HasChangedFrom(GameUiPixelSample baseline)
    {
        var tolerance = Math.Max(Tolerance, baseline.Tolerance);
        return Math.Abs(Red - baseline.Red) > tolerance ||
               Math.Abs(Green - baseline.Green) > tolerance ||
               Math.Abs(Blue - baseline.Blue) > tolerance;
    }
}

public static class MapSearchFlowState
{
    private static readonly TimeSpan ResultConfirmationWindow = TimeSpan.FromMilliseconds(1000);
    private const int LegacyResultPixelRed = 0x40;
    private const int LegacyResultPixelGreen = 0x9F;
    private const int LegacyResultPixelBlue = 0xFF;

    public const string PixelSampleFact = "mapSearchPixelSample";
    public const string CollectionMapFact = "collectionMap";
    public const string CollectionMapMatchesFact = "collectionMapMatches";
    public const string GoldBalloonMapFact = "goldBalloonMap";
    public const string GoldBalloonMapMatchesFact = "goldBalloonMapMatches";

    public static bool TryGetPixelSample(GameUiSnapshot snapshot, out GameUiPixelSample sample)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Facts.TryGetValue(PixelSampleFact, out var rawSample) &&
            rawSample is GameUiPixelSample typedSample)
        {
            sample = typedSample;
            return true;
        }

        sample = default;
        return false;
    }

    public static void CapturePixelBaseline(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        string baselineStateKey)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineStateKey);

        if (!state.TryGetProperty<GameUiPixelSample>(baselineStateKey, out _) &&
            TryGetPixelSample(snapshot, out var sample))
        {
            state.SetProperty(baselineStateKey, sample);
        }
    }

    public static bool HasPixelChanged(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        string baselineStateKey)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineStateKey);

        return state.TryGetProperty<GameUiPixelSample>(baselineStateKey, out var baseline) &&
               TryGetPixelSample(snapshot, out var current) &&
               current.HasChangedFrom(baseline);
    }

    public static bool IsResultReady(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        string baselineStateKey,
        string changedSinceStateKey)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineStateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(changedSinceStateKey);

        if (!IsResultConfirmationInProgress(state, snapshot, baselineStateKey))
        {
            state.RemoveProperty(changedSinceStateKey);
            return false;
        }

        return HasRemainedChangedForConfirmationWindow(state, snapshot, changedSinceStateKey);
    }

    public static bool IsResultConfirmationInProgress(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        string baselineStateKey)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineStateKey);

        return state.TryGetProperty<GameUiPixelSample>(baselineStateKey, out _)
            ? HasPixelChanged(state, snapshot, baselineStateKey)
            : IsLegacyResultPixel(snapshot);
    }

    private static bool IsLegacyResultPixel(GameUiSnapshot snapshot)
    {
        if (!TryGetPixelSample(snapshot, out var sample))
        {
            return false;
        }

        return Math.Abs(sample.Red - LegacyResultPixelRed) <= sample.Tolerance &&
               Math.Abs(sample.Green - LegacyResultPixelGreen) <= sample.Tolerance &&
               Math.Abs(sample.Blue - LegacyResultPixelBlue) <= sample.Tolerance;
    }

    private static bool HasRemainedChangedForConfirmationWindow(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        string changedSinceStateKey)
    {
        if (!state.TryGetProperty<DateTimeOffset>(changedSinceStateKey, out var changedSince))
        {
            state.SetProperty(changedSinceStateKey, snapshot.CapturedAt);
            return false;
        }

        return snapshot.CapturedAt - changedSince >= ResultConfirmationWindow;
    }

    public static bool HasRecognizedMap(GameUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Facts.ContainsKey(CollectionMapFact) ||
               snapshot.Facts.ContainsKey(GoldBalloonMapFact);
    }
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

    public string VariantKey { get; init; } = string.Empty;

    public string PreferredFilePath { get; init; } = string.Empty;

    public string SlotId { get; init; } = string.Empty;

    public IReadOnlyList<string> RequiredTags { get; init; } = [];

    public int StartStepIndex { get; init; }

    public int? EndStepIndexExclusive { get; init; }

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
    /// <summary>
    /// Maximum time to wait for a script worker pause or resume acknowledgement.
    /// </summary>
    public TimeSpan WorkerAcknowledgementTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ScriptOffLevelGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ScriptRecoveryTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan ScriptRecoveryClickInterval { get; init; } = TimeSpan.FromMilliseconds(800);

    public GameUiRecoveryPoint ScriptRecoveryPoint { get; init; } = new(960, 540);

    public static List<GameUiRecoveryPoint> CreateDefaultStuckRecoveryPoints()
    {
        return
        [
            new(960, 840),
            new(960, 760),
            new(1340, 850),
            new(850, 810),
            new(780, 730),
            new(1140, 730),
            new(80, 55)
        ];
    }

    /// <summary>
    /// Maximum number of UI decision loops to execute. A null value means no limit.
    /// </summary>
    public int? MaxLoopIterations { get; init; }

    public int MaxConsecutiveNavigationFailures { get; init; } = 5;

    public int DefaultDecisionDelayMs { get; init; } = 400;

    public TimeSpan StuckUiTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int VisualFingerprintDistanceTolerance { get; init; } = 6;

    public int StuckRecoveryDelayMs { get; init; } = 800;

    public IReadOnlyList<GameUiRecoveryPoint> StuckRecoveryPoints { get; init; } =
        CreateDefaultStuckRecoveryPoints().AsReadOnly();

    public AutoTaskRuntimeServices? RuntimeServices { get; init; }
}

public readonly record struct GameUiRecoveryPoint(int X, int Y);

public sealed class AutoTaskProgressSnapshot
{
    public string TaskKey { get; set; } = string.Empty;

    public AutoTaskKind TaskKind { get; set; }

    public AutoTaskRunState RunState { get; set; } = AutoTaskRunState.Idle;

    public AutoTaskPhase Phase { get; set; } = AutoTaskPhase.PreparingStage;

    public AutoTaskActivityKind CurrentActivity { get; set; } = AutoTaskActivityKind.None;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int LoopIteration { get; set; }

    public int CompletedStageCount { get; set; }

    public GameUiStateId CurrentUiState { get; set; } = GameUiStateId.Unknown;

    public GameUiSnapshot? LastUiSnapshot { get; set; }

    public string CurrentCheckpoint { get; set; } = string.Empty;

    public int CurrentAttempt { get; set; }

    public bool IsPauseRequested { get; set; }

    public string ActiveScriptPath { get; set; } = string.Empty;

    public string ActiveScriptDisplayName { get; set; } = string.Empty;

    public IReadOnlyList<string> ActiveScriptSteps { get; set; } = Array.Empty<string>();

    public ScriptExecutionProgressSnapshot? ActiveScriptProgress { get; set; }

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
            CurrentActivity = CurrentActivity,
            StartedAt = StartedAt,
            LastUpdatedAt = LastUpdatedAt,
            LoopIteration = LoopIteration,
            CompletedStageCount = CompletedStageCount,
            CurrentUiState = CurrentUiState,
            LastUiSnapshot = LastUiSnapshot,
            CurrentCheckpoint = CurrentCheckpoint,
            CurrentAttempt = CurrentAttempt,
            IsPauseRequested = IsPauseRequested,
            ActiveScriptPath = ActiveScriptPath,
            ActiveScriptDisplayName = ActiveScriptDisplayName,
            ActiveScriptSteps = ActiveScriptSteps.Count == 0 ? Array.Empty<string>() : [.. ActiveScriptSteps],
            ActiveScriptProgress = ActiveScriptProgress?.Clone(),
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
    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
    private bool _isStageCompletionPending;

    public AutoTaskRuntimeState(AutoTaskRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Phase = AutoTaskPhase.PreparingStage;
    }

    public AutoTaskRequest Request { get; }

    public AutoTaskPhase Phase { get; set; }

    public int LoopIteration { get; private set; }

    public int CompletedStageCount { get; private set; }

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

    public void BeginStageAttempt()
    {
        _isStageCompletionPending = false;
    }

    public void RecordScriptExecutionResult(ScriptExecutionResult result)
    {
        LastScriptExecutionResult = result ?? throw new ArgumentNullException(nameof(result));
        HasPendingScriptOutcome = true;
        _isStageCompletionPending = true;
    }

    public void RecordStageFailure()
    {
        _isStageCompletionPending = false;
    }

    public bool TryRecordStageCompletion(GameUiStateId state)
    {
        if (!_isStageCompletionPending ||
            (Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound
                ? !IsSuccessfulFreeplayOutcome(this, state)
                : !IsSuccessfulStageOutcome(state)))
        {
            return false;
        }

        CompletedStageCount++;
        _isStageCompletionPending = false;
        return true;
    }

    public void ClearPendingScriptOutcome()
    {
        HasPendingScriptOutcome = false;
    }

    public void ClearActiveScript()
    {
        ActiveScript = null;
    }

    public void SetProperty(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _properties[key] = value;
    }

    public bool TryGetProperty<T>(string key, out T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_properties.TryGetValue(key, out var rawValue) && rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default!;
        return false;
    }

    public void RemoveProperty(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _properties.Remove(key);
    }

    private static bool IsSuccessfulStageOutcome(GameUiStateId state)
    {
        return state is
            GameUiStateId.Victory or
            GameUiStateId.StageSettlement or
            GameUiStateId.OdysseyStageVictory or
            GameUiStateId.OdysseySettlement or
            GameUiStateId.OdysseyReward or
            GameUiStateId.Reward or
            GameUiStateId.ChestOpened or
            GameUiStateId.TwoChests or
            GameUiStateId.ThreeChests or
            GameUiStateId.RaceResult or
            GameUiStateId.BossResult;
    }

    private static bool IsSuccessfulFreeplayOutcome(AutoTaskRuntimeState state, GameUiStateId uiState)
    {
        return uiState == GameUiStateId.MainMenu &&
               state.TryGetProperty<bool>(
                   LoopStageAutoTaskStateKeys.TargetRoundReached,
                   out var targetRoundReached) &&
               targetRoundReached &&
               state.TryGetProperty<LoopStageScriptRunState>(
                   LoopStageAutoTaskStateKeys.ScriptRunState,
                   out var runState) &&
               runState == LoopStageScriptRunState.WaitingForExit;
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
            AutoTaskKind.GoldBalloon => "goldballoon",
            AutoTaskKind.BlackBorder => "blackborder",
            AutoTaskKind.LoopStage => "loopstage",
            AutoTaskKind.Odyssey => "odyssey",
            AutoTaskKind.Race => "race",
            _ => kind.ToString().ToLowerInvariant()
        };
    }
}
