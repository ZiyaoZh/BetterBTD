using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.ScriptExecution;

public sealed class ScriptExecutionSession
{
    private readonly object _syncRoot = new();
    private readonly AsyncManualResetEvent _resumeGate = new(initialState: true);
    private readonly ScriptExecutionProgressSnapshot _progressSnapshot;

    private ScriptExecutionRunState _runState = ScriptExecutionRunState.Idle;

    public ScriptExecutionSession(string sourceFilePath)
    {
        _progressSnapshot = new ScriptExecutionProgressSnapshot
        {
            SourceFilePath = sourceFilePath ?? string.Empty,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            RunState = ScriptExecutionRunState.Idle
        };
    }

    public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

    public ScriptExecutionRunState RunState
    {
        get
        {
            lock (_syncRoot)
            {
                return _runState;
            }
        }
    }

    public ScriptExecutionProgressSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _progressSnapshot.Clone();
        }
    }

    public void MarkStarted()
    {
        PublishUpdate(
            ScriptExecutionRunState.Running,
            null,
            null,
            "Execution started.",
            null,
            null,
            resetPauseRequested: true);
    }

    public void EnterStep(int stepIndex, string commandType)
    {
        PublishUpdate(
            null,
            stepIndex,
            commandType,
            "Step entered.",
            "StepEntered",
            0,
            resetPauseRequested: false);
    }

    public void MarkStepCompleted(int completedStepCount, int lastCompletedStepIndex)
    {
        ScriptExecutionProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            _progressSnapshot.CompletedStepCount = completedStepCount;
            _progressSnapshot.LastCompletedStepIndex = lastCompletedStepIndex;
            _progressSnapshot.CurrentCheckpoint = "StepCompleted";
            _progressSnapshot.CurrentAttempt = 0;
            _progressSnapshot.Message = "Step completed.";
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(snapshot);
    }

    public void MarkCompleted(int executedStepCount, int lastCompletedStepIndex)
    {
        PublishTerminalState(
            ScriptExecutionRunState.Completed,
            executedStepCount,
            lastCompletedStepIndex,
            "Execution completed.");
    }

    public void MarkCancelled(int executedStepCount, int lastCompletedStepIndex)
    {
        PublishTerminalState(
            ScriptExecutionRunState.Cancelled,
            executedStepCount,
            lastCompletedStepIndex,
            "Execution cancelled.");
    }

    public void MarkFailed(int executedStepCount, int lastCompletedStepIndex, string message)
    {
        PublishTerminalState(
            ScriptExecutionRunState.Failed,
            executedStepCount,
            lastCompletedStepIndex,
            string.IsNullOrWhiteSpace(message) ? "Execution failed." : message);
    }

    public bool RequestPause()
    {
        ScriptExecutionProgressSnapshot? snapshot = null;

        lock (_syncRoot)
        {
            if (_runState is not ScriptExecutionRunState.Running)
            {
                return false;
            }

            _runState = ScriptExecutionRunState.PauseRequested;
            _progressSnapshot.RunState = _runState;
            _progressSnapshot.IsPauseRequested = true;
            _progressSnapshot.CurrentCheckpoint = "PauseRequested";
            _progressSnapshot.Message = "Pause requested.";
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
            _resumeGate.Reset();
        }

        RaiseProgressChanged(snapshot);
        return true;
    }

    public bool Resume()
    {
        ScriptExecutionProgressSnapshot? snapshot = null;

        lock (_syncRoot)
        {
            if (_runState is not (ScriptExecutionRunState.PauseRequested or ScriptExecutionRunState.Paused))
            {
                return false;
            }

            _runState = ScriptExecutionRunState.Running;
            _progressSnapshot.RunState = _runState;
            _progressSnapshot.IsPauseRequested = false;
            _progressSnapshot.CurrentCheckpoint = "Resumed";
            _progressSnapshot.CurrentAttempt = 0;
            _progressSnapshot.Message = "Execution resumed.";
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
            _resumeGate.Set();
        }

        RaiseProgressChanged(snapshot);
        return true;
    }

    public async Task ReachCheckpointAsync(string checkpoint, string? message, int? attempt, CancellationToken cancellationToken)
    {
        PublishUpdate(
            null,
            null,
            null,
            message,
            checkpoint,
            attempt,
            resetPauseRequested: false);

        await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        var remaining = Math.Max(0, milliseconds);
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            var delaySlice = Math.Min(remaining, 100);
            await Task.Delay(delaySlice, cancellationToken).ConfigureAwait(false);
            remaining -= delaySlice;
        }

        await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            bool shouldWait;
            bool transitionedToPaused = false;
            ScriptExecutionProgressSnapshot? snapshot = null;

            lock (_syncRoot)
            {
                shouldWait = _runState is ScriptExecutionRunState.PauseRequested or ScriptExecutionRunState.Paused;
                if (!shouldWait)
                {
                    return;
                }

                if (_runState == ScriptExecutionRunState.PauseRequested)
                {
                    _runState = ScriptExecutionRunState.Paused;
                    _progressSnapshot.RunState = _runState;
                    _progressSnapshot.IsPauseRequested = true;
                    _progressSnapshot.CurrentCheckpoint = "Paused";
                    _progressSnapshot.Message = "Execution paused at a safe checkpoint.";
                    _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
                    snapshot = _progressSnapshot.Clone();
                    transitionedToPaused = true;
                }
            }

            if (transitionedToPaused && snapshot is not null)
            {
                RaiseProgressChanged(snapshot);
            }

            await _resumeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void PublishTerminalState(
        ScriptExecutionRunState runState,
        int completedStepCount,
        int lastCompletedStepIndex,
        string message)
    {
        ScriptExecutionProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            _runState = runState;
            _progressSnapshot.RunState = runState;
            _progressSnapshot.CompletedStepCount = completedStepCount;
            _progressSnapshot.LastCompletedStepIndex = lastCompletedStepIndex;
            _progressSnapshot.CurrentCheckpoint = runState.ToString();
            _progressSnapshot.CurrentAttempt = 0;
            _progressSnapshot.IsPauseRequested = false;
            _progressSnapshot.Message = message;
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(snapshot);
    }

    private void PublishUpdate(
        ScriptExecutionRunState? runState,
        int? stepIndex,
        string? commandType,
        string? message,
        string? checkpoint,
        int? attempt,
        bool resetPauseRequested)
    {
        ScriptExecutionProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            if (runState.HasValue)
            {
                _runState = runState.Value;
                _progressSnapshot.RunState = runState.Value;
            }

            if (stepIndex.HasValue)
            {
                _progressSnapshot.CurrentStepIndex = stepIndex.Value;
            }

            if (commandType is not null)
            {
                _progressSnapshot.CurrentCommandType = commandType;
            }

            if (checkpoint is not null)
            {
                _progressSnapshot.CurrentCheckpoint = checkpoint;
            }

            if (attempt.HasValue)
            {
                _progressSnapshot.CurrentAttempt = Math.Max(0, attempt.Value);
            }

            if (message is not null)
            {
                _progressSnapshot.Message = message;
            }

            if (resetPauseRequested)
            {
                _progressSnapshot.IsPauseRequested = false;
            }

            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(snapshot);
    }

    private void RaiseProgressChanged(ScriptExecutionProgressSnapshot snapshot)
    {
        ProgressChanged?.Invoke(this, snapshot);
    }
}

