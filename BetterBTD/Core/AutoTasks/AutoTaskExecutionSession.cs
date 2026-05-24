using BetterBTD.Models.AutoTasks;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.AutoTasks;

public sealed class AutoTaskExecutionSession
{
    private readonly object _syncRoot = new();
    private readonly AsyncManualResetEvent _resumeGate = new(initialState: true);
    private readonly AutoTaskProgressSnapshot _progressSnapshot;

    private AutoTaskRunState _runState = AutoTaskRunState.Idle;

    public AutoTaskExecutionSession(string taskKey, AutoTaskKind taskKind)
    {
        _progressSnapshot = new AutoTaskProgressSnapshot
        {
            TaskKey = taskKey ?? string.Empty,
            TaskKind = taskKind,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            RunState = AutoTaskRunState.Idle
        };
    }

    public event EventHandler<AutoTaskProgressSnapshot>? ProgressChanged;

    public AutoTaskRunState RunState
    {
        get
        {
            lock (_syncRoot)
            {
                return _runState;
            }
        }
    }

    public AutoTaskProgressSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return _progressSnapshot.Clone();
        }
    }

    public void MarkStarted(AutoTaskPhase phase, string message)
    {
        PublishUpdate(
            AutoTaskRunState.Running,
            phase,
            null,
            null,
            0,
            null,
            message,
            resetPauseRequested: true);
    }

    public void MarkLoopIteration(int loopIteration)
    {
        AutoTaskProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            _progressSnapshot.LoopIteration = loopIteration;
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(snapshot);
    }

    public void MarkPhase(AutoTaskPhase phase, string message)
    {
        PublishUpdate(
            null,
            phase,
            null,
            null,
            0,
            null,
            message,
            resetPauseRequested: false);
    }

    public void UpdateUiSnapshot(GameUiSnapshot snapshot, string message)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        AutoTaskProgressSnapshot progressSnapshot;
        lock (_syncRoot)
        {
            _progressSnapshot.CurrentUiState = snapshot.State;
            _progressSnapshot.LastUiSnapshot = snapshot;
            _progressSnapshot.Message = message;
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            progressSnapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(progressSnapshot);
    }

    public void UpdateActiveScript(
        string filePath,
        string displayName,
        IReadOnlyList<string> steps,
        string message)
    {
        ArgumentNullException.ThrowIfNull(steps);

        AutoTaskProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            _progressSnapshot.ActiveScriptPath = filePath ?? string.Empty;
            _progressSnapshot.ActiveScriptDisplayName = displayName ?? string.Empty;
            _progressSnapshot.ActiveScriptSteps = steps.Count == 0 ? Array.Empty<string>() : [.. steps];
            _progressSnapshot.ActiveScriptProgress = null;
            _progressSnapshot.Message = message;
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(snapshot);
    }

    public void UpdateActiveScriptProgress(ScriptExecutionProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        AutoTaskProgressSnapshot progressSnapshot;
        lock (_syncRoot)
        {
            _progressSnapshot.ActiveScriptProgress = snapshot.Clone();
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            progressSnapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(progressSnapshot);
    }

    public void UpdateNavigationFailures(int failureCount, string message)
    {
        AutoTaskProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            _progressSnapshot.ConsecutiveNavigationFailures = failureCount;
            _progressSnapshot.Message = message;
            _progressSnapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
            snapshot = _progressSnapshot.Clone();
        }

        RaiseProgressChanged(snapshot);
    }

    public bool RequestPause()
    {
        AutoTaskProgressSnapshot? snapshot = null;

        lock (_syncRoot)
        {
            if (_runState is not AutoTaskRunState.Running)
            {
                return false;
            }

            _runState = AutoTaskRunState.PauseRequested;
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
        AutoTaskProgressSnapshot? snapshot = null;

        lock (_syncRoot)
        {
            if (_runState is not (AutoTaskRunState.PauseRequested or AutoTaskRunState.Paused))
            {
                return false;
            }

            _runState = AutoTaskRunState.Running;
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
            checkpoint,
            attempt ?? 0,
            null,
            message,
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

            var slice = Math.Min(remaining, 100);
            await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            remaining -= slice;
        }

        await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
    }

    public void MarkCompleted(AutoTaskPhase phase, string message)
    {
        PublishTerminalState(AutoTaskRunState.Completed, phase, message);
    }

    public void MarkCancelled(AutoTaskPhase phase, string message)
    {
        PublishTerminalState(AutoTaskRunState.Cancelled, phase, message);
    }

    public void MarkFailed(AutoTaskPhase phase, string message)
    {
        PublishTerminalState(AutoTaskRunState.Failed, phase, message);
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            bool shouldWait;
            bool transitionedToPaused = false;
            AutoTaskProgressSnapshot? snapshot = null;

            lock (_syncRoot)
            {
                shouldWait = _runState is AutoTaskRunState.PauseRequested or AutoTaskRunState.Paused;
                if (!shouldWait)
                {
                    return;
                }

                if (_runState == AutoTaskRunState.PauseRequested)
                {
                    _runState = AutoTaskRunState.Paused;
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
        AutoTaskRunState runState,
        AutoTaskPhase phase,
        string message)
    {
        AutoTaskProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            _runState = runState;
            _progressSnapshot.RunState = runState;
            _progressSnapshot.Phase = phase;
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
        AutoTaskRunState? runState,
        AutoTaskPhase? phase,
        GameUiStateId? uiState,
        string? checkpoint,
        int currentAttempt,
        int? loopIteration,
        string? message,
        bool resetPauseRequested)
    {
        AutoTaskProgressSnapshot snapshot;
        lock (_syncRoot)
        {
            if (runState.HasValue)
            {
                _runState = runState.Value;
                _progressSnapshot.RunState = runState.Value;
            }

            if (phase.HasValue)
            {
                _progressSnapshot.Phase = phase.Value;
            }

            if (uiState.HasValue)
            {
                _progressSnapshot.CurrentUiState = uiState.Value;
            }

            if (checkpoint is not null)
            {
                _progressSnapshot.CurrentCheckpoint = checkpoint;
            }

            _progressSnapshot.CurrentAttempt = Math.Max(0, currentAttempt);

            if (loopIteration.HasValue)
            {
                _progressSnapshot.LoopIteration = loopIteration.Value;
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

    private void RaiseProgressChanged(AutoTaskProgressSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            ProgressChanged?.Invoke(this, snapshot);
        }
    }
}
