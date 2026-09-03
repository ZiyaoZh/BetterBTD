using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading.Channels;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.ScriptExecution;

public sealed class ScriptTaskFlowWorker : IScriptTaskFlowWorker
{
    private static readonly Lazy<ScriptTaskFlowWorker> InstanceHolder = new(
        () => new ScriptTaskFlowWorker(ScriptTaskFlowExecutor.Instance, TimeProvider.System));

    private readonly object _syncRoot = new();
    private readonly IScriptTaskFlowExecutionEngine _executionEngine;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<ScriptWorkerCommand> _commands;
    private readonly Dictionary<long, Channel<ScriptWorkerEvent>> _subscribers = [];
    private readonly CancellationTokenSource _lifetimeCancellationSource = new();
    private readonly Task _commandLoopTask;

    private ScriptWorkerState _state = ScriptWorkerState.NotStarted;
    private Guid? _currentRunId;
    private long _lastRequestSequence;
    private long? _pendingPauseRequestSequence;
    private long? _pendingResumeRequestSequence;
    private long _nextSubscriberId;
    private CancellationTokenSource? _runCancellationSource;
    private Task? _executionTask;

    private ScriptTaskFlowWorker()
        : this(ScriptTaskFlowExecutor.Instance, TimeProvider.System)
    {
    }

    internal ScriptTaskFlowWorker(
        IScriptTaskFlowExecutionEngine executionEngine,
        TimeProvider timeProvider)
    {
        _executionEngine = executionEngine ?? throw new ArgumentNullException(nameof(executionEngine));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _commands = Channel.CreateUnbounded<ScriptWorkerCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _executionEngine.ProgressChanged += OnExecutionProgressChanged;
        _commandLoopTask = Task.Run(
            () => RunCommandLoopAsync(_lifetimeCancellationSource.Token),
            CancellationToken.None);
    }

    public static ScriptTaskFlowWorker Instance => InstanceHolder.Value;

    public ScriptWorkerState State
    {
        get
        {
            lock (_syncRoot)
            {
                return _state;
            }
        }
    }

