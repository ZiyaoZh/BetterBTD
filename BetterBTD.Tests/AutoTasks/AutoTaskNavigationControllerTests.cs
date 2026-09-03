using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskNavigationControllerTests
{
    [Fact]
    public async Task CompletedScript_RemainsIdleInLevel_ThenHandsPageToNavigation()
    {
        var observations = new TestObservationService();
        var engine = new ControllableEngine();
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        var controller = CreateController(observations, worker);
        var startedAt = DateTimeOffset.UtcNow;

        var runTask = controller.RunAsync("completed.json", CreateOptions());
        observations.Publish(GameUiStateId.InLevel, startedAt);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        engine.Complete(ScriptExecutionStatus.Completed);
        await WaitUntilAsync(() => worker.State == ScriptWorkerState.Completed);

        observations.Publish(GameUiStateId.InLevel, startedAt.AddSeconds(1));
        await Task.Delay(50);

        Assert.False(runTask.IsCompleted);
        Assert.Equal(1, engine.ExecuteCallCount);
        Assert.Equal(0, engine.RequestPauseCallCount);

        observations.Publish(GameUiStateId.Victory, startedAt.AddSeconds(2));
        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ScriptExecutionStatus.Completed, result.ScriptResult.Status);
        Assert.Equal(GameUiStateId.Victory, result.HandoffSnapshot?.State);
        Assert.Equal(StageChallengeState.NavigationFallback, controller.State);
        await worker.StopAsync();
    }

    [Fact]
    public async Task ActiveScript_UsesContinuousOffLevelGrace_ThenRecoversAndResumes()
    {
        var observations = new TestObservationService();
        var engine = new ControllableEngine();
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        var recovery = new RecordingRecoveryExecutor();
        var controller = CreateController(observations, worker, recovery);
        var startedAt = DateTimeOffset.UtcNow;

        var runTask = controller.RunAsync("recovery.json", CreateOptions());
        observations.Publish(GameUiStateId.InLevel, startedAt);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        observations.Publish(GameUiStateId.ConfirmDialog, startedAt.AddSeconds(1));
        observations.Publish(GameUiStateId.StageHint, startedAt.AddSeconds(5.9));
        observations.Publish(GameUiStateId.Unknown, startedAt.AddSeconds(6.5));
        await Task.Delay(50);

        Assert.Equal(0, engine.RequestPauseCallCount);
        Assert.Empty(recovery.ClickedPoints);

        observations.Publish(GameUiStateId.MainMenu, startedAt.AddSeconds(6));
        await WaitUntilAsync(() => recovery.ClickedPoints.Count == 1);
        Assert.Equal(new GameUiRecoveryPoint(960, 540), recovery.ClickedPoints[0]);
        Assert.Equal(1, engine.RequestPauseCallCount);

        observations.Publish(GameUiStateId.InLevel, startedAt.AddSeconds(6.1));
        await WaitUntilAsync(() => engine.ResumeCallCount == 1);
        Assert.Equal(1, engine.ExecuteCallCount);
        Assert.False(runTask.IsCompleted);

        engine.Complete(ScriptExecutionStatus.Completed);
        await WaitUntilAsync(() => worker.State == ScriptWorkerState.Completed);
        observations.Publish(GameUiStateId.Victory, startedAt.AddSeconds(7));
        _ = await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync();
    }

    [Fact]
    public async Task RecoveryTimeout_WaitsForCancellationThenHandsLatestPageToNavigation()
    {
        var observations = new TestObservationService();
        var engine = new ControllableEngine();
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        var recovery = new RecordingRecoveryExecutor();
        var controller = CreateController(observations, worker, recovery, TimeSpan.FromSeconds(2));
        var startedAt = DateTimeOffset.UtcNow;

        var runTask = controller.RunAsync("cancel.json", CreateOptions());
        observations.Publish(GameUiStateId.InLevel, startedAt);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        observations.Publish(GameUiStateId.ConfirmDialog, startedAt.AddSeconds(1));
        observations.Publish(GameUiStateId.StageHint, startedAt.AddSeconds(6));
        await WaitUntilAsync(() => recovery.ClickedPoints.Count == 1);
        observations.Publish(GameUiStateId.MainMenu, startedAt.AddSeconds(8));

        var result = await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ScriptExecutionStatus.Cancelled, result.ScriptResult.Status);
        Assert.Equal(GameUiStateId.MainMenu, result.HandoffSnapshot?.State);
        Assert.Equal(ScriptWorkerState.Cancelled, worker.State);
        Assert.True(engine.RequestStopCallCount > 0);
        await worker.StopAsync();
    }

    private static AutoTaskNavigationController CreateController(
        TestObservationService observations,
        ScriptTaskFlowWorker worker,
        IGameUiStuckRecoveryExecutor? recovery = null,
        TimeSpan? recoveryTimeout = null)
    {
        return new AutoTaskNavigationController(
            observations,
            worker,
            recovery,
            acknowledgementTimeout: TimeSpan.FromSeconds(1),
            offLevelGracePeriod: TimeSpan.FromSeconds(5),
            recoveryTimeout: recoveryTimeout ?? TimeSpan.FromSeconds(5),
            recoveryClickInterval: TimeSpan.Zero);
    }

    private static ScriptExecutionOptions CreateOptions()
    {
        return new ScriptExecutionOptions
        {
            RequireCaptureService = false,
            RequireTargetWindow = false
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeoutAt)
                throw new TimeoutException("Expected condition was not reached.");
            await Task.Delay(10);
        }
    }

    private sealed class TestObservationService : INavigationObservationService
    {
        private readonly Channel<NavigationObservation> _channel = Channel.CreateUnbounded<NavigationObservation>();
        private long _sequence;

        public NavigationObservation? LatestObservation { get; private set; }

        public NavigationObservationDiagnostics GetDiagnostics() => new();

        public void Start(CancellationToken cancellationToken = default) => cancellationToken.ThrowIfCancellationRequested();

        public async IAsyncEnumerable<NavigationObservation> SubscribeAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var observation in _channel.Reader.ReadAllAsync(cancellationToken))
                yield return observation;
        }

        public Task StopAsync()
        {
            _channel.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public void Publish(GameUiStateId state, DateTimeOffset capturedAt)
        {
            var snapshot = new GameUiSnapshot { State = state, CapturedAt = capturedAt };
            LatestObservation = new NavigationObservation(
                Interlocked.Increment(ref _sequence),
                capturedAt,
                snapshot);
            Assert.True(_channel.Writer.TryWrite(LatestObservation));
        }
    }

    private sealed class RecordingRecoveryExecutor : IGameUiStuckRecoveryExecutor
    {
        public List<GameUiRecoveryPoint> ClickedPoints { get; } = [];

        public Task<GameUiActionExecutionResult> ClickAsync(
            GameUiRecoveryPoint point,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClickedPoints.Add(point);
            return Task.FromResult(new GameUiActionExecutionResult { Succeeded = true });
        }
    }

    private sealed class ControllableEngine : IScriptTaskFlowExecutionEngine
    {
        private TaskCompletionSource<ScriptExecutionResult> _completion = CreateCompletion();

        public bool IsRunning { get; private set; }

        public int ExecuteCallCount { get; private set; }

        public int RequestPauseCallCount { get; private set; }

        public int ResumeCallCount { get; private set; }

        public int RequestStopCallCount { get; private set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public bool RequestPause()
        {
            RequestPauseCallCount++;
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot { RunState = ScriptExecutionRunState.PauseRequested });
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot { RunState = ScriptExecutionRunState.Paused });
            return true;
        }

        public bool Resume()
        {
            ResumeCallCount++;
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot { RunState = ScriptExecutionRunState.Running });
            return true;
        }

        public bool RequestStop()
        {
            RequestStopCallCount++;
            Complete(ScriptExecutionStatus.Cancelled);
            return true;
        }

        public Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            IsRunning = true;
            _completion = CreateCompletion();
            cancellationToken.Register(() => Complete(ScriptExecutionStatus.Cancelled));
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete(ScriptExecutionStatus status)
        {
            IsRunning = false;
            _completion.TrySetResult(new ScriptExecutionResult
            {
                Status = status,
                ExecutedStepCount = 1,
                LastCompletedStepIndex = 0
            });
        }

        private static TaskCompletionSource<ScriptExecutionResult> CreateCompletion()
        {
            return new TaskCompletionSource<ScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
