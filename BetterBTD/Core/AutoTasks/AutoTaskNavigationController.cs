using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.AutoTasks;

public sealed record AutoTaskNavigationControllerResult(
    ScriptExecutionResult ScriptResult,
    GameUiSnapshot? HandoffSnapshot);

public sealed class AutoTaskNavigationController
{
    private readonly INavigationObservationService _observations;
    private readonly IScriptTaskFlowWorker _worker;
    private readonly IGameUiStuckRecoveryExecutor? _recoveryExecutor;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _acknowledgementTimeout;
    private readonly TimeSpan _offLevelGracePeriod;
    private readonly TimeSpan _recoveryTimeout;
    private readonly TimeSpan _recoveryClickInterval;
    private readonly GameUiRecoveryPoint _recoveryPoint;
    private readonly object _syncRoot = new();
    private readonly List<StageChallengeStateTransition> _transitions = [];
    private readonly Dictionary<long, TaskCompletionSource<ScriptWorkerEvent>> _pendingAcknowledgements = [];
    private long _requestSequence;
    private Guid _runId;
    private bool _startRequested;
    private bool _pausedForRecovery;
    private DateTimeOffset? _offLevelSince;
    private DateTimeOffset? _recoveryStartedAt;
    private DateTimeOffset? _lastRecoveryClickAt;
    private ScriptWorkerEvent? _terminalEvent;

