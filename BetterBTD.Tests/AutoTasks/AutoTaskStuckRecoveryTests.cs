using System.Text.Json;
using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;
using BetterBTD.Services.Settings;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskStuckRecoveryTests
{
    [Fact]
    public void Configuration_JsonRoundTripPreservesRecoveryPointOrderAndDefaultsMissingFields()
    {
        var configured = new AppConfiguration
        {
            AutoTaskMaxConsecutiveNavigationFailures = 4,
            AutoTaskStuckUiTimeoutSeconds = 25,
            AutoTaskVisualFingerprintDistanceTolerance = 8,
            AutoTaskStuckRecoveryDelayMs = 1500,
            AutoTaskStuckRecoveryPoints = [new(12, 34), new(56, 78)]
        };

        var roundTripped = JsonSerializer.Deserialize<AppConfiguration>(JsonSerializer.Serialize(configured));
        var defaults = JsonSerializer.Deserialize<AppConfiguration>("{}");

        Assert.NotNull(roundTripped);
        Assert.Equal(4, roundTripped.AutoTaskMaxConsecutiveNavigationFailures);
        Assert.Equal(25, roundTripped.AutoTaskStuckUiTimeoutSeconds);
        Assert.Equal(8, roundTripped.AutoTaskVisualFingerprintDistanceTolerance);
        Assert.Equal(1500, roundTripped.AutoTaskStuckRecoveryDelayMs);
        Assert.Equal([new GameUiRecoveryPoint(12, 34), new GameUiRecoveryPoint(56, 78)], roundTripped.AutoTaskStuckRecoveryPoints);
        Assert.NotNull(defaults);
        Assert.Equal(10, defaults.AutoTaskStuckUiTimeoutSeconds);
        Assert.Equal(6, defaults.AutoTaskVisualFingerprintDistanceTolerance);
        Assert.Equal(
            [
                new GameUiRecoveryPoint(960, 840),
                new GameUiRecoveryPoint(960, 760),
                new GameUiRecoveryPoint(1340, 850),
                new GameUiRecoveryPoint(850, 810),
                new GameUiRecoveryPoint(780, 730),
                new GameUiRecoveryPoint(1140, 730),
                new GameUiRecoveryPoint(80, 55)
            ],
            defaults.AutoTaskStuckRecoveryPoints);
    }

    [Fact]
    public void Configuration_SaveAndReloadPersistsRecoverySettingsIncludingEmptyPointList()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BetterBTD.Tests.{Guid.NewGuid():N}");
        var configPath = Path.Combine(directory, "appsettings.json");

        try
        {
            var writer = new ConfigurationService(configPath);
            writer.Save(new AppConfiguration
            {
                AutoTaskMaxConsecutiveNavigationFailures = 2,
                AutoTaskStuckUiTimeoutSeconds = 31,
                AutoTaskVisualFingerprintDistanceTolerance = 4,
                AutoTaskStuckRecoveryDelayMs = 975,
                AutoTaskStuckRecoveryPoints = []
            });

            var reloaded = new ConfigurationService(configPath).Current;

            Assert.Equal(2, reloaded.AutoTaskMaxConsecutiveNavigationFailures);
            Assert.Equal(31, reloaded.AutoTaskStuckUiTimeoutSeconds);
            Assert.Equal(4, reloaded.AutoTaskVisualFingerprintDistanceTolerance);
            Assert.Equal(975, reloaded.AutoTaskStuckRecoveryDelayMs);
            Assert.Empty(reloaded.AutoTaskStuckRecoveryPoints);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Configuration_LoadMigratesLegacyDefaultRecoveryPoints()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BetterBTD.Tests.{Guid.NewGuid():N}");
        var configPath = Path.Combine(directory, "appsettings.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                configPath,
                """
                {
                  "AutoTaskStuckRecoveryPoints": [
                    { "X": 960, "Y": 840 },
                    { "X": 960, "Y": 760 },
                    { "X": 1340, "Y": 850 },
                    { "X": 850, "Y": 810 },
                    { "X": 80, "Y": 55 }
                  ]
                }
                """);

            var loaded = new ConfigurationService(configPath).Current;

            Assert.Equal(
                AutoTaskExecutionOptions.CreateDefaultStuckRecoveryPoints(),
                loaded.AutoTaskStuckRecoveryPoints);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Configuration_LoadPreservesCustomizedRecoveryPoints()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BetterBTD.Tests.{Guid.NewGuid():N}");
        var configPath = Path.Combine(directory, "appsettings.json");

        try
        {
            var writer = new ConfigurationService(configPath);
            writer.Save(new AppConfiguration
            {
                AutoTaskStuckRecoveryPoints = [new(960, 840), new(80, 55)]
            });

            var loaded = new ConfigurationService(configPath).Current;

            Assert.Equal(
                [new GameUiRecoveryPoint(960, 840), new GameUiRecoveryPoint(80, 55)],
                loaded.AutoTaskStuckRecoveryPoints);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Configuration_NormalizesRecoveryValuesAndPreservesEmptyPointList()
    {
        Assert.Equal(5, ConfigurationService.NormalizeAutoTaskNavigationFailureLimit(0));
        Assert.Equal(20, ConfigurationService.NormalizeAutoTaskNavigationFailureLimit(99));
        Assert.Equal(10, ConfigurationService.NormalizeAutoTaskStuckTimeoutSeconds(0));
        Assert.Equal(300, ConfigurationService.NormalizeAutoTaskStuckTimeoutSeconds(999));
        Assert.Equal(0, ConfigurationService.NormalizeAutoTaskVisualFingerprintDistanceTolerance(-1));
        Assert.Equal(64, ConfigurationService.NormalizeAutoTaskVisualFingerprintDistanceTolerance(99));
        Assert.Equal(0, ConfigurationService.NormalizeAutoTaskRecoveryDelay(-1));
        Assert.Equal(10000, ConfigurationService.NormalizeAutoTaskRecoveryDelay(20000));

        Assert.Empty(ConfigurationService.NormalizeAutoTaskRecoveryPoints([]));
        Assert.Equal(
            [new GameUiRecoveryPoint(0, 1079), new GameUiRecoveryPoint(1919, 0)],
            ConfigurationService.NormalizeAutoTaskRecoveryPoints([new(-10, 2000), new(3000, -5)]));
        Assert.Equal(
            AutoTaskExecutionOptions.CreateDefaultStuckRecoveryPoints(),
            ConfigurationService.NormalizeAutoTaskRecoveryPoints(null));
    }

    [Fact]
    public void Configuration_MapsPersistedRecoverySettingsToExecutionSnapshot()
    {
        var configuration = ConfigurationService.Instance.Current;
        var originalFailureLimit = configuration.AutoTaskMaxConsecutiveNavigationFailures;
        var originalTimeout = configuration.AutoTaskStuckUiTimeoutSeconds;
        var originalVisualTolerance = configuration.AutoTaskVisualFingerprintDistanceTolerance;
        var originalDelay = configuration.AutoTaskStuckRecoveryDelayMs;
        var originalPoints = configuration.AutoTaskStuckRecoveryPoints;

        try
        {
            configuration.AutoTaskMaxConsecutiveNavigationFailures = 3;
            configuration.AutoTaskStuckUiTimeoutSeconds = 17;
            configuration.AutoTaskVisualFingerprintDistanceTolerance = 9;
            configuration.AutoTaskStuckRecoveryDelayMs = 1250;
            configuration.AutoTaskStuckRecoveryPoints = [new(100, 200), new(300, 400)];

            var options = ConfigurationService.Instance.GetAutoTaskExecutionOptions();

            Assert.Equal(3, options.MaxConsecutiveNavigationFailures);
            Assert.Equal(TimeSpan.FromSeconds(17), options.StuckUiTimeout);
            Assert.Equal(9, options.VisualFingerprintDistanceTolerance);
            Assert.Equal(1250, options.StuckRecoveryDelayMs);
            Assert.Equal([new GameUiRecoveryPoint(100, 200), new GameUiRecoveryPoint(300, 400)], options.StuckRecoveryPoints);

            configuration.AutoTaskStuckRecoveryPoints[0] = new GameUiRecoveryPoint(900, 900);
            Assert.Equal(new GameUiRecoveryPoint(100, 200), options.StuckRecoveryPoints[0]);
        }
        finally
        {
            configuration.AutoTaskMaxConsecutiveNavigationFailures = originalFailureLimit;
            configuration.AutoTaskStuckUiTimeoutSeconds = originalTimeout;
            configuration.AutoTaskVisualFingerprintDistanceTolerance = originalVisualTolerance;
            configuration.AutoTaskStuckRecoveryDelayMs = originalDelay;
            configuration.AutoTaskStuckRecoveryPoints = originalPoints;
        }
    }

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
    public void Tracker_UsesConfiguredVisualFingerprintDistanceTolerance()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sevenChangedBits = 0b111_1111UL;
        var strictTracker = new AutoTaskStuckUiTracker(TimeSpan.FromSeconds(10), 6);
        var tolerantTracker = new AutoTaskStuckUiTracker(TimeSpan.FromSeconds(10), 7);

        Assert.False(strictTracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt, 0), AutoTaskPhase.PreparingStage, 0));
        Assert.False(tolerantTracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt, 0), AutoTaskPhase.PreparingStage, 0));

        Assert.False(strictTracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), sevenChangedBits), AutoTaskPhase.PreparingStage, 0));
        Assert.True(tolerantTracker.Observe(CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), sevenChangedBits), AutoTaskPhase.PreparingStage, 0));
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
    public async Task Runner_RecoversWhenFirstClickChangesVisualFingerprintWithoutChangingUiState()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var uiState = new QueueUiStateService(
        [
            CreateSnapshot(GameUiStateId.Unknown, startedAt, 0),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), 0),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(11), ulong.MaxValue),
            CreateSnapshot(GameUiStateId.MainMenu, startedAt.AddSeconds(12), ulong.MaxValue)
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
    public async Task Runner_ContinuesRecoveryWhenUiStateChangesWithoutVisualFingerprintChange()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var uiState = new QueueUiStateService(
        [
            CreateSnapshot(GameUiStateId.Unknown, startedAt, 0),
            CreateSnapshot(GameUiStateId.Unknown, startedAt.AddSeconds(10), 0),
            CreateSnapshot(GameUiStateId.MainMenu, startedAt.AddSeconds(11), 0),
            CreateSnapshot(GameUiStateId.MainMenu, startedAt.AddSeconds(12), ulong.MaxValue)
        ]);
        var recovery = new RecordingRecoveryExecutor();
        var runner = CreateRunner(uiState, recovery);

        var result = await runner.ExecuteAsync(
            CreateRequest(),
            CreateOptions(CreateRuntimeServices(uiState, recovery)));

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Equal(
            [new GameUiRecoveryPoint(100, 200), new GameUiRecoveryPoint(300, 400)],
            recovery.ClickedPoints);
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