internal sealed class AsyncManualResetEvent
{
    private volatile TaskCompletionSource<bool> _taskCompletionSource;

    public AsyncManualResetEvent(bool initialState)
    {
        _taskCompletionSource = CreateTaskCompletionSource();
        if (initialState)
        {
            _taskCompletionSource.TrySetResult(true);
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        var waitTask = _taskCompletionSource.Task;
        return waitTask.IsCompleted || !cancellationToken.CanBeCanceled
            ? waitTask
            : WaitWithCancellationAsync(waitTask, cancellationToken);
    }

    public void Set()
    {
        _taskCompletionSource.TrySetResult(true);
    }

    public void Reset()
    {
        while (true)
        {
            var current = _taskCompletionSource;
            if (!current.Task.IsCompleted)
            {
                return;
            }

            var next = CreateTaskCompletionSource();
            if (Interlocked.CompareExchange(ref _taskCompletionSource, next, current) == current)
            {
                return;
            }
        }
    }

    private static async Task WaitWithCancellationAsync(Task waitTask, CancellationToken cancellationToken)
    {
        var cancellationTaskCompletionSource = CreateTaskCompletionSource();
        await using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            cancellationTaskCompletionSource);

        var completedTask = await Task.WhenAny(waitTask, cancellationTaskCompletionSource.Task).ConfigureAwait(false);
        await completedTask.ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> CreateTaskCompletionSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
