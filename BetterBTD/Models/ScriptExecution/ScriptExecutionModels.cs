using System.Windows;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Models.ScriptExecution;

public enum ScriptExecutionStatus
{
    Completed,
    Cancelled,
    Failed
}

public enum ScriptExecutionRunState
{
    Idle,
    Running,
    PauseRequested,
    Paused,
    Completed,
    Cancelled,
    Failed
}

public sealed class ScriptExecutionOptions
{
    public int? OverrideInstructionIntervalMs { get; init; }

    public bool RequireCaptureService { get; init; } = true;

    public bool RequireTargetWindow { get; init; } = true;

    public ScriptExecutionRuntimeServices? RuntimeServices { get; init; }
}

public sealed class ScriptTaskFlow
{
    public string SourceFilePath { get; init; } = string.Empty;

    public required ScriptDocument Document { get; init; }

    public required IReadOnlyList<ScriptTaskFlowStep> Steps { get; init; }

    public required IReadOnlyDictionary<string, ScriptMonkeyObjectDocument> MonkeyObjectsByBindingId { get; init; }
}

public sealed class ScriptTaskFlowStep
{
    public required int Index { get; init; }

    public required ScriptCommandType CommandType { get; init; }

    public required ScriptInstructionDocument Instruction { get; init; }
}

public sealed class ScriptMonkeyRuntimeState
{
    public required string BindingId { get; init; }

    public string ObjectId { get; set; } = string.Empty;

    public string SelectionCode { get; set; } = string.Empty;

    public int PlacementOrder { get; set; }

    public Point? LastKnownCoordinate { get; set; }
}

public sealed class ScriptExecutionState
{
    private readonly Dictionary<string, ScriptMonkeyRuntimeState> _monkeyStates = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, ScriptMonkeyRuntimeState> MonkeyStates => _monkeyStates;

    public void SeedMonkeyStates(IEnumerable<ScriptMonkeyObjectDocument> monkeyObjects)
    {
        ArgumentNullException.ThrowIfNull(monkeyObjects);

        _monkeyStates.Clear();
        foreach (var monkeyObject in monkeyObjects)
        {
            if (string.IsNullOrWhiteSpace(monkeyObject.BindingId))
            {
                continue;
            }

            _monkeyStates[monkeyObject.BindingId] = new ScriptMonkeyRuntimeState
            {
                BindingId = monkeyObject.BindingId,
                ObjectId = monkeyObject.ObjectId,
                SelectionCode = monkeyObject.SelectionCode,
                PlacementOrder = monkeyObject.PlacementOrder
            };
        }
    }

    public bool TryGetMonkeyState(string bindingId, out ScriptMonkeyRuntimeState monkeyState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        return _monkeyStates.TryGetValue(bindingId, out monkeyState!);
    }

    public ScriptMonkeyRuntimeState UpsertMonkeyState(
        string bindingId,
        string objectId,
        string selectionCode,
        int placementOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);

        if (_monkeyStates.TryGetValue(bindingId, out var monkeyState))
        {
            if (!string.IsNullOrWhiteSpace(objectId))
            {
                monkeyState.ObjectId = objectId;
            }

            if (!string.IsNullOrWhiteSpace(selectionCode))
            {
                monkeyState.SelectionCode = selectionCode;
            }

            if (placementOrder > 0)
            {
                monkeyState.PlacementOrder = placementOrder;
            }

            return monkeyState;
        }

        monkeyState = new ScriptMonkeyRuntimeState
        {
            BindingId = bindingId,
            ObjectId = objectId ?? string.Empty,
            SelectionCode = selectionCode ?? string.Empty,
            PlacementOrder = placementOrder
        };

        _monkeyStates[bindingId] = monkeyState;
        return monkeyState;
    }
}

public sealed class ScriptInstructionExecutionContext
{
    public required ScriptTaskFlow TaskFlow { get; init; }

    public required ScriptTaskFlowStep Step { get; init; }

    public required ScriptExecutionState State { get; init; }

    public required ScriptExecutionOptions Options { get; init; }

    public required ScriptExecutionRuntimeServices RuntimeServices { get; init; }

    public required ScriptExecutionSession ExecutionSession { get; init; }
}

public sealed class GameStageUpgradePanelState
{
    public static GameStageUpgradePanelState Empty { get; } = new();

    public bool? IsVisible { get; init; }

    public int? TopPathLevel { get; init; }

    public int? MiddlePathLevel { get; init; }

    public int? BottomPathLevel { get; init; }
}

public sealed class GameStageStateSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool? IsInLevel { get; init; }

    public int? Gold { get; init; }

    public int? Round { get; init; }

    public GameStageUpgradePanelState RightUpgradePanel { get; init; } = GameStageUpgradePanelState.Empty;

    public GameStageUpgradePanelState LeftUpgradePanel { get; init; } = GameStageUpgradePanelState.Empty;

    public bool? IsPlacingMonkey { get; init; }

    public bool? CanPlaceHero { get; init; }

    public string StageTarget { get; init; } = string.Empty;
}

public sealed class ScriptExecutionResult
{
    public required ScriptExecutionStatus Status { get; init; }

    public required int ExecutedStepCount { get; init; }

    public required int LastCompletedStepIndex { get; init; }

    public Exception? Exception { get; init; }

    public ScriptExecutionProgressSnapshot? FinalProgress { get; init; }

    public ScriptExecutionFailureDetails? Failure { get; init; }
}

public sealed class ScriptExecutionProgressSnapshot
{
    public string SourceFilePath { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ScriptExecutionRunState RunState { get; set; } = ScriptExecutionRunState.Idle;

    public int CurrentStepIndex { get; set; } = -1;

    public string CurrentCommandType { get; set; } = string.Empty;

    public string CurrentCheckpoint { get; set; } = string.Empty;

    public int CurrentAttempt { get; set; }

    public int CompletedStepCount { get; set; }

    public int LastCompletedStepIndex { get; set; } = -1;

    public bool IsPauseRequested { get; set; }

    public string Message { get; set; } = string.Empty;

    public ScriptExecutionProgressSnapshot Clone()
    {
        return new ScriptExecutionProgressSnapshot
        {
            SourceFilePath = SourceFilePath,
            StartedAt = StartedAt,
            LastUpdatedAt = LastUpdatedAt,
            RunState = RunState,
            CurrentStepIndex = CurrentStepIndex,
            CurrentCommandType = CurrentCommandType,
            CurrentCheckpoint = CurrentCheckpoint,
            CurrentAttempt = CurrentAttempt,
            CompletedStepCount = CompletedStepCount,
            LastCompletedStepIndex = LastCompletedStepIndex,
            IsPauseRequested = IsPauseRequested,
            Message = Message
        };
    }
}

public sealed class ScriptExecutionFailureDetails
{
    public int StepIndex { get; init; } = -1;

    public string CommandType { get; init; } = string.Empty;

    public string Checkpoint { get; init; } = string.Empty;

    public int Attempt { get; init; }

    public string Message { get; init; } = string.Empty;
}
