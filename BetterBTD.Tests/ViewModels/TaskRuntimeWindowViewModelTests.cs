using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ViewModels;

public sealed class TaskRuntimeWindowViewModelTests
{
    [Fact]
    public void ApplyProgressSnapshot_MapsCompletedStageCountAndFormatsLongRuntimeDuration()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 14, 32, 8, TimeSpan.Zero));
        using var viewModel = CreateViewModel(timeProvider);

        Start(viewModel);
        viewModel.ApplyProgressSnapshot(CreateProgressSnapshot(
            timeProvider.GetUtcNow() - new TimeSpan(25, 2, 3),
            completedStageCount: 128));

        Assert.Equal("128", viewModel.CompletedStageCountText);
        Assert.Equal("25:02:03", viewModel.RuntimeDurationText);
    }

    [Fact]
    public async Task StartExecutionAgain_ResetsRuntimeMetrics()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 14, 32, 8, TimeSpan.Zero));
        var startCount = 0;
        using var viewModel = CreateViewModel(
            timeProvider,
            viewModel =>
            {
                startCount++;
                if (startCount == 1)
                {
                    viewModel.ApplyResult(CreateResult(CreateProgressSnapshot(timeProvider.GetUtcNow(), completedStageCount: 128)));
                }

                return Task.CompletedTask;
            });

        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.StartCommand.Execute(null);

        Assert.Equal(2, startCount);
        Assert.Equal(LocalizationService.Instance.T("Tasks.Runtime.Metrics.NotStarted"), viewModel.CompletedStageCountText);
        Assert.Equal("00:00:00", viewModel.RuntimeDurationText);

        viewModel.ApplyResult(CreateResult(CreateProgressSnapshot(timeProvider.GetUtcNow(), completedStageCount: 0)));
    }

    [Fact]
    public void ApplyResult_FreezesRuntimeDuration()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 14, 32, 8, TimeSpan.Zero));
        using var viewModel = CreateViewModel(timeProvider);

        Start(viewModel);
        viewModel.ApplyProgressSnapshot(CreateProgressSnapshot(timeProvider.GetUtcNow(), completedStageCount: 7));
        timeProvider.Advance(TimeSpan.FromMinutes(3));
        viewModel.ApplyResult(CreateResult(CreateProgressSnapshot(timeProvider.GetUtcNow() - TimeSpan.FromMinutes(3), completedStageCount: 8)));

        Assert.Equal("00:03:00", viewModel.RuntimeDurationText);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        viewModel.ApplyUnexpectedException(new InvalidOperationException("ignored after completion"));

        Assert.Equal("00:03:00", viewModel.RuntimeDurationText);
    }

    [Fact]
    public void ApplyProgressSnapshot_ReplacesStageActionStatusWithoutAccumulatingHistory()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var viewModel = CreateViewModel(timeProvider);
        Start(viewModel);

        for (var index = 0; index < 200; index++)
        {
            var snapshot = CreateProgressSnapshot(timeProvider.GetUtcNow(), completedStageCount: 0);
            snapshot.Phase = index == 199
                ? AutoTaskPhase.SettlingResult
                : AutoTaskPhase.NavigatingToStage;
            snapshot.CurrentActivity = index == 199
                ? AutoTaskActivityKind.HandlingResult
                : AutoTaskActivityKind.Navigating;
            viewModel.ApplyProgressSnapshot(snapshot);
        }

        Assert.Equal(
            LocalizationService.Instance.T("Tasks.Runtime.Phase.SettlingResult"),
            viewModel.CurrentPhaseText);
        Assert.Equal(
            LocalizationService.Instance.T("Tasks.Runtime.Activity.HandlingResult"),
            viewModel.CurrentActivityText);
        Assert.Equal(
            LocalizationService.Instance.T("Tasks.Runtime.ActivityStatus.Running"),
            viewModel.ActivityStatusText);
    }

    [Fact]
    public void ApplyProgressSnapshot_DisplaysOnlyRelevantInLevelInformation()
    {
        using var viewModel = CreateViewModel(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var snapshot = CreateProgressSnapshot(DateTimeOffset.UtcNow, completedStageCount: 0);
        snapshot.CurrentCheckpoint = "CaptureUiState";
        snapshot.ActiveScriptPath = @"C:\scripts\internal.json";
        snapshot.Message = "Internal runner message";
        snapshot.LastUiSnapshot = new GameUiSnapshot
        {
            State = GameUiStateId.InLevel,
            Confidence = 0.95,
            Summary = "Internal recognition summary",
            StageState = new GameStageStateSnapshot
            {
                IsInLevel = true,
                Gold = 1234,
                Round = 45,
                LeftUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = false,
                    TopPathLevel = 9
                },
                RightUpgradePanel = new GameStageUpgradePanelState
                {
                    IsVisible = true,
                    TopPathLevel = 2,
                    MiddlePathLevel = 1,
                    BottomPathLevel = 0
                }
            }
        };

        viewModel.ApplyProgressSnapshot(snapshot);

        Assert.Contains(LocalizationService.Instance.T("CaptureTest.GameUiState.InLevel"), viewModel.StatusText);
        Assert.Contains("1234", viewModel.StatusText);
        Assert.Contains("45", viewModel.StatusText);
        Assert.Contains(LocalizationService.Instance.T("Tasks.Runtime.Status.RightUpgrade"), viewModel.StatusText);
        Assert.DoesNotContain(LocalizationService.Instance.T("Tasks.Runtime.Status.LeftUpgrade"), viewModel.StatusText);
        Assert.DoesNotContain("CaptureUiState", viewModel.StatusText);
        Assert.DoesNotContain("internal.json", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Internal runner message", viewModel.StatusText);
        Assert.DoesNotContain("Internal recognition summary", viewModel.StatusText);
        Assert.DoesNotContain("95%", viewModel.StatusText);
    }

    [Fact]
    public void ApplyProgressSnapshot_MapSearchDisplaysOnlyConfirmedMap()
    {
        using var viewModel = CreateViewModel(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var snapshot = CreateProgressSnapshot(DateTimeOffset.UtcNow, completedStageCount: 0);
        snapshot.LastUiSnapshot = new GameUiSnapshot
        {
            State = GameUiStateId.MapSearch,
            Summary = "debug candidate score 99%",
            Facts = new Dictionary<string, object?>
            {
                [MapSearchFlowState.CollectionMapFact] = GameMapType.MonkeyMeadow,
                [MapSearchFlowState.CollectionMapMatchesFact] = "debug candidates"
            }
        };

        viewModel.ApplyProgressSnapshot(snapshot);

        Assert.Contains(GameElementCatalog.GetMapDisplayName(GameMapType.MonkeyMeadow), viewModel.StatusText);
        Assert.DoesNotContain("debug", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("99%", viewModel.StatusText);
    }

    [Fact]
    public void ApplyProgressSnapshot_DisplaysNavigationRetryUntilRecovery()
    {
        using var viewModel = CreateViewModel(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var failedSnapshot = CreateProgressSnapshot(DateTimeOffset.UtcNow, completedStageCount: 0);
        failedSnapshot.CurrentActivity = AutoTaskActivityKind.Navigating;
        failedSnapshot.ConsecutiveNavigationFailures = 1;
        failedSnapshot.Message = "Map button was not found.";
        viewModel.ApplyProgressSnapshot(failedSnapshot);

        var captureSnapshot = CreateProgressSnapshot(DateTimeOffset.UtcNow, completedStageCount: 0);
        captureSnapshot.CurrentActivity = AutoTaskActivityKind.CapturingUi;
        captureSnapshot.ConsecutiveNavigationFailures = 1;
        captureSnapshot.Message = "Capturing current UI.";
        viewModel.ApplyProgressSnapshot(captureSnapshot);

        Assert.Equal(
            string.Format(
                LocalizationService.Instance.T("Tasks.Runtime.ActivityStatus.NavigationRetry"),
                1),
            viewModel.ActivityStatusText);

        captureSnapshot.ConsecutiveNavigationFailures = 0;
        viewModel.ApplyProgressSnapshot(captureSnapshot);

        Assert.Equal(
            LocalizationService.Instance.T("Tasks.Runtime.ActivityStatus.Running"),
            viewModel.ActivityStatusText);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void RuntimeEnumDisplayResources_CoverEverySupportedValue(string languageCode)
    {
        var localization = LocalizationService.Instance;
        var previousLanguage = localization.LanguageCode;

        try
        {
            localization.SetLanguage(languageCode);

            foreach (var phase in Enum.GetValues<AutoTaskPhase>())
            {
                AssertLocalized(localization, $"Tasks.Runtime.Phase.{phase}");
            }

            foreach (var activity in Enum.GetValues<AutoTaskActivityKind>())
            {
                AssertLocalized(localization, $"Tasks.Runtime.Activity.{activity}");
            }

            foreach (var uiState in Enum.GetValues<GameUiStateId>())
            {
                AssertLocalized(localization, $"CaptureTest.GameUiState.{uiState}");
            }
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }
    }

    private static void AssertLocalized(LocalizationService localization, string key)
    {
        Assert.NotEqual(key, localization.T(key));
    }

    private static TaskRuntimeWindowViewModel CreateViewModel(
        TimeProvider timeProvider,
        Func<TaskRuntimeWindowViewModel, Task>? startExecutionAsync = null)
    {
        return new TaskRuntimeWindowViewModel(
            LocalizationService.Instance,
            "Test Task",
            "Test task summary",
            operationIntervalMs: 200,
            startExecutionAsync: startExecutionAsync ?? (_ => Task.CompletedTask),
            requestStop: () => { },
            timeProvider: timeProvider);
    }

    private static void Start(TaskRuntimeWindowViewModel viewModel)
    {
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.IsRunning);
    }

    private static AutoTaskProgressSnapshot CreateProgressSnapshot(DateTimeOffset startedAt, int completedStageCount)
    {
        return new AutoTaskProgressSnapshot
        {
            RunState = AutoTaskRunState.Running,
            StartedAt = startedAt,
            LoopIteration = 10000,
            CompletedStageCount = completedStageCount,
            Message = "Running"
        };
    }

    private static AutoTaskExecutionResult CreateResult(AutoTaskProgressSnapshot finalProgress)
    {
        return new AutoTaskExecutionResult
        {
            Status = AutoTaskExecutionStatus.Completed,
            FinalProgress = finalProgress
        };
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan elapsed)
        {
            _utcNow += elapsed;
        }
    }
}
