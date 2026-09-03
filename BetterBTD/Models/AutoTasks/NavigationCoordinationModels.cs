namespace BetterBTD.Models.AutoTasks;

public enum StageChallengeState
{
    Preparing,
    EnteringStage,
    InStageBeforeScript,
    ScriptRunning,
    ScriptCompletedWaitingForResult,
    ResultDetected,
    HandlingPopup,
    HandlingVictory,
    HandlingDefeat,
    Completed,
    Failed,
    Cancelled
}

public sealed record NavigationObservation
{
    public NavigationObservation(long sequence, DateTimeOffset capturedAt, GameUiSnapshot snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (capturedAt == default)
        {
            throw new ArgumentException("An observation must have a capture time.", nameof(capturedAt));
        }

        if (capturedAt != snapshot.CapturedAt)
        {
            throw new ArgumentException("The observation and snapshot capture times must match.", nameof(capturedAt));
        }

        Sequence = sequence;
        CapturedAt = capturedAt;
        Snapshot = snapshot;
    }

    public long Sequence { get; }

    public DateTimeOffset CapturedAt { get; }

    public GameUiSnapshot Snapshot { get; }
}

public sealed record NavigationObservationDiagnostics
{
    public bool IsRunning { get; init; }

    public long PublishedCount { get; init; }

    public long FailureCount { get; init; }

    public int ConsecutiveFailureCount { get; init; }

    public DateTimeOffset? LastPublishedAt { get; init; }

    public string LastMessage { get; init; } = string.Empty;
}

public sealed record StageChallengeStateTransition
{
    public StageChallengeStateTransition(
        StageChallengeState previousState,
        StageChallengeState currentState,
        string reason,
        DateTimeOffset occurredAt,
        long navigationSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentOutOfRangeException.ThrowIfNegative(navigationSequence);
        if (occurredAt == default)
        {
            throw new ArgumentException("A state transition must have a timestamp.", nameof(occurredAt));
        }

        PreviousState = previousState;
        CurrentState = currentState;
        Reason = reason;
        OccurredAt = occurredAt;
        NavigationSequence = navigationSequence;
    }

    public StageChallengeState PreviousState { get; }

    public StageChallengeState CurrentState { get; }

    public string Reason { get; }

    public DateTimeOffset OccurredAt { get; }

    public long NavigationSequence { get; }
}
