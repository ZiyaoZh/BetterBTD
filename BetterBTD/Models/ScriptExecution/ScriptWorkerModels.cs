using System.Text.Json.Serialization;

namespace BetterBTD.Models.ScriptExecution;

public enum ScriptWorkerState
{
    NotStarted,
    Starting,
    Running,
    Pausing,
    Paused,
    CancellationRequested,
    Completed,
    Cancelled,
    Failed
}

public enum ScriptWorkerCommandKind
{
    Start,
    Pause,
    Resume,
    Cancel
}

public enum ScriptWorkerEventKind
{
    Started,
    ProgressChanged,
    PauseAcknowledged,
    ResumeAcknowledged,
    Completed,
    Cancelled,
    Failed
}

public sealed record ScriptWorkerStartRequest
{
    public ScriptWorkerStartRequest(string filePath, ScriptExecutionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(options);

        FilePath = filePath;
        Options = options;
    }

    public string FilePath { get; }

    public ScriptExecutionOptions Options { get; }
}

public sealed record ScriptWorkerCommand
{
    public ScriptWorkerCommand(
        ScriptWorkerCommandKind kind,
        Guid runId,
        long requestSequence,
        string reason,
        CancellationToken cancellationToken,
        bool waitForAcknowledgement,
        ScriptWorkerStartRequest? startRequest = null)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A worker command must identify its script run.", nameof(runId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestSequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if ((kind == ScriptWorkerCommandKind.Start) != (startRequest is not null))
        {
            throw new ArgumentException("Only a Start command must include a start request.", nameof(startRequest));
        }

        Kind = kind;
        RunId = runId;
        RequestSequence = requestSequence;
        Reason = reason;
        CancellationToken = cancellationToken;
        WaitForAcknowledgement = waitForAcknowledgement;
        StartRequest = startRequest;
    }

    public ScriptWorkerCommandKind Kind { get; }

    public Guid RunId { get; }

    public long RequestSequence { get; }

    public string Reason { get; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; }

    public bool WaitForAcknowledgement { get; }

    public ScriptWorkerStartRequest? StartRequest { get; }
}

public sealed record ScriptObservationDiagnostics
{
    public ScriptObservationDiagnostics(
        DateTimeOffset? lastCapturedAt,
        string checkpoint,
        int attempt,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        LastCapturedAt = lastCapturedAt;
        Checkpoint = checkpoint;
        Attempt = attempt;
        Message = message ?? string.Empty;
    }

    public DateTimeOffset? LastCapturedAt { get; }

    public string Checkpoint { get; }

    public int Attempt { get; }

    public string Message { get; }
}

public sealed record ScriptWorkerEvent
{
    public ScriptWorkerEvent(
        ScriptWorkerEventKind kind,
        Guid runId,
        ScriptWorkerState state,
        DateTimeOffset occurredAt,
        long? requestSequence = null,
        int currentStepIndex = -1,
        int lastCompletedStepIndex = -1,
        Exception? error = null,
        ScriptObservationDiagnostics? diagnostics = null)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A worker event must identify its script run.", nameof(runId));
        }

        if (occurredAt == default)
        {
            throw new ArgumentException("A worker event must have a timestamp.", nameof(occurredAt));
        }

        if (requestSequence is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestSequence));
        }

        if (currentStepIndex < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentStepIndex));
        }

        if (lastCompletedStepIndex < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(lastCompletedStepIndex));
        }


        if (lastCompletedStepIndex > currentStepIndex)
        {
            throw new ArgumentException(
                "The last completed step cannot be after the current step.",
                nameof(lastCompletedStepIndex));
        }

        var isAcknowledgement = kind is
            ScriptWorkerEventKind.PauseAcknowledged or ScriptWorkerEventKind.ResumeAcknowledged;
        if (isAcknowledgement && requestSequence is null)
        {
            throw new ArgumentException("An acknowledgement must identify its command request.", nameof(requestSequence));
        }

        if ((kind == ScriptWorkerEventKind.Failed) != (error is not null))
        {
            throw new ArgumentException("Only a Failed event must include an error.", nameof(error));
        }


        var expectedState = kind switch
        {
            ScriptWorkerEventKind.Started => ScriptWorkerState.Running,
            ScriptWorkerEventKind.PauseAcknowledged => ScriptWorkerState.Paused,
            ScriptWorkerEventKind.ResumeAcknowledged => ScriptWorkerState.Running,
            ScriptWorkerEventKind.Completed => ScriptWorkerState.Completed,
            ScriptWorkerEventKind.Cancelled => ScriptWorkerState.Cancelled,
            ScriptWorkerEventKind.Failed => ScriptWorkerState.Failed,
            _ => state
        };
        if (state != expectedState)
        {
            throw new ArgumentException($"A {kind} event must report the {expectedState} state.", nameof(state));
        }

        Kind = kind;
        RunId = runId;
        State = state;
        OccurredAt = occurredAt;
        RequestSequence = requestSequence;
        CurrentStepIndex = currentStepIndex;
        LastCompletedStepIndex = lastCompletedStepIndex;
        Error = error;
        Diagnostics = diagnostics;
    }

    public ScriptWorkerEventKind Kind { get; }

    public Guid RunId { get; }

    public ScriptWorkerState State { get; }

    public DateTimeOffset OccurredAt { get; }

    public long? RequestSequence { get; }

    public int CurrentStepIndex { get; }

    public int LastCompletedStepIndex { get; }

    public Exception? Error { get; }

    public ScriptObservationDiagnostics? Diagnostics { get; }
}

public sealed record ScriptWorkerStateTransition
{
    public ScriptWorkerStateTransition(
        ScriptWorkerState previousState,
        ScriptWorkerState currentState,
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

    public ScriptWorkerState PreviousState { get; }

    public ScriptWorkerState CurrentState { get; }

    public string Reason { get; }

    public DateTimeOffset OccurredAt { get; }

    public long NavigationSequence { get; }
}
