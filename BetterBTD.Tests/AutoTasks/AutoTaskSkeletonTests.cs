using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Core.Simulator;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.AutoTasks;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using BetterBTD.Tests.TestDoubles;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskSkeletonTests
{
    [Fact]
    public void AutoTaskExecutionOptions_HasNoDefaultLoopLimit()
    {
        var options = new AutoTaskExecutionOptions();

        Assert.Null(options.MaxLoopIterations);
        Assert.Equal(TimeSpan.FromSeconds(5), options.WorkerAcknowledgementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ScriptOffLevelGracePeriod);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ScriptRecoveryTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(800), options.ScriptRecoveryClickInterval);
        Assert.Equal(new GameUiRecoveryPoint(960, 540), options.ScriptRecoveryPoint);
    }

    [Fact]
    public async Task Runner_RejectsNonPositiveWorkerAcknowledgementTimeout()
    {
        var runner = new AutoTaskRunner();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.ExecuteAsync(
            new AutoTaskRequest { Kind = AutoTaskKind.Custom, StageTarget = CreateTarget() },
            new AutoTaskExecutionOptions { WorkerAcknowledgementTimeout = TimeSpan.Zero }));
    }

    [Fact]
    public void RuntimeState_CountsOnlyConfirmedSuccessfulStageOutcomes()
    {
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.LoopStage,
            StageTarget = CreateTarget()
        });

        state.BeginStageAttempt();
        state.RecordScriptExecutionResult(CreateSuccessfulScriptResult());

        Assert.False(state.TryRecordStageCompletion(GameUiStateId.InLevel));
        Assert.True(state.TryRecordStageCompletion(GameUiStateId.StageSettlement));
        Assert.False(state.TryRecordStageCompletion(GameUiStateId.Victory));
        Assert.Equal(1, state.CompletedStageCount);

        state.BeginStageAttempt();
        state.RecordScriptExecutionResult(CreateSuccessfulScriptResult());
        state.RecordStageFailure();

        Assert.False(state.TryRecordStageCompletion(GameUiStateId.Victory));
        Assert.Equal(1, state.CompletedStageCount);
    }

    [Fact]
    public void RuntimeState_FreeplayCountsOnlyAfterTargetRoundAndMainMenu()
    {
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.LoopStage,
            StageTarget = CreateTarget(),
            LoopStageRunMode = LoopStageRunMode.FreeplayUntilRound,
            ExitAfterRound = 102
        });

        state.BeginStageAttempt();
        state.RecordScriptExecutionResult(CreateSuccessfulScriptResult());
        state.SetProperty(LoopStageAutoTaskStateKeys.ScriptRunState, LoopStageScriptRunState.WaitingForExit);
        state.SetProperty(LoopStageAutoTaskStateKeys.TargetRoundReached, true);

        Assert.False(state.TryRecordStageCompletion(GameUiStateId.Victory));
        Assert.False(state.TryRecordStageCompletion(GameUiStateId.StageSettlement));
        Assert.True(state.TryRecordStageCompletion(GameUiStateId.MainMenu));
        Assert.Equal(1, state.CompletedStageCount);
    }

    [Fact]
    public void RoundProgressTracker_RequiresConsecutiveValidTargetObservations()
    {
        var tracker = new LoopStageRoundProgressTracker(102);

        Assert.False(tracker.Observe(null));
        Assert.False(tracker.Observe(102));
        Assert.False(tracker.Observe(101));
        Assert.False(tracker.Observe(102));
        Assert.True(tracker.Observe(102));
    }

    [Fact]
    public void RoundProgressTracker_RejectsObviousHighValuesAndRestartsConfirmation()
    {
        var tracker = new LoopStageRoundProgressTracker(102);

        Assert.False(tracker.Observe(102));
        Assert.False(tracker.Observe(999));
        Assert.False(tracker.Observe(102));
        Assert.True(tracker.Observe(102));
    }

    [Theory]
    [InlineData(GameUiStateId.MainMenu, GameUiActionKind.OpenMapSelection)]
    [InlineData(GameUiStateId.MapCategorySelect, GameUiActionKind.SelectMapCategory)]
    [InlineData(GameUiStateId.MapGrid, GameUiActionKind.SelectMap)]
    [InlineData(GameUiStateId.DifficultySelect, GameUiActionKind.SelectDifficulty)]
    [InlineData(GameUiStateId.ModeSelect, GameUiActionKind.SelectMode)]
    [InlineData(GameUiStateId.Loading, GameUiActionKind.Wait)]
    [InlineData(GameUiStateId.InLevel, GameUiActionKind.None)]
    [InlineData(GameUiStateId.Victory, GameUiActionKind.CollectReward)]
    [InlineData(GameUiStateId.NetworkUnavailableDialog, GameUiActionKind.ConfirmDialog)]
    public void Navigator_ReturnsExpectedAction(GameUiStateId state, GameUiActionKind expectedAction)
    {
        var target = CreateTarget();
        var snapshot = new GameUiSnapshot { State = state };

        var step = GameUiNavigator.Instance.GetNextStep(target, snapshot);

        Assert.Equal(expectedAction, step.ActionKind);
    }

    [Fact]
    public async Task ActionExecutor_DismissesNetworkUnavailableDialog_ForEveryAutoTaskKind()
    {
        foreach (var kind in Enum.GetValues<AutoTaskKind>())
        {
            var dispatcher = new RecordingInputSimulationCommandDispatcher();
            var input = new ScriptInputSimulationService(
                new FakeScriptInputSimulationEnvironment(new GameWindowInfo(
                    (nint)123,
                    "Test Window",
                    new NativeWindowBounds(0, 0, 1920, 1080),
                    new NativeWindowBounds(0, 0, 1920, 1080),
                    1d)),
                dispatcher);
            var executor = new GameUiActionExecutor(
                input,
                UnimplementedGameUiElementLocator.Instance,
                GameCaptureService.Instance,
                GameUiNavigationOcrService.Instance);
            var snapshot = new GameUiSnapshot { State = GameUiStateId.NetworkUnavailableDialog };
            var step = GameUiNavigator.Instance.GetNextStep(CreateTarget(), snapshot);
            var state = new AutoTaskRuntimeState(new AutoTaskRequest
            {
                Kind = kind,
                StageTarget = CreateTarget()
            });

            var result = await executor.ExecuteAsync(step, state, snapshot);

            Assert.True(result.Succeeded);
            var move = Assert.Single(
                dispatcher.Commands,
                static command => command.Type == InputSimulationCommandType.MoveMouseToVirtualDesktop);
            Assert.Equal(780, move.X);
            Assert.Equal(730, move.Y);
        }
    }

    [Fact]
    public async Task CollectionActionHandler_TreatsUnknownUiAsWait()
    {
        var target = CreateTarget();
        var snapshot = new GameUiSnapshot { State = GameUiStateId.Unknown };
        var step = GameUiNavigator.Instance.GetNextStep(target, snapshot);
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = target
        });
        var handler = new CollectionGameUiActionHandler(
            ScriptInputSimulationService.Instance,
            GameCaptureService.Instance,
            GameUiNavigationOcrService.Instance);

        var result = await handler.ExecuteAsync(step, state, snapshot);

        Assert.Equal(GameUiActionKind.Wait, step.ActionKind);
        Assert.True(result.Succeeded);
        Assert.Equal(step.PostActionDelayMs, result.RecommendedDelayMs);
    }

    [Fact]
    public async Task Runner_StartsScriptImmediately_WhenAlreadyInLevel()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Victory }
        ]);
        var actionExecutor = new RecordingGameUiActionExecutor();
        var scriptResolver = new RecordingAutoTaskScriptResolver("custom-stage.json");
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());
        await using var runtime = CreateRuntimeServices(
            uiStateService,
            actionExecutor,
            scriptResolver,
            scriptExecutor);
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner();
        var resultTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "custom-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10,
                ScriptOffLevelGracePeriod = TimeSpan.Zero
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await WaitUntilAsync(() => scriptExecutor.ExecutedFilePaths.Count == 1, TimeSpan.FromSeconds(2));
        Assert.False(resultTask.IsCompleted);
        runtime.Navigation.Publish(GameUiStateId.Victory);

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Single(scriptResolver.Queries);
        Assert.Equal("custom-stage.json", scriptResolver.Queries[0].PreferredFilePath);
        Assert.Single(scriptExecutor.ExecutedFilePaths);
        Assert.Equal("custom-stage.json", scriptExecutor.ExecutedFilePaths[0]);
        Assert.Empty(actionExecutor.ExecutedSteps);
        Assert.Equal(1, result.FinalProgress.CompletedStageCount);
        Assert.Equal(0, uiStateService.CaptureCount);
        Assert.Equal(1, runtime.Navigation.StartCount);
        Assert.Equal(1, runtime.Navigation.StopCount);
    }

    [Fact]
    public async Task Runner_NavigatesBeforeStartingScript_WhenNotYetInLevel()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.MainMenu },
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Victory }
        ]);
        var actionExecutor = new RecordingGameUiActionExecutor();
        var scriptResolver = new RecordingAutoTaskScriptResolver("nav-stage.json");
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());

        await using var runtime = CreateRuntimeServices(
            uiStateService,
            actionExecutor,
            scriptResolver,
            scriptExecutor);
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner();
        var resultTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "nav-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10
            });

        runtime.Navigation.Publish(GameUiStateId.MainMenu);
        await WaitUntilAsync(() => actionExecutor.ExecutedSteps.Count == 1, TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await WaitUntilAsync(() => scriptExecutor.ExecutedFilePaths.Count == 1, TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.Victory);

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Single(actionExecutor.ExecutedSteps);
        Assert.Equal(GameUiActionKind.OpenMapSelection, actionExecutor.ExecutedSteps[0].ActionKind);
        Assert.Single(scriptExecutor.ExecutedFilePaths);
        Assert.Equal(0, uiStateService.CaptureCount);
    }

    [Fact]
    public async Task Runner_IgnoresObservationPublishedBeforeExecution()
    {
        var uiStateService = new QueueGameUiStateService([]);
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());
        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("custom-stage.json"),
            scriptExecutor);
        runtime.Navigation.Publish(GameUiStateId.InLevel);

        var runner = new AutoTaskRunner();
        var executionTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "custom-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtime.Services,
                MaxLoopIterations = 10
            });

        await WaitUntilAsync(() => runtime.Navigation.StartCount == 1, TimeSpan.FromSeconds(2));
        Assert.Empty(scriptExecutor.ExecutedFilePaths);

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await WaitUntilAsync(() => scriptExecutor.ExecutedFilePaths.Count == 1, TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.Victory);

        var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Runner_ResetsUiStateStabilization_BeforeAndAfterExecution()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Victory }
        ]);
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());
        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("custom-stage.json"),
            scriptExecutor);
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner();
        var resultTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "custom-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await WaitUntilAsync(() => scriptExecutor.ExecutedFilePaths.Count == 1, TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.Victory);

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Equal(2, uiStateService.ResetCount);
    }

    [Fact]
    public async Task Runner_ForwardsPauseAndResume_ToUnderlyingScriptExecutor()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Victory }
        ]);
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("blocking-stage.json"),
            scriptExecutor);
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner();
        var executionTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "blocking-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            AutoTaskActivityKind.ExecutingScript,
            runner.CurrentSession?.GetSnapshot().CurrentActivity);

        Assert.True(runner.RequestPause());
        Assert.Equal(1, scriptExecutor.PauseRequestCount);

        Assert.True(runner.Resume());
        Assert.Equal(1, scriptExecutor.ResumeCount);

        scriptExecutor.Complete(CreateSuccessfulScriptResult());
        await WaitUntilAsync(
            () => runtime.WorkerState == ScriptWorkerState.Completed,
            TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.Victory);

        var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Runner_CancellationSignalsUnderlyingStageScriptWorker()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel }
        ]);
        var strategy = new InterruptAwareCollectionStrategy();
        var scriptExecutor = new DelayedCancellationAutoTaskScriptExecutor();
        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("collection-stage.json"),
            scriptExecutor);
        var runtimeServices = runtime.Services;
        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);
        using var cancellationSource = new CancellationTokenSource();

        var executionTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Collection,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "collection-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10
            },
            cancellationSource.Token);

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellationSource.Cancel();
        try
        {
            await scriptExecutor.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            scriptExecutor.AllowExit();
            var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(AutoTaskExecutionStatus.Cancelled, result.Status);
            Assert.False(scriptExecutor.IsRunning);
        }
        finally
        {
            scriptExecutor.AllowExit();
        }

        await WaitUntilAsync(() => !scriptExecutor.IsRunning, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Runner_FailsWhenNavigationRuntimeIsMissing()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel }
        ]);
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());
        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("custom-stage.json"),
            ScriptExecutor = scriptExecutor
        };
        var runner = new AutoTaskRunner();

        var result = await runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "custom-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10,
                ScriptOffLevelGracePeriod = TimeSpan.Zero
            });

        Assert.Equal(AutoTaskExecutionStatus.Failed, result.Status);
        Assert.Equal("NavigationController", result.Failure?.Checkpoint);
        Assert.Empty(scriptExecutor.ExecutedFilePaths);
    }

    [Fact]
    public async Task Runner_InterruptsCollectionScript_WhenDefeatUiDetected()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Defeat }
        ]);
        var strategy = new InterruptAwareCollectionStrategy();
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("collection-stage.json"),
            scriptExecutor);
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

        var resultTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Collection,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "collection-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10,
                ScriptOffLevelGracePeriod = TimeSpan.Zero
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.Defeat);

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        await WaitUntilAsync(() => scriptExecutor.CancellationObserved, TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { GameUiStateId.Defeat }, strategy.InterruptedSnapshots);
    }

    [Fact]
    public async Task Runner_InterruptsCollectionScript_WhenSettlementUiDetected()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.StageSettlement }
        ]);
        var strategy = new InterruptAwareCollectionStrategy();
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("collection-stage.json"),
            scriptExecutor);
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

        var resultTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Collection,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "collection-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10,
                ScriptOffLevelGracePeriod = TimeSpan.Zero
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.StageSettlement);

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        await WaitUntilAsync(() => scriptExecutor.CancellationObserved, TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { GameUiStateId.StageSettlement }, strategy.InterruptedSnapshots);
    }

    [Theory]
    [InlineData(GameUiStateId.StageHint)]
    [InlineData(GameUiStateId.ConfirmDialog)]
    public async Task Runner_PausesAndResumesRaceScript_WhenPopupDetected(
        GameUiStateId uiState)
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.RaceResult }
        ]);
        var strategy = new InterruptAwareRaceStrategy();
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("race-stage.json"),
            scriptExecutor,
            new SuccessfulRecoveryExecutor());
        var runtimeServices = runtime.Services;

        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

        var resultTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Race,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "race-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10,
                ScriptOffLevelGracePeriod = TimeSpan.Zero
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(uiState);
        await WaitUntilAsync(() => scriptExecutor.PauseRequestCount == 1, TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await WaitUntilAsync(() => scriptExecutor.ResumeCount == 1, TimeSpan.FromSeconds(2));
        scriptExecutor.Complete(CreateSuccessfulScriptResult());
        await WaitUntilAsync(
            () => runtime.WorkerState == ScriptWorkerState.Completed,
            TimeSpan.FromSeconds(2));
        runtime.Navigation.Publish(GameUiStateId.RaceResult);

        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Equal(new[] { GameUiStateId.RaceResult }, strategy.InterruptedSnapshots);
    }

    [Fact]
    public async Task Runner_WaitsForResultAfterScriptWorkerCompletes()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Victory }
        ]);
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());
        await using var runtime = CreateRuntimeServices(
            uiStateService,
            new RecordingGameUiActionExecutor(),
            new RecordingAutoTaskScriptResolver("custom-stage.json"),
            scriptExecutor);
        var runner = new AutoTaskRunner();

        var executionTask = runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Custom,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "custom-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtime.Services,
                MaxLoopIterations = 10
            });

        runtime.Navigation.Publish(GameUiStateId.InLevel);
        await WaitUntilAsync(() => scriptExecutor.ExecutedFilePaths.Count == 1, TimeSpan.FromSeconds(2));

        Assert.False(executionTask.IsCompleted);

        runtime.Navigation.Publish(GameUiStateId.Victory);
        var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
    }

    private static TestAutoTaskRuntime CreateRuntimeServices(
        IGameUiStateService gameUiState,
        IGameUiActionExecutor actionExecutor,
        IAutoTaskScriptResolver scriptResolver,
        IAutoTaskScriptExecutor scriptExecutor,
        IGameUiStuckRecoveryExecutor? recoveryExecutor = null)
    {
        return new TestAutoTaskRuntime(
            gameUiState,
            actionExecutor,
            scriptResolver,
            scriptExecutor,
            recoveryExecutor);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt >= timeout)
            {
                throw new TimeoutException("Expected condition was not reached before the timeout.");
            }

            await Task.Delay(20);
        }
    }

    private static StageEntryTarget CreateTarget()
    {
        return new StageEntryTarget
        {
            Map = GameMapType.MonkeyMeadow,
            Difficulty = StageDifficulty.Easy,
            Mode = StageMode.Standard
        };
    }

    private static ScriptExecutionResult CreateSuccessfulScriptResult()
    {
        return new ScriptExecutionResult
        {
            Status = ScriptExecutionStatus.Completed,
            ExecutedStepCount = 1,
            LastCompletedStepIndex = 0,
            FinalProgress = new ScriptExecutionProgressSnapshot()
        };
    }

    private sealed class QueueGameUiStateService : IGameUiStateService
    {
        private readonly Queue<GameUiSnapshot> _snapshots;
        private GameUiSnapshot _lastSnapshot;
        public int CaptureCount { get; private set; }
        public int ResetCount { get; private set; }

        public QueueGameUiStateService(IEnumerable<GameUiSnapshot> snapshots)
        {
            _snapshots = new Queue<GameUiSnapshot>(snapshots);
            _lastSnapshot = _snapshots.Count > 0 ? _snapshots.Peek() : new GameUiSnapshot { State = GameUiStateId.Unknown };
        }

        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCount++;

            if (_snapshots.Count > 0)
            {
                _lastSnapshot = _snapshots.Dequeue();
            }

            return Task.FromResult(_lastSnapshot);
        }

        public void ResetStabilizationState()
        {
            ResetCount++;
        }
    }

    private sealed class RecordingGameUiActionExecutor : IGameUiActionExecutor
    {
        public List<GameUiNavigationStep> ExecutedSteps { get; } = [];

        public Task<GameUiActionExecutionResult> ExecuteAsync(
            GameUiNavigationStep step,
            AutoTaskRuntimeState state,
            GameUiSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            ExecutedSteps.Add(step);

            return Task.FromResult(new GameUiActionExecutionResult
            {
                Succeeded = true,
                Message = step.Description,
                RecommendedDelayMs = 0
            });
        }
    }

    private sealed class RecordingAutoTaskScriptResolver : IAutoTaskScriptResolver
    {
        private readonly string _resolvedFilePath;

        public RecordingAutoTaskScriptResolver(string resolvedFilePath)
        {
            _resolvedFilePath = resolvedFilePath;
        }

        public List<AutoTaskScriptQuery> Queries { get; } = [];

        public Task<AutoTaskScriptResolution> ResolveAsync(
            AutoTaskScriptQuery query,
            AutoTaskRuntimeState state,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);

            return Task.FromResult(new AutoTaskScriptResolution
            {
                IsResolved = true,
                FilePath = _resolvedFilePath,
                Query = query,
                Message = "Resolved by test double."
            });
        }
    }

    private sealed class RecordingAutoTaskScriptExecutor : IAutoTaskScriptExecutor
    {
        private readonly ScriptExecutionResult _result;

        public RecordingAutoTaskScriptExecutor(ScriptExecutionResult result)
        {
            _result = result;
        }

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public List<string> ExecutedFilePaths { get; } = [];

        public bool IsRunning => false;

        public bool RequestPause()
        {
            return false;
        }

        public bool Resume()
        {
            return false;
        }

        public Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            ExecutedFilePaths.Add(filePath);
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                CurrentStepIndex = 0,
                LastCompletedStepIndex = -1,
                RunState = ScriptExecutionRunState.Running
            });
            return Task.FromResult(_result);
        }
    }

    private sealed class BlockingAutoTaskScriptExecutor : IAutoTaskScriptExecutor
    {
        private readonly TaskCompletionSource<ScriptExecutionResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public int PauseRequestCount { get; private set; }

        public int ResumeCount { get; private set; }

        public bool CancellationObserved { get; private set; }

        public bool IsRunning { get; private set; }

        public bool RequestPause()
        {
            PauseRequestCount++;
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                RunState = ScriptExecutionRunState.PauseRequested
            });
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                RunState = ScriptExecutionRunState.Paused
            });
            return true;
        }

        public bool Resume()
        {
            ResumeCount++;
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                RunState = ScriptExecutionRunState.Running
            });
            return true;
        }

        public async Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            Started.TrySetResult(true);
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                CurrentStepIndex = 0,
                LastCompletedStepIndex = -1,
                RunState = ScriptExecutionRunState.Running
            });

            try
            {
                return await _completion.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                return new ScriptExecutionResult
                {
                    Status = ScriptExecutionStatus.Cancelled,
                    ExecutedStepCount = 0,
                    LastCompletedStepIndex = -1,
                    FinalProgress = new ScriptExecutionProgressSnapshot
                    {
                        RunState = ScriptExecutionRunState.Cancelled
                    }
                };
            }
            finally
            {
                IsRunning = false;
            }
        }

        public void Complete(ScriptExecutionResult result)
        {
            _completion.TrySetResult(result);
        }
    }

    private sealed class DelayedCancellationAutoTaskScriptExecutor : IAutoTaskScriptExecutor
    {
        private readonly TaskCompletionSource _allowExit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public bool IsRunning { get; private set; }

        public bool RequestPause() => false;

        public bool Resume() => false;

        public async Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            Started.TrySetResult();
            ProgressChanged?.Invoke(this, new ScriptExecutionProgressSnapshot
            {
                RunState = ScriptExecutionRunState.Running
            });

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancellation test script unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                await _allowExit.Task;
                return new ScriptExecutionResult
                {
                    Status = ScriptExecutionStatus.Cancelled,
                    ExecutedStepCount = 0,
                    LastCompletedStepIndex = -1,
                    FinalProgress = new ScriptExecutionProgressSnapshot
                    {
                        RunState = ScriptExecutionRunState.Cancelled
                    }
                };
            }
            finally
            {
                IsRunning = false;
            }
        }

        public void AllowExit()
        {
            _allowExit.TrySetResult();
        }
    }

    private sealed class TestAutoTaskRuntime : IAsyncDisposable
    {
        private readonly ScriptTaskFlowWorker _worker;

        public TestAutoTaskRuntime(
            IGameUiStateService gameUiState,
            IGameUiActionExecutor actionExecutor,
            IAutoTaskScriptResolver scriptResolver,
            IAutoTaskScriptExecutor scriptExecutor,
            IGameUiStuckRecoveryExecutor? recoveryExecutor)
        {
            Navigation = new TestNavigationObservationService();
            _worker = new ScriptTaskFlowWorker(
                new AutoTaskScriptExecutionEngineAdapter(scriptExecutor),
                TimeProvider.System);
            Services = new AutoTaskRuntimeServices
            {
                GameUiState = gameUiState,
                NavigationObservation = Navigation,
                Navigator = GameUiNavigator.Instance,
                UiActionExecutor = actionExecutor,
                StuckRecoveryExecutor = recoveryExecutor,
                ScriptResolver = scriptResolver,
                ScriptExecutor = scriptExecutor,
                ScriptWorker = _worker
            };
        }

        public TestNavigationObservationService Navigation { get; }

        public AutoTaskRuntimeServices Services { get; }

        public ScriptWorkerState WorkerState => _worker.State;

        public async ValueTask DisposeAsync()
        {
            await Navigation.StopAsync();
            await _worker.StopAsync();
        }
    }

    private sealed class SuccessfulRecoveryExecutor : IGameUiStuckRecoveryExecutor
    {
        public Task<GameUiActionExecutionResult> ClickAsync(
            GameUiRecoveryPoint point,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GameUiActionExecutionResult { Succeeded = true });
        }
    }

    private sealed class TestNavigationObservationService : INavigationObservationService
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<long, System.Threading.Channels.Channel<NavigationObservation>> _subscribers = [];
        private long _sequence;
        private long _nextSubscriberId;
        private bool _isRunning;

        public NavigationObservation? LatestObservation { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public NavigationObservationDiagnostics GetDiagnostics()
        {
            return new NavigationObservationDiagnostics
            {
                IsRunning = _isRunning,
                PublishedCount = Volatile.Read(ref _sequence)
            };
        }

        public void Start(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                StartCount++;
                _isRunning = true;
            }
        }

        public async IAsyncEnumerable<NavigationObservation> SubscribeAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = System.Threading.Channels.Channel.CreateBounded<NavigationObservation>(
                new System.Threading.Channels.BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                });
            long subscriberId;

            lock (_syncRoot)
            {
                subscriberId = ++_nextSubscriberId;
                _subscribers.Add(subscriberId, channel);
                if (LatestObservation is not null)
                {
                    channel.Writer.TryWrite(LatestObservation);
                }
            }

            try
            {
                await foreach (var observation in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return observation;
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

        public Task StopAsync()
        {
            lock (_syncRoot)
            {
                StopCount++;
                _isRunning = false;
            }

            return Task.CompletedTask;
        }

        public void Publish(GameUiStateId state)
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var snapshot = new GameUiSnapshot
            {
                CapturedAt = capturedAt,
                State = state
            };
            var observation = new NavigationObservation(
                Interlocked.Increment(ref _sequence),
                capturedAt,
                snapshot);
            lock (_syncRoot)
            {
                LatestObservation = observation;
                foreach (var subscriber in _subscribers.Values)
                {
                    Assert.True(subscriber.Writer.TryWrite(observation));
                }
            }
        }
    }

    private sealed class AutoTaskScriptExecutionEngineAdapter : IScriptTaskFlowExecutionEngine
    {
        private readonly IAutoTaskScriptExecutor _executor;

        public AutoTaskScriptExecutionEngineAdapter(IAutoTaskScriptExecutor executor)
        {
            _executor = executor;
        }

        public bool IsRunning => _executor.IsRunning;

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged
        {
            add => _executor.ProgressChanged += value;
            remove => _executor.ProgressChanged -= value;
        }

        public bool RequestPause() => _executor.RequestPause();

        public bool Resume() => _executor.Resume();

        public bool RequestStop() => _executor.IsRunning;

        public Task<ScriptExecutionResult> ExecuteAsync(
            string filePath,
            ScriptExecutionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return _executor.ExecuteAsync(
                filePath,
                options ?? new ScriptExecutionOptions(),
                cancellationToken);
        }
    }

    private sealed class InterruptAwareCollectionStrategy : IAutoTaskStrategy
    {
        public AutoTaskKind Kind => AutoTaskKind.Collection;

        public List<GameUiStateId> InterruptedSnapshots { get; } = [];

        public Task<AutoTaskDecision> DecideNextAsync(
            AutoTaskRuntimeState state,
            GameUiSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (state.HasPendingScriptOutcome)
            {
                InterruptedSnapshots.Add(snapshot.State);
                return Task.FromResult(AutoTaskDecision.Complete($"Handled collection result UI '{snapshot.State}'."));
            }

            if (snapshot.State == GameUiStateId.InLevel)
            {
                return Task.FromResult(AutoTaskDecision.StartScript(
                    new AutoTaskScriptQuery
                    {
                        Kind = AutoTaskKind.Collection,
                        StageTarget = state.Request.StageTarget,
                        PreferredFilePath = "collection-stage.json",
                        Description = "Start collection test script."
                    },
                    "Start collection test script."));
            }

            return Task.FromResult(AutoTaskDecision.Navigate("Advance collection test flow."));
        }
    }

    private sealed class InterruptAwareRaceStrategy : IAutoTaskStrategy
    {
        public AutoTaskKind Kind => AutoTaskKind.Race;

        public List<GameUiStateId> InterruptedSnapshots { get; } = [];

        public Task<AutoTaskDecision> DecideNextAsync(
            AutoTaskRuntimeState state,
            GameUiSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (state.HasPendingScriptOutcome)
            {
                InterruptedSnapshots.Add(snapshot.State);
                return Task.FromResult(AutoTaskDecision.Complete($"Handled race UI '{snapshot.State}'."));
            }

            if (snapshot.State == GameUiStateId.InLevel)
            {
                return Task.FromResult(AutoTaskDecision.StartScript(
                    new AutoTaskScriptQuery
                    {
                        Kind = AutoTaskKind.Race,
                        StageTarget = state.Request.StageTarget,
                        PreferredFilePath = "race-stage.json",
                        Description = "Start race test script."
                    },
                    "Start race test script."));
            }

            return Task.FromResult(AutoTaskDecision.Navigate("Advance race test flow."));
        }
    }

    private sealed class SingleStrategyRegistry : IAutoTaskStrategyRegistry
    {
        private readonly IAutoTaskStrategy _strategy;

        public SingleStrategyRegistry(IAutoTaskStrategy strategy)
        {
            _strategy = strategy;
        }

        public IAutoTaskStrategy GetRequiredStrategy(AutoTaskKind kind)
        {
            Assert.Equal(_strategy.Kind, kind);
            return _strategy;
        }
    }
}
