using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Core.GameControl;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptTaskFlowWorkerTests
{
    [Fact]
    public async Task Commands_ProduceCorrelatedLifecycleEvents()
    {
        var engine = new ControllableExecutionEngine();
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var events = worker.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
        var nextEventTask = events.MoveNextAsync().AsTask();
        var runId = Guid.NewGuid();

        Assert.True(worker.TryPostCommand(CreateCommand(
            ScriptWorkerCommandKind.Start,
            runId,
            1,
            new ScriptWorkerStartRequest("worker-test.json", CreateOptions()))));
        var started = await ReadNextAsync(events, nextEventTask);
        Assert.Equal(ScriptWorkerEventKind.Started, started.Kind);
        Assert.Equal(runId, started.RunId);

        Assert.True(worker.TryPostCommand(CreateCommand(ScriptWorkerCommandKind.Pause, runId, 2)));
        var paused = await ReadUntilAsync(events, ScriptWorkerEventKind.PauseAcknowledged);
        Assert.Equal(2, paused.RequestSequence);
        Assert.Equal(ScriptWorkerState.Paused, paused.State);

        Assert.True(worker.TryPostCommand(CreateCommand(ScriptWorkerCommandKind.Resume, runId, 3)));
        var resumed = await ReadUntilAsync(events, ScriptWorkerEventKind.ResumeAcknowledged);
        Assert.Equal(3, resumed.RequestSequence);
        Assert.Equal(ScriptWorkerState.Running, resumed.State);

        engine.Complete(ScriptExecutionStatus.Completed);
        var completed = await ReadUntilAsync(events, ScriptWorkerEventKind.Completed);
        Assert.Equal(runId, completed.RunId);
        Assert.Equal(ScriptWorkerState.Completed, worker.State);

        await worker.StopAsync();
    }

    [Fact]
    public async Task Cancel_WaitsForExecutionTerminalResult()
    {
        var engine = new ControllableExecutionEngine();
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var events = worker.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
        var nextEventTask = events.MoveNextAsync().AsTask();
        var runId = Guid.NewGuid();

        worker.TryPostCommand(CreateCommand(
            ScriptWorkerCommandKind.Start,
            runId,
            1,
            new ScriptWorkerStartRequest("cancel-test.json", CreateOptions())));
        _ = await ReadNextAsync(events, nextEventTask);

        worker.TryPostCommand(CreateCommand(ScriptWorkerCommandKind.Cancel, runId, 2));
        var cancelled = await ReadUntilAsync(events, ScriptWorkerEventKind.Cancelled);

        Assert.Equal(runId, cancelled.RunId);
        Assert.True(engine.RequestStopCallCount > 0);
        Assert.Equal(ScriptWorkerState.Cancelled, worker.State);

        await worker.StopAsync();
    }

    [Fact]
    public async Task StaleAndDuplicateCommands_AreIgnoredWithoutInterruptingCurrentRun()
    {
        var engine = new ControllableExecutionEngine();
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var events = worker.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
        var nextEventTask = events.MoveNextAsync().AsTask();
        var runId = Guid.NewGuid();

        worker.TryPostCommand(CreateCommand(
            ScriptWorkerCommandKind.Start,
            runId,
            5,
            new ScriptWorkerStartRequest("stable-test.json", CreateOptions())));
        _ = await ReadNextAsync(events, nextEventTask);

        worker.TryPostCommand(CreateCommand(ScriptWorkerCommandKind.Pause, Guid.NewGuid(), 6));
        worker.TryPostCommand(CreateCommand(ScriptWorkerCommandKind.Pause, runId, 5));
        worker.TryPostCommand(CreateCommand(
            ScriptWorkerCommandKind.Start,
            Guid.NewGuid(),
            7,
            new ScriptWorkerStartRequest("duplicate.json", CreateOptions())));
        engine.Complete(ScriptExecutionStatus.Completed);

        _ = await ReadUntilAsync(events, ScriptWorkerEventKind.Completed);
        Assert.Equal(0, engine.RequestPauseCallCount);
        Assert.Equal(1, engine.ExecuteCallCount);

        await worker.StopAsync();
    }

    [Fact]
    public async Task SynchronouslyCompletedExecution_PublishesStartedBeforeCompleted()
    {
        var engine = new ControllableExecutionEngine { CompleteSynchronously = true };
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var events = worker.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
        var nextEventTask = events.MoveNextAsync().AsTask();
        var runId = Guid.NewGuid();

        worker.TryPostCommand(CreateCommand(
            ScriptWorkerCommandKind.Start,
            runId,
            1,
            new ScriptWorkerStartRequest("empty-test.json", CreateOptions())));

        Assert.Equal(ScriptWorkerEventKind.Started, (await ReadNextAsync(events, nextEventTask)).Kind);
        Assert.Equal(ScriptWorkerEventKind.Completed, (await ReadUntilAsync(events, ScriptWorkerEventKind.Completed)).Kind);

        await worker.StopAsync();
    }

    [Fact]
    public async Task StartCommand_CarriesAmbientControlContextsIntoExecution()
    {
        var engine = new ControllableExecutionEngine { CompleteSynchronously = true };
        var worker = new ScriptTaskFlowWorker(engine, TimeProvider.System);
        var arbiter = new GameInputArbiter();
        Assert.True(arbiter.TryAcquire("auto-task", GameInputPriority.Navigation, out var navigationLease));
        var inputContext = new InputArbiterContextState(arbiter, navigationLease);
        var runId = Guid.NewGuid();

        using (GameControlLeaseContext.Push("auto-task-owner"))
        using (GameInputArbiterContext.Push(inputContext))
        {
            Assert.True(worker.TryPostCommand(CreateCommand(
                ScriptWorkerCommandKind.Start,
                runId,
                1,
                new ScriptWorkerStartRequest("context.json", CreateOptions()))));
        }

        await WaitUntilAsync(() => worker.State == ScriptWorkerState.Completed);

        Assert.Equal("auto-task-owner", engine.CapturedGameControlOwnerId);
        Assert.Same(inputContext, engine.CapturedInputContext);
        navigationLease.Dispose();
        await worker.StopAsync();
    }

    private static ScriptExecutionOptions CreateOptions()
    {
        return new ScriptExecutionOptions
        {
            RequireCaptureService = false,
            RequireTargetWindow = false
        };
    }

    private static ScriptWorkerCommand CreateCommand(
        ScriptWorkerCommandKind kind,
        Guid runId,
        long sequence,
        ScriptWorkerStartRequest? startRequest = null)
    {
        return new ScriptWorkerCommand(
            kind,
            runId,
            sequence,
            "test command",
            CancellationToken.None,
            waitForAcknowledgement: kind is ScriptWorkerCommandKind.Pause or ScriptWorkerCommandKind.Resume,
            startRequest);
    }

    private static async Task<ScriptWorkerEvent> ReadNextAsync(
        IAsyncEnumerator<ScriptWorkerEvent> events,
        Task<bool> moveNextTask)
    {
        Assert.True(await moveNextTask);
        return events.Current;
    }

    private static async Task<ScriptWorkerEvent> ReadUntilAsync(
        IAsyncEnumerator<ScriptWorkerEvent> events,
        ScriptWorkerEventKind expectedKind)
    {
        while (await events.MoveNextAsync())
        {
            if (events.Current.Kind == expectedKind)
            {
                return events.Current;
            }
        }

        throw new Xunit.Sdk.XunitException($"Worker event '{expectedKind}' was not published.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeoutAt)
                throw new TimeoutException("Expected worker state was not reached.");
            await Task.Delay(10);
        }
    }

    private sealed class ControllableExecutionEngine : IScriptTaskFlowExecutionEngine
    {
        private TaskCompletionSource<ScriptExecutionResult> _completionSource = CreateCompletionSource();
        private CancellationTokenRegistration _cancellationRegistration;

        public bool IsRunning { get; private set; }

        public int ExecuteCallCount { get; private set; }

        public int RequestPauseCallCount { get; private set; }

        public int RequestStopCallCount { get; private set; }

        public bool CompleteSynchronously { get; init; }

        public string? CapturedGameControlOwnerId { get; private set; }

        public InputArbiterContextState? CapturedInputContext { get; private set; }

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public bool RequestPause()
        {
            RequestPauseCallCount++;
            if (!IsRunning)
            {
                return false;
            }

            Publish(ScriptExecutionRunState.PauseRequested, "PauseRequested");
            Publish(ScriptExecutionRunState.Paused, "Paused");
            return true;
        }

        public bool Resume()
        {
            if (!IsRunning)
            {
                return false;
            }

            Publish(ScriptExecutionRunState.Running, "Resumed");
            return true;
        }

        public bool RequestStop()
        {
            RequestStopCallCount++;
            if (!IsRunning)
            {
                return false;
            }

            Complete(ScriptExecutionStatus.Cancelled);
            return true;
        }

        public Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            CapturedGameControlOwnerId = GameControlLeaseContext.CurrentOwnerId;
            CapturedInputContext = GameInputArbiterContext.Current;
            IsRunning = true;
            _completionSource = CreateCompletionSource();
            _cancellationRegistration = cancellationToken.Register(
                () => Complete(ScriptExecutionStatus.Cancelled));
            if (CompleteSynchronously)
            {
                Complete(ScriptExecutionStatus.Completed);
            }

            return _completionSource.Task;
        }

        public void Complete(ScriptExecutionStatus status)
        {
            IsRunning = false;
            _completionSource.TrySetResult(new ScriptExecutionResult
            {
                Status = status,
                ExecutedStepCount = 1,
                LastCompletedStepIndex = 0,
                FinalProgress = new ScriptExecutionProgressSnapshot
                {
                    RunState = status switch
                    {
                        ScriptExecutionStatus.Completed => ScriptExecutionRunState.Completed,
                        ScriptExecutionStatus.Cancelled => ScriptExecutionRunState.Cancelled,
                        _ => ScriptExecutionRunState.Failed
                    },
                    CurrentStepIndex = 0,
                    LastCompletedStepIndex = 0,
                    CurrentCheckpoint = status.ToString(),
                    Message = status.ToString()
                }
            });
            _cancellationRegistration.Dispose();
        }

        private void Publish(ScriptExecutionRunState runState, string checkpoint)
        {
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                RunState = runState,
                CurrentStepIndex = 0,
                LastCompletedStepIndex = -1,
                CurrentCheckpoint = checkpoint,
                Message = checkpoint
            });
        }

        private static TaskCompletionSource<ScriptExecutionResult> CreateCompletionSource()
        {
            return new TaskCompletionSource<ScriptExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