    public AutoTaskNavigationController(
        INavigationObservationService observations,
        IScriptTaskFlowWorker worker,
        IGameUiStuckRecoveryExecutor? recoveryExecutor = null,
        TimeProvider? timeProvider = null,
        TimeSpan? acknowledgementTimeout = null,
        TimeSpan? offLevelGracePeriod = null,
        TimeSpan? recoveryTimeout = null,
        TimeSpan? recoveryClickInterval = null,
        GameUiRecoveryPoint? recoveryPoint = null)
    {
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _recoveryExecutor = recoveryExecutor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(5);
        _offLevelGracePeriod = offLevelGracePeriod ?? TimeSpan.FromSeconds(5);
        _recoveryTimeout = recoveryTimeout ?? TimeSpan.FromSeconds(5);
        _recoveryClickInterval = recoveryClickInterval ?? TimeSpan.FromMilliseconds(800);
        _recoveryPoint = recoveryPoint ?? new GameUiRecoveryPoint(960, 540);

        if (_acknowledgementTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout));
        if (_offLevelGracePeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(offLevelGracePeriod));
        if (_recoveryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recoveryTimeout));
        if (_recoveryClickInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recoveryClickInterval));

        State = StageChallengeState.Navigating;
    }

    public StageChallengeState State { get; private set; }

    public ScriptWorkerState WorkerState => _worker.State;

    public IReadOnlyList<StageChallengeStateTransition> Transitions
    {
        get
        {
            lock (_syncRoot)
                return [.. _transitions];
        }
    }

    public async Task<AutoTaskNavigationControllerResult> RunAsync(
        string scriptFilePath,
        ScriptExecutionOptions scriptOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFilePath);
        ArgumentNullException.ThrowIfNull(scriptOptions);
        ResetAttempt();

        using var lifetime = new CancellationTokenSource();
        _observations.Start(lifetime.Token);
        var workerEventsTask = ObserveWorkerEventsAsync(lifetime.Token);
        try
        {
            await using var subscription = _observations.SubscribeAsync(cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (await subscription.MoveNextAsync().ConfigureAwait(false))
            {
                var result = await ProcessObservationAsync(
                        subscription.Current,
                        scriptFilePath,
                        scriptOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result is not null)
                    return result;
            }

            throw new InvalidOperationException("Navigation observation stream ended unexpectedly.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = await CancelWorkerAsync("External cancellation").ConfigureAwait(false);
            TransitionIfAllowed(StageChallengeState.Cancelled, "External cancellation", _observations.LatestObservation?.Sequence ?? 0);
            return new AutoTaskNavigationControllerResult(ResolveScriptResult(ScriptExecutionStatus.Cancelled), null);
        }
        catch (Exception ex)
        {
            _ = await CancelWorkerAsync("Controller failure").ConfigureAwait(false);
            TransitionIfAllowed(StageChallengeState.Failed, ex.GetBaseException().Message, _observations.LatestObservation?.Sequence ?? 0);
            return new AutoTaskNavigationControllerResult(ResolveScriptResult(ScriptExecutionStatus.Failed, ex), null);
        }
        finally
        {
            lifetime.Cancel();
            CancelPendingAcknowledgements();
            try
            {
                await workerEventsTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            await _observations.StopAsync().ConfigureAwait(false);
        }
    }

    private void ResetAttempt()
    {
        _runId = Guid.NewGuid();
        _requestSequence = 0;
        _startRequested = false;
        _pausedForRecovery = false;
        _offLevelSince = null;
        _recoveryStartedAt = null;
        _lastRecoveryClickAt = null;
        _terminalEvent = null;
        State = StageChallengeState.Navigating;
        lock (_syncRoot)
            _transitions.Clear();
    }

    private async Task ObserveWorkerEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var workerEvent in _worker.SubscribeAsync(cancellationToken).ConfigureAwait(false))
            {
                if (workerEvent.RunId != _runId)
                    continue;

                if (workerEvent.RequestSequence is long requestSequence &&
                    TryTakeAcknowledgement(requestSequence, out var acknowledgement))
                {
                    acknowledgement.TrySetResult(workerEvent);
                }

                if (IsTerminal(workerEvent.State))
                {
                    _terminalEvent = workerEvent;
                    CompletePendingWithTerminalEvent(workerEvent);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<AutoTaskNavigationControllerResult?> ProcessObservationAsync(
        NavigationObservation observation,
        string scriptFilePath,
        ScriptExecutionOptions scriptOptions,
        CancellationToken cancellationToken)
    {
        var uiState = observation.Snapshot.State;
        if (_terminalEvent?.Kind == ScriptWorkerEventKind.Failed)
        {
            TransitionIfAllowed(StageChallengeState.Failed, "Script worker failed.", observation.Sequence);
            return new AutoTaskNavigationControllerResult(
                ResolveScriptResult(ScriptExecutionStatus.Failed, _terminalEvent.Error),
                null);
        }

        if (uiState == GameUiStateId.InLevel)
            return await ProcessInLevelAsync(observation, scriptFilePath, scriptOptions).ConfigureAwait(false);

        if (uiState == GameUiStateId.Unknown)
        {
            if (_pausedForRecovery &&
                _recoveryStartedAt is DateTimeOffset unknownRecoveryStartedAt &&
                observation.CapturedAt - unknownRecoveryStartedAt >= _recoveryTimeout)
            {
                return await CancelAndHandoffAsync(observation).ConfigureAwait(false);
            }
            return null;
        }

        if (!_startRequested)
            return null;

        if (IsTerminal(_worker.State))
            return HandoffToNavigation(observation);

        _offLevelSince ??= observation.CapturedAt;
        TransitionIfAllowed(StageChallengeState.OffLevelGrace, $"Script is active while UI is '{uiState}'.", observation.Sequence);

        if (observation.CapturedAt - _offLevelSince < _offLevelGracePeriod)
            return null;

        if (!_pausedForRecovery)
        {
            TransitionIfAllowed(StageChallengeState.PausingForRecovery, "Non-level UI persisted beyond the recovery grace period.", observation.Sequence);
            if (_worker.State != ScriptWorkerState.Running ||
                !await PostAndWaitAsync(ScriptWorkerCommandKind.Pause, "Pause for in-level recovery", scriptOptions).ConfigureAwait(false))
            {
                return await CancelAndHandoffAsync(observation).ConfigureAwait(false);
            }

            _pausedForRecovery = true;
            _recoveryStartedAt = observation.CapturedAt;
            TransitionIfAllowed(StageChallengeState.Recovering, "Script paused at a safe checkpoint; recovery input is enabled.", observation.Sequence);
        }

        if (_recoveryStartedAt is DateTimeOffset recoveryStartedAt &&
            observation.CapturedAt - recoveryStartedAt >= _recoveryTimeout)
        {
            return await CancelAndHandoffAsync(observation).ConfigureAwait(false);
        }

        if (_recoveryExecutor is null)
            return await CancelAndHandoffAsync(observation).ConfigureAwait(false);

        if (_lastRecoveryClickAt is null || observation.CapturedAt - _lastRecoveryClickAt >= _recoveryClickInterval)
        {
            var clickResult = await _recoveryExecutor.ClickAsync(_recoveryPoint, cancellationToken).ConfigureAwait(false);
            if (!clickResult.Succeeded)
                return await CancelAndHandoffAsync(observation).ConfigureAwait(false);
            _lastRecoveryClickAt = observation.CapturedAt;
        }

        return null;
    }

    private async Task<AutoTaskNavigationControllerResult?> ProcessInLevelAsync(
        NavigationObservation observation,
        string scriptFilePath,
        ScriptExecutionOptions scriptOptions)
    {
        _offLevelSince = null;
        _recoveryStartedAt = null;
        _lastRecoveryClickAt = null;

        if (_worker.State == ScriptWorkerState.Completed)
        {
            _pausedForRecovery = false;
            TransitionIfAllowed(StageChallengeState.InLevel, "Script completed while the level remained active.", observation.Sequence);
            return null;
        }
        if (_worker.State is ScriptWorkerState.Cancelled or ScriptWorkerState.Failed)
        {
            TransitionIfAllowed(StageChallengeState.Failed, "Script became terminal unexpectedly while still in-level.", observation.Sequence);
            return new AutoTaskNavigationControllerResult(
                ResolveScriptResult(ScriptExecutionStatus.Failed),
                null);
        }

        if (_pausedForRecovery)
        {
            TransitionIfAllowed(StageChallengeState.Resuming, "In-level UI recovered.", observation.Sequence);
            if (!await PostAndWaitAsync(ScriptWorkerCommandKind.Resume, "Resume after in-level recovery", scriptOptions).ConfigureAwait(false))
            {
                if (_worker.State == ScriptWorkerState.Completed)
                {
                    _pausedForRecovery = false;
                    TransitionIfAllowed(StageChallengeState.InLevel, "Script completed during recovery.", observation.Sequence);
                    return null;
                }
                TransitionIfAllowed(StageChallengeState.Failed, "Script resume was not acknowledged.", observation.Sequence);
                return new AutoTaskNavigationControllerResult(ResolveScriptResult(ScriptExecutionStatus.Failed), null);
            }
            _pausedForRecovery = false;
        }

        TransitionIfAllowed(StageChallengeState.InLevel, "In-level UI confirmed.", observation.Sequence);
        if (!_startRequested)
        {
            _startRequested = true;
            if (!await PostAndWaitAsync(
                    ScriptWorkerCommandKind.Start,
                    "First in-level observation; start script.",
                    scriptOptions,
                    scriptFilePath).ConfigureAwait(false))
            {
                TransitionIfAllowed(StageChallengeState.Failed, "Script start was not acknowledged.", observation.Sequence);
                return new AutoTaskNavigationControllerResult(ResolveScriptResult(ScriptExecutionStatus.Failed), null);
            }
        }

        return null;
    }

    private async Task<AutoTaskNavigationControllerResult> CancelAndHandoffAsync(NavigationObservation observation)
    {
        if (!await CancelWorkerAsync("In-level recovery failed").ConfigureAwait(false))
        {
            TransitionIfAllowed(StageChallengeState.Failed, "Script cancellation was not acknowledged.", observation.Sequence);
            return new AutoTaskNavigationControllerResult(
                ResolveScriptResult(
                    ScriptExecutionStatus.Failed,
                    new TimeoutException("Script cancellation was not acknowledged.")),
                null);
        }
        TransitionIfAllowed(StageChallengeState.NavigationFallback, "Recovery failed; script stopped and navigation resumed.", observation.Sequence);
        return new AutoTaskNavigationControllerResult(
            ResolveScriptResult(ScriptExecutionStatus.Cancelled),
            observation.Snapshot);
    }

    private AutoTaskNavigationControllerResult HandoffToNavigation(NavigationObservation observation)
    {
        TransitionIfAllowed(StageChallengeState.NavigationFallback, "Script is terminal outside the level; navigation resumed.", observation.Sequence);
        return new AutoTaskNavigationControllerResult(
            ResolveScriptResult(_worker.State switch
            {
                ScriptWorkerState.Completed => ScriptExecutionStatus.Completed,
                ScriptWorkerState.Cancelled => ScriptExecutionStatus.Cancelled,
                _ => ScriptExecutionStatus.Failed
            }),
            observation.Snapshot);
    }

    private async Task<bool> PostAndWaitAsync(
        ScriptWorkerCommandKind kind,
        string reason,
        ScriptExecutionOptions options,
        string? filePath = null)
    {
        var requestSequence = ++_requestSequence;
        var acknowledgement = new TaskCompletionSource<ScriptWorkerEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_syncRoot)
            _pendingAcknowledgements[requestSequence] = acknowledgement;

        var command = new ScriptWorkerCommand(
            kind,
            _runId,
            requestSequence,
            reason,
            CancellationToken.None,
            true,
            kind == ScriptWorkerCommandKind.Start ? new ScriptWorkerStartRequest(filePath!, options) : null);
        if (!_worker.TryPostCommand(command))
        {
            lock (_syncRoot)
                _pendingAcknowledgements.Remove(requestSequence);
            return false;
        }

        try
        {
            var workerEvent = await acknowledgement.Task.WaitAsync(_acknowledgementTimeout).ConfigureAwait(false);
            return kind switch
            {
                ScriptWorkerCommandKind.Start => workerEvent.Kind == ScriptWorkerEventKind.Started,
                ScriptWorkerCommandKind.Pause => workerEvent.Kind == ScriptWorkerEventKind.PauseAcknowledged,
                ScriptWorkerCommandKind.Resume => workerEvent.Kind == ScriptWorkerEventKind.ResumeAcknowledged,
                ScriptWorkerCommandKind.Cancel => IsTerminal(workerEvent.State),
                _ => false
            };
        }
        catch (TimeoutException)
        {
            lock (_syncRoot)
                _pendingAcknowledgements.Remove(requestSequence);
            return false;
        }
    }

    private async Task<bool> CancelWorkerAsync(string reason)
    {
        if (!_startRequested || _worker.CurrentRunId != _runId || IsTerminal(_worker.State))
            return true;

        return await PostAndWaitAsync(
                ScriptWorkerCommandKind.Cancel,
                reason,
                new ScriptExecutionOptions())
            .ConfigureAwait(false);
    }

    private ScriptExecutionResult ResolveScriptResult(ScriptExecutionStatus fallbackStatus, Exception? exception = null)
    {
        if (_worker.CurrentRunId == _runId && _worker.LastResult is { } result)
            return result;

        return new ScriptExecutionResult
        {
            Status = fallbackStatus,
            ExecutedStepCount = 0,
            LastCompletedStepIndex = -1,
            Exception = exception,
            Failure = fallbackStatus == ScriptExecutionStatus.Failed
                ? new ScriptExecutionFailureDetails
                {
                    Message = exception?.GetBaseException().Message ?? "Script worker did not provide a terminal result."
                }
                : null
        };
    }

    private bool TryTakeAcknowledgement(long requestSequence, out TaskCompletionSource<ScriptWorkerEvent> acknowledgement)
    {
        lock (_syncRoot)
        {
            if (_pendingAcknowledgements.Remove(requestSequence, out var value))
            {
                acknowledgement = value;
                return true;
            }
        }

        acknowledgement = null!;
        return false;
    }

    private void CompletePendingWithTerminalEvent(ScriptWorkerEvent workerEvent)
    {
        TaskCompletionSource<ScriptWorkerEvent>[] pending;
        lock (_syncRoot)
        {
            pending = [.. _pendingAcknowledgements.Values];
            _pendingAcknowledgements.Clear();
        }
        foreach (var acknowledgement in pending)
            acknowledgement.TrySetResult(workerEvent);
    }

    private void CancelPendingAcknowledgements()
    {
        TaskCompletionSource<ScriptWorkerEvent>[] pending;
        lock (_syncRoot)
        {
            pending = [.. _pendingAcknowledgements.Values];
            _pendingAcknowledgements.Clear();
        }
        foreach (var acknowledgement in pending)
            acknowledgement.TrySetCanceled();
    }

    private void TransitionIfAllowed(StageChallengeState next, string reason, long sequence)
    {
        if (State == next || !StageChallengeStateTransitions.CanTransition(State, next))
            return;

        var at = _timeProvider.GetUtcNow();
        lock (_syncRoot)
            _transitions.Add(StageChallengeStateTransitions.Create(State, next, reason, at, sequence));
        State = next;
    }

    private static bool IsTerminal(ScriptWorkerState state)
    {
        return state is ScriptWorkerState.Completed or ScriptWorkerState.Cancelled or ScriptWorkerState.Failed;
    }
}
