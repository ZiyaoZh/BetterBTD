using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskStuckRecoveryTests
{
    [Fact]
    public void Tracker_IncludesUnknownAndResetsWhenVisualInterfaceChanges()
    {
        var tracker = new AutoTaskStuckUiTracker(TimeSpan.FromSeconds(10));
        var startedAt = DateTimeOffset.UtcNow;

        Assert.False(tracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt, 0x10), AutoTaskPhase.PreparingStage, 0));
        Assert.False(tracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(9), 0x10), AutoTaskPhase.PreparingStage, 0));
        Assert.False(tracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), ulong.MaxValue), AutoTaskPhase.PreparingStage, 0));
        Assert.True(tracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(20), ulong.MaxValue), AutoTaskPhase.PreparingStage, 0));
    }

    [Fact]
    public void Tracker_ExemptsInLevelAndClearsPreviousObservation()
    {
        var tracker = new AutoTaskStuckUiTracker(TimeSpan.FromSeconds(10));
        var startedAt = DateTimeOffset.UtcNow;

        Assert.False(tracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt, 1), AutoTaskPhase.PreparingStage, 0));
        Assert.False(tracker.Observe(CreateSnapshot(GameUiStateId.InLevel, startedAt.AddSeconds(20), 1), AutoTaskPhase.ExecutingScript, 0));
        Assert.False(tracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(30), 1), AutoTaskPhase.PreparingStage, 0));
    }

    [Fact]
    public async Task Runner_RecoversUnknownWhenFirstClickChangesInterface()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var uiState = new QueueUiStateService(
        [
            CreateSnapshot(GameUiStateId.Unknown, startedAt, 1),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), 1),
            CreateSnapshot(GameUiStateId.MainMenu, startedAt.AddSeconds(11), 2)
        ]);
        var recovery = new RecordingRecoveryExecutor();
        var artifactWriter = new RecordingFailureArtifactWriter(throwOnWrite: false);
        var runner = CreateRunner(uiState, recovery, artifactWriter);

        var result = await runner.ExecuteAsync(
            CreateRequest(),
            CreateOptions(CreateRuntimeServices(uiState, recovery, artifactWriter)));

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Equal([new GameUiRecoveryPoint(100, 200)], recovery.ClickedPoints);
        Assert.Equal(0, artifactWriter.WriteCount);
    }

    [Fact]
    public async Task Runner_FailsUnknownAfterAllRecoveryClicksLeaveInterfaceUnchanged()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var uiState = new QueueUiStateService(
        [
            CreateSnapshot(GameUiStateId.Unknown, startedAt, 1),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), 1),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(11), 1),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(12), 1)
        ]);
        var recovery = new RecordingRecoveryExecutor();
        var runner = CreateRunner(uiState, recovery);

        var result = await runner.ExecuteAsync(
            CreateRequest(),
            CreateOptions(CreateRuntimeServices(uiState, recovery)));

        Assert.Equal(AutoTaskExecutionStatus.Failed, result.Status);
        Assert.Equal("StuckUiRecovery", result.Failure?.Checkpoint);
        Assert.Equal(GameUiStateId.Unknown, result.Failure?.UiState);
        Assert.Equal(2, result.Failure?.Attempt);
        Assert.Equal(
            [new GameUiRecoveryPoint(100, 200), new GameUiRecoveryPoint(300, 400)],
            recovery.ClickedPoints);
    }

    [Fact]
    public async Task Runner_FailsLoadingWithoutRecoveryClicks()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var uiState = new QueueUiStateService(
        [
            CreateSnapshot(GameUiStateId.Loading, startedAt, 1),
            CreateSnapshot(GameUiStateId.Loading, startedAt.AddSeconds(10), 1)
        ]);
        var recovery = new RecordingRecoveryExecutor();
        var artifactWriter = new RecordingFailureArtifactWriter(throwOnWrite: true);
        var runner = CreateRunner(uiState, recovery, artifactWriter);

        var result = await runner.ExecuteAsync(
            CreateRequest(),
            CreateOptions(CreateRuntimeServices(uiState, recovery, artifactWriter)));

        Assert.Equal(AutoTaskExecutionStatus.Failed, result.Status);
        Assert.Equal("StuckUiRecovery", result.Failure?.Checkpoint);
        Assert.Empty(recovery.ClickedPoints);
        Assert.Equal(1, artifactWriter.WriteCount);
    }

    private static AutoTaskRunner CreateRunner(
        IGameUiStateService uiState,
        IGameUiStuckRecoveryExecutor recovery,
        IAutoTaskFailureArtifactWriter? failureArtifactWriter = null)
    {
        var runtimeServices = CreateRuntimeServices(uiState, recovery, failureArtifactWriter);
        return new AutoTaskRunner(
            new SingleStrategyRegistry(new RecoveryTestStrategy()),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);
    }

    private static AutoTaskRuntimeServices CreateRuntimeServices(
        IGameUiStateService uiState,
        IGameUiStuckRecoveryExecutor recovery,
        IAutoTaskFailureArtifactWriter? failureArtifactWriter = null)
    {
        return new AutoTaskRuntimeServices
        {
            GameUiState = uiState,
            Navigator = new NoOpNavigator(),
            UiActionExecutor = new NoOpActionExecutor(),
            StuckRecoveryExecutor = recovery,
            FailureArtifactWriter = failureArtifactWriter,
            ScriptResolver = new UnusedScriptResolver(),
            ScriptExecutor = new UnusedScriptExecutor()
        };
    }

    private sealed class RecordingFailureArtifactWriter(bool throwOnWrite) : IAutoTaskFailureArtifactWriter
    {
        public int WriteCount { get; private set; }

        public Task WriteAsync(
            AutoTaskExecutionResult result,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            Assert.Equal(AutoTaskExecutionStatus.Failed, result.Status);
            if (throwOnWrite)
            {
                throw new IOException("Simulated artifact write failure.");
            }

            return Task.CompletedTask;
        }
    }

    private static AutoTaskExecutionOptions CreateOptions(AutoTaskRuntimeServices runtimeServices)
    {
        return new AutoTaskExecutionOptions
        {
            RuntimeServices = runtimeServices,
            MaxLoopIterations = 5,
            StuckUiTimeout = TimeSpan.FromSeconds(10),
            StuckRecoveryDelayMs = 0,
            StuckRecoveryPoints = [new(100, 200), new(300, 400)]
        };
    }

    private static AutoTaskRequest CreateRequest()
    {
        return new AutoTaskRequest
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = new StageEntryTarget
            {
                Map = GameMapType.MonkeyMeadow,
                Difficulty = StageDifficulty.Easy,
                Mode = StageMode.Standard
            }
        };
    }

    private static GameUiSnapshot CreateSnapshot(
        GameUiStateId state,
        DateTimeOffset capturedAt,
        ulong fingerprint)
    {
        return new GameUiSnapshot
        {
            State = state,
            CapturedAt = capturedAt,
            VisualFingerprint = fingerprint
        };
    }

    private sealed class QueueUiStateService(IEnumerable<GameUiSnapshot> snapshots) : IGameUiStateService
    {
        private readonly Queue<GameUiSnapshot> _snapshots = new(snapshots);
        private GameUiSnapshot? _lastSnapshot;

        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_snapshots.Count > 0)
            {
                _lastSnapshot = _snapshots.Dequeue();
            }

            return Task.FromResult(_lastSnapshot ?? new GameUiSnapshot());
        }

        public void ResetStabilizationState()
        {
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

    private sealed class RecoveryTestStrategy : IAutoTaskStrategy
    {
        public AutoTaskKind Kind => AutoTaskKind.Collection;

        public Task<AutoTaskDecision> DecideNextAsync(
            AutoTaskRuntimeState state,
            GameUiSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot.State == GameUiStateId.MainMenu
                ? AutoTaskDecision.Complete("Recovered to the main menu.")
                : AutoTaskDecision.Wait("Waiting for UI progress.", 1));
        }
    }

    private sealed class SingleStrategyRegistry(IAutoTaskStrategy strategy) : IAutoTaskStrategyRegistry
    {
        public IAutoTaskStrategy GetRequiredStrategy(AutoTaskKind kind)
        {
            Assert.Equal(strategy.Kind, kind);
            return strategy;
        }
    }

    private sealed class NoOpNavigator : IGameUiNavigator
    {
        public GameUiNavigationStep GetNextStep(StageEntryTarget target, GameUiSnapshot snapshot)
        {
            return new GameUiNavigationStep { ActionKind = GameUiActionKind.Wait };
        }
    }

    private sealed class NoOpActionExecutor : IGameUiActionExecutor
    {
        public Task<GameUiActionExecutionResult> ExecuteAsync(
            GameUiNavigationStep step,
            AutoTaskRuntimeState state,
            GameUiSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The recovery test must not execute normal navigation actions.");
        }
    }

    private sealed class UnusedScriptResolver : IAutoTaskScriptResolver
    {
        public Task<AutoTaskScriptResolution> ResolveAsync(
            AutoTaskScriptQuery query,
            AutoTaskRuntimeState state,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The recovery test must not resolve scripts.");
        }
    }

    private sealed class UnusedScriptExecutor : IAutoTaskScriptExecutor
    {
        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public bool IsRunning => false;

        public bool RequestPause() => false;

        public bool Resume() => false;

        public Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            _ = ProgressChanged;
            throw new InvalidOperationException("The recovery test must not execute scripts.");
        }
    }
}
