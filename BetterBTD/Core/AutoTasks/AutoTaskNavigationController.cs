using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.AutoTasks;

/// <summary>
/// Coordinates the long-lived navigation observation stream with one script worker.
/// Recognition failures are treated as transient; the controller keeps observing until
/// cancellation or a terminal worker/result state is reached.
/// </summary>
public sealed class AutoTaskNavigationController
{
    private readonly INavigationObservationService _observations;
    private readonly IScriptTaskFlowWorker _worker;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _acknowledgementTimeout;
    private readonly object _syncRoot = new();
    private readonly List<StageChallengeStateTransition> _transitions = [];
    private long _requestSequence;
    private Guid _runId;
    private bool _pauseRequested;
    private bool _scriptStarted;
    private bool _scriptCompleted;
    private readonly Dictionary<long, TaskCompletionSource<ScriptWorkerEvent>> _pendingAcknowledgements = [];

    public AutoTaskNavigationController(
        INavigationObservationService observations,
        IScriptTaskFlowWorker worker,
        TimeProvider? timeProvider = null,
        TimeSpan? acknowledgementTimeout = null)
    {
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(5);
        if (_acknowledgementTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout));
        State = StageChallengeState.Preparing;
    }

    public StageChallengeState State { get; private set; }

    public ScriptWorkerState WorkerState => _worker.State;

    public IReadOnlyList<StageChallengeStateTransition> Transitions
    {
        get { lock (_syncRoot) return [.. _transitions]; }
    }

    public async Task RunAsync(
        string scriptFilePath,
        ScriptExecutionOptions scriptOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFilePath);
        ArgumentNullException.ThrowIfNull(scriptOptions);
        _runId = Guid.NewGuid();
        _requestSequence = 0;
        _pauseRequested = false;
        _scriptStarted = false;
        _scriptCompleted = false;
        lock (_syncRoot) _transitions.Clear();
        Transition(StageChallengeState.EnteringStage, "Navigation controller started.", 0);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _observations.Start(linked.Token);
        var workerEventsTask = ObserveWorkerEventsAsync(linked.Token);
        try
        {
            await using var subscription = _observations.SubscribeAsync(linked.Token)
                .GetAsyncEnumerator(linked.Token);
            while (await subscription.MoveNextAsync().ConfigureAwait(false))
            {
                var observation = subscription.Current;
                await ProcessObservationAsync(observation, scriptFilePath, scriptOptions, linked)
                    .ConfigureAwait(false);
                if (State is StageChallengeState.Completed or StageChallengeState.Failed or StageChallengeState.Cancelled)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CancelWorkerAsync("External cancellation").ConfigureAwait(false);
            TransitionIfAllowed(StageChallengeState.Cancelled, "External cancellation", _observations.LatestObservation?.Sequence ?? 0);
        }
        catch (Exception ex)
        {
            await CancelWorkerAsync("Controller failure").ConfigureAwait(false);
            TransitionIfAllowed(StageChallengeState.Failed, ex.GetBaseException().Message, _observations.LatestObservation?.Sequence ?? 0);
        }
        finally
        {
            linked.Cancel();
            lock (_syncRoot)
            {
                foreach (var acknowledgement in _pendingAcknowledgements.Values)
                    acknowledgement.TrySetCanceled();
                _pendingAcknowledgements.Clear();
            }
            try { await workerEventsTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            await _observations.StopAsync().ConfigureAwait(false);
        }
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
                    workerEvent.Kind is ScriptWorkerEventKind.PauseAcknowledged or ScriptWorkerEventKind.ResumeAcknowledged &&
                    TryTakeAcknowledgement(requestSequence, out var acknowledgement))
                {
                    acknowledgement.TrySetResult(workerEvent);
                }

                if (workerEvent.Kind == ScriptWorkerEventKind.Completed)
                    _scriptCompleted = true;
                else if (workerEvent.Kind == ScriptWorkerEventKind.Failed)
                    _scriptCompleted = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessObservationAsync(
        NavigationObservation observation,
        string scriptFilePath,
        ScriptExecutionOptions scriptOptions,
        CancellationTokenSource linked)
    {
        var snapshot = observation.Snapshot;
        if (snapshot.State == GameUiStateId.Unknown)
            return;

        if (IsResult(snapshot.State))
        {
            if (_scriptStarted && !_scriptCompleted)
                await CancelWorkerAsync("Stage result detected").ConfigureAwait(false);
            TransitionIfAllowed(
                StageChallengeState.ResultDetected,
                $"Detected result UI '{snapshot.State}'.",
                observation.Sequence);
            TransitionIfAllowed(
                snapshot.State == GameUiStateId.Defeat ? StageChallengeState.HandlingDefeat : StageChallengeState.HandlingVictory,
                "Handling stage result.", observation.Sequence);
            TransitionIfAllowed(StageChallengeState.Completed, "Result handling complete.", observation.Sequence);
            linked.Cancel();
            return;
        }

        if (IsPopup(snapshot.State))
        {
            if (_scriptStarted && !_pauseRequested && _worker.State == ScriptWorkerState.Running)
            {
                _pauseRequested = await PostAsync(ScriptWorkerCommandKind.Pause, "Popup detected", true, scriptOptions).ConfigureAwait(false);
            }
            TransitionIfAllowed(StageChallengeState.HandlingPopup, "Popup requires temporary pause.", observation.Sequence);
            return;
        }

        if (_pauseRequested && _worker.State == ScriptWorkerState.Paused)
        {
            await PostAsync(ScriptWorkerCommandKind.Resume, "Popup cleared", true, scriptOptions).ConfigureAwait(false);
            _pauseRequested = false;
            TransitionIfAllowed(_scriptCompleted ? StageChallengeState.ScriptCompletedWaitingForResult : StageChallengeState.ScriptRunning,
                "Resumed after popup.", observation.Sequence);
        }

        if (snapshot.State == GameUiStateId.InLevel)
        {
            TransitionIfAllowed(
                StageChallengeState.InStageBeforeScript,
                "Stage is ready for script execution.",
                observation.Sequence);
            if (!_scriptStarted)
            {
                _scriptStarted = await PostAsync(ScriptWorkerCommandKind.Start, "Stage loaded; starting script.", false, scriptOptions, scriptFilePath).ConfigureAwait(false);
                if (_scriptStarted)
                    TransitionIfAllowed(StageChallengeState.ScriptRunning, "Script worker started.", observation.Sequence);
            }
            else if (_scriptCompleted)
            {
                TransitionIfAllowed(StageChallengeState.ScriptCompletedWaitingForResult, "Waiting for stage result.", observation.Sequence);
            }
        }
    }

    private async Task<bool> PostAsync(ScriptWorkerCommandKind kind, string reason, bool wait, ScriptExecutionOptions options, string? filePath = null)
    {
        var requestSequence = ++_requestSequence;
        TaskCompletionSource<ScriptWorkerEvent>? acknowledgement = null;
        if (wait && kind is not (ScriptWorkerCommandKind.Start or ScriptWorkerCommandKind.Cancel))
        {
            acknowledgement = new TaskCompletionSource<ScriptWorkerEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_syncRoot)
                _pendingAcknowledgements[requestSequence] = acknowledgement;
        }

        var command = new ScriptWorkerCommand(kind, _runId, requestSequence, reason, CancellationToken.None, wait,
            kind == ScriptWorkerCommandKind.Start ? new ScriptWorkerStartRequest(filePath!, options) : null);
        if (!_worker.TryPostCommand(command))
        {
            if (acknowledgement is not null)
            {
                lock (_syncRoot)
                    _pendingAcknowledgements.Remove(requestSequence);
            }
            return false;
        }
        if (!wait || kind is ScriptWorkerCommandKind.Start or ScriptWorkerCommandKind.Cancel)
            return true;

        try
        {
            await acknowledgement!.Task.WaitAsync(_acknowledgementTimeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            lock (_syncRoot)
                _pendingAcknowledgements.Remove(requestSequence);
            TransitionIfAllowed(StageChallengeState.Failed, $"Worker acknowledgement timed out for command '{kind}'.", _observations.LatestObservation?.Sequence ?? 0);
            return false;
        }
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

    private async Task CancelWorkerAsync(string reason)
    {
        if (!_scriptStarted || _worker.CurrentRunId != _runId || _worker.State is ScriptWorkerState.Completed or ScriptWorkerState.Cancelled or ScriptWorkerState.Failed)
            return;
        await PostAsync(ScriptWorkerCommandKind.Cancel, reason, true, new ScriptExecutionOptions()).ConfigureAwait(false);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void TransitionIfAllowed(StageChallengeState next, string reason, long sequence)
    {
        if (State == next) return;
        if (StageChallengeStateTransitions.CanTransition(State, next)) Transition(next, reason, sequence);
    }

    private void Transition(StageChallengeState next, string reason, long sequence)
    {
        var at = _timeProvider.GetUtcNow();
        _transitions.Add(StageChallengeStateTransitions.Create(State, next, reason, at, sequence));
        State = next;
    }

    private static bool IsPopup(GameUiStateId state) => state is GameUiStateId.ConfirmDialog or GameUiStateId.LevelUp or GameUiStateId.StageHint or GameUiStateId.FreeplayPrompt or GameUiStateId.InstaMonkeyReward;
    private static bool IsResult(GameUiStateId state) => state is GameUiStateId.Victory or GameUiStateId.Defeat or GameUiStateId.StageSettlement or GameUiStateId.OdysseyStageVictory or GameUiStateId.OdysseySettlement or GameUiStateId.OdysseyReward or GameUiStateId.Reward or GameUiStateId.RaceResult or GameUiStateId.BossResult;
}