    public Guid? CurrentRunId
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentRunId;
            }
        }
    }

    public bool TryPostCommand(ScriptWorkerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return !_lifetimeCancellationSource.IsCancellationRequested && _commands.Writer.TryWrite(command);
    }

    public async IAsyncEnumerable<ScriptWorkerEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<ScriptWorkerEvent>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        long subscriberId;
        lock (_syncRoot)
        {
            subscriberId = ++_nextSubscriberId;
            _subscribers.Add(subscriberId, channel);
        }

        try
        {
            await foreach (var workerEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return workerEvent;
            }
        }
        finally
        {
            lock (_syncRoot)
            {
                _subscribers.Remove(subscriberId);
            }

            channel.Writer.TryComplete();
        }
    }

    public async Task StopAsync()
    {
        _lifetimeCancellationSource.Cancel();
        _commands.Writer.TryComplete();

        CancellationTokenSource? runCancellationSource;
        Task? executionTask;
        lock (_syncRoot)
        {
            runCancellationSource = _runCancellationSource;
            executionTask = _executionTask;
        }

        runCancellationSource?.Cancel();
        try
        {
            _executionEngine.RequestStop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Script worker stop request failed and cleanup will continue: {ex}");
        }
        await _commandLoopTask.ConfigureAwait(false);
        if (executionTask is not null)
        {
            await executionTask.ConfigureAwait(false);
        }

        _executionEngine.ProgressChanged -= OnExecutionProgressChanged;
        CompleteSubscribers();
    }

    private async Task RunCommandLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    HandleCommand(command);
                }
                catch (Exception ex)
                {
                    PublishCommandDiagnostic(command, ex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleCommand(ScriptWorkerCommand command)
    {
        if (command.CancellationToken.IsCancellationRequested && command.Kind != ScriptWorkerCommandKind.Cancel)
        {
            return;
        }

        switch (command.Kind)
        {
            case ScriptWorkerCommandKind.Start:
                HandleStart(command);
                break;
            case ScriptWorkerCommandKind.Pause:
                HandlePause(command);
                break;
            case ScriptWorkerCommandKind.Resume:
                HandleResume(command);
                break;
            case ScriptWorkerCommandKind.Cancel:
                HandleCancel(command);
                break;
        }
    }

    private void HandleStart(ScriptWorkerCommand command)
    {
        ScriptWorkerStartRequest startRequest = command.StartRequest!;
        CancellationToken runCancellationToken;
        lock (_syncRoot)
        {
            if (_executionTask is { IsCompleted: false } || IsActiveState(_state))
            {
                return;
            }

            _runCancellationSource?.Dispose();
            _runCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                command.CancellationToken,
                _lifetimeCancellationSource.Token);
            _currentRunId = command.RunId;
            _lastRequestSequence = command.RequestSequence;
            _pendingPauseRequestSequence = null;
            _pendingResumeRequestSequence = null;
            _state = ScriptWorkerState.NotStarted;
            TransitionTo(ScriptWorkerState.Starting);
            TransitionTo(ScriptWorkerState.Running);
            runCancellationToken = _runCancellationSource.Token;
            Publish(new ScriptWorkerEvent(
                ScriptWorkerEventKind.Started,
                command.RunId,
                ScriptWorkerState.Running,
                _timeProvider.GetUtcNow(),
                currentStepIndex: -1,
                lastCompletedStepIndex: -1));
            _executionTask = RunExecutionAsync(command.RunId, startRequest, runCancellationToken);
        }
    }

    private void HandlePause(ScriptWorkerCommand command)
    {
        lock (_syncRoot)
        {
            if (!CanAcceptCommand(command, ScriptWorkerState.Running))
            {
                return;
            }

            _lastRequestSequence = command.RequestSequence;
            _pendingPauseRequestSequence = command.RequestSequence;
            if (!_executionEngine.RequestPause())
            {
                _pendingPauseRequestSequence = null;
            }
        }
    }

    private void HandleResume(ScriptWorkerCommand command)
    {
        lock (_syncRoot)
        {
            if (!CanAcceptCommand(command, ScriptWorkerState.Paused))
            {
                return;
            }

            _lastRequestSequence = command.RequestSequence;
            _pendingResumeRequestSequence = command.RequestSequence;
            if (!_executionEngine.Resume())
            {
                _pendingResumeRequestSequence = null;
            }
        }
    }

    private void HandleCancel(ScriptWorkerCommand command)
    {
        CancellationTokenSource? runCancellationSource;
        lock (_syncRoot)
        {
            if (!CanAcceptCommand(command, _state) || !IsActiveState(_state))
            {
                return;
            }

            _lastRequestSequence = command.RequestSequence;
            _pendingPauseRequestSequence = null;
            _pendingResumeRequestSequence = null;
            TransitionTo(ScriptWorkerState.CancellationRequested);
            runCancellationSource = _runCancellationSource;
        }

        runCancellationSource?.Cancel();
        _executionEngine.RequestStop();
    }

    private bool CanAcceptCommand(ScriptWorkerCommand command, ScriptWorkerState requiredState)
    {
        return _currentRunId == command.RunId &&
               command.RequestSequence > _lastRequestSequence &&
               _state == requiredState;
    }

    private async Task RunExecutionAsync(
        Guid runId,
        ScriptWorkerStartRequest startRequest,
        CancellationToken cancellationToken)
    {
        ScriptExecutionResult result;
        try
        {
            result = await _executionEngine
                .ExecuteAsync(startRequest.FilePath, startRequest.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = CreateTerminalResult(ScriptExecutionStatus.Cancelled);
        }
        catch (Exception ex)
        {
            result = CreateTerminalResult(ScriptExecutionStatus.Failed, ex);
        }

        CompleteRun(runId, result);
    }

    private void CompleteRun(Guid runId, ScriptExecutionResult result)
    {
        ScriptWorkerEvent terminalEvent;
        lock (_syncRoot)
        {
            if (_currentRunId != runId || IsTerminalState(_state))
            {
                return;
            }

            var progress = result.FinalProgress;
            var currentStepIndex = progress?.CurrentStepIndex ?? -1;
            var lastCompletedStepIndex = result.LastCompletedStepIndex;
            if (currentStepIndex < lastCompletedStepIndex)
            {
                currentStepIndex = lastCompletedStepIndex;
            }

            var eventKind = result.Status switch
            {
                ScriptExecutionStatus.Completed => ScriptWorkerEventKind.Completed,
                ScriptExecutionStatus.Cancelled => ScriptWorkerEventKind.Cancelled,
                _ => ScriptWorkerEventKind.Failed
            };
            var terminalState = result.Status switch
            {
                ScriptExecutionStatus.Completed => ScriptWorkerState.Completed,
                ScriptExecutionStatus.Cancelled => ScriptWorkerState.Cancelled,
                _ => ScriptWorkerState.Failed
            };
            if (terminalState == ScriptWorkerState.Cancelled &&
                _state != ScriptWorkerState.CancellationRequested)
            {
                TransitionTo(ScriptWorkerState.CancellationRequested);
            }

            TransitionTo(terminalState);
            var error = eventKind == ScriptWorkerEventKind.Failed
                ? result.Exception ?? new InvalidOperationException(
                    result.Failure?.Message ?? "Script execution failed.")
                : null;
            terminalEvent = new ScriptWorkerEvent(
                eventKind,
                runId,
                terminalState,
                _timeProvider.GetUtcNow(),
                currentStepIndex: currentStepIndex,
                lastCompletedStepIndex: lastCompletedStepIndex,
                error: error,
                diagnostics: CreateDiagnostics(progress));
        }

        Publish(terminalEvent);
    }

    private void OnExecutionProgressChanged(object? sender, ScriptExecutionProgressSnapshot snapshot)
    {
        ScriptWorkerEvent? acknowledgement = null;
        ScriptWorkerEvent? progressEvent;
        lock (_syncRoot)
        {
            if (_currentRunId is not Guid runId || IsTerminalState(_state))
            {
                return;
            }

            if (snapshot.RunState == ScriptExecutionRunState.PauseRequested &&
                _state == ScriptWorkerState.Running &&
                _pendingPauseRequestSequence is not null)
            {
                TransitionTo(ScriptWorkerState.Pausing);
            }
            else if (snapshot.RunState == ScriptExecutionRunState.Paused &&
                _state == ScriptWorkerState.Pausing &&
                _pendingPauseRequestSequence is long pauseRequestSequence)
            {
                TransitionTo(ScriptWorkerState.Paused);
                _pendingPauseRequestSequence = null;
                acknowledgement = new ScriptWorkerEvent(
                    ScriptWorkerEventKind.PauseAcknowledged,
                    runId,
                    ScriptWorkerState.Paused,
                    _timeProvider.GetUtcNow(),
                    pauseRequestSequence,
                    snapshot.CurrentStepIndex,
                    snapshot.LastCompletedStepIndex,
                    diagnostics: CreateDiagnostics(snapshot));
            }
            else if (snapshot.RunState == ScriptExecutionRunState.Running &&
                     _state == ScriptWorkerState.Paused &&
                     _pendingResumeRequestSequence is long resumeRequestSequence)
            {
                TransitionTo(ScriptWorkerState.Running);
                _pendingResumeRequestSequence = null;
                acknowledgement = new ScriptWorkerEvent(
                    ScriptWorkerEventKind.ResumeAcknowledged,
                    runId,
                    ScriptWorkerState.Running,
                    _timeProvider.GetUtcNow(),
                    resumeRequestSequence,
                    snapshot.CurrentStepIndex,
                    snapshot.LastCompletedStepIndex,
                    diagnostics: CreateDiagnostics(snapshot));
            }

            progressEvent = new ScriptWorkerEvent(
                ScriptWorkerEventKind.ProgressChanged,
                runId,
                _state,
                _timeProvider.GetUtcNow(),
                currentStepIndex: snapshot.CurrentStepIndex,
                lastCompletedStepIndex: snapshot.LastCompletedStepIndex,
                diagnostics: CreateDiagnostics(snapshot));
        }

        Publish(progressEvent);
        if (acknowledgement is not null)
        {
            Publish(acknowledgement);
        }
    }

    private void PublishCommandDiagnostic(ScriptWorkerCommand command, Exception exception)
    {
        ScriptWorkerEvent? diagnosticEvent = null;
        lock (_syncRoot)
        {
            if (_currentRunId != command.RunId || IsTerminalState(_state))
            {
                return;
            }

            diagnosticEvent = new ScriptWorkerEvent(
                ScriptWorkerEventKind.ProgressChanged,
                command.RunId,
                _state,
                _timeProvider.GetUtcNow(),
                diagnostics: new ScriptObservationDiagnostics(
                    null,
                    $"Command{command.Kind}",
                    0,
                    $"Worker command was ignored after an internal error: {exception.GetBaseException().Message}"));
        }

        Debug.WriteLine($"Script worker command '{command.Kind}' failed and was ignored: {exception}");
        Publish(diagnosticEvent);
    }

    private void Publish(ScriptWorkerEvent workerEvent)
    {
        lock (_syncRoot)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryWrite(workerEvent);
            }
        }
    }

    private void CompleteSubscribers()
    {
        lock (_syncRoot)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    private static ScriptExecutionResult CreateTerminalResult(
        ScriptExecutionStatus status,
        Exception? exception = null)
    {
        return new ScriptExecutionResult
        {
            Status = status,
            ExecutedStepCount = 0,
            LastCompletedStepIndex = -1,
            Exception = exception,
            Failure = exception is null
                ? null
                : new ScriptExecutionFailureDetails { Message = exception.GetBaseException().Message }
        };
    }

    private static ScriptObservationDiagnostics? CreateDiagnostics(ScriptExecutionProgressSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.CurrentCheckpoint))
        {
            return null;
        }

        return new ScriptObservationDiagnostics(
            null,
            snapshot.CurrentCheckpoint,
            snapshot.CurrentAttempt,
            snapshot.Message);
    }

    private static bool IsActiveState(ScriptWorkerState state)
    {
        return state is ScriptWorkerState.Starting or
            ScriptWorkerState.Running or
            ScriptWorkerState.Pausing or
            ScriptWorkerState.Paused or
            ScriptWorkerState.CancellationRequested;
    }

    private static bool IsTerminalState(ScriptWorkerState state)
    {
        return state is ScriptWorkerState.Completed or ScriptWorkerState.Cancelled or ScriptWorkerState.Failed;
    }

    private void TransitionTo(ScriptWorkerState nextState)
    {
        if (_state == nextState)
        {
            return;
        }

        if (!ScriptWorkerStateTransitions.CanTransition(_state, nextState))
        {
            Debug.WriteLine($"Script worker recovered from an unexpected state transition: {_state} -> {nextState}.");
        }

        _state = nextState;
    }
}
