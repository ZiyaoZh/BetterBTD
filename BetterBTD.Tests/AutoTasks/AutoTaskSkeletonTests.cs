using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskSkeletonTests
{
    [Fact]
    public void AutoTaskExecutionOptions_UsesRaisedDefaultLoopLimit()
    {
        var options = new AutoTaskExecutionOptions();

        Assert.Equal(20000, options.MaxLoopIterations);
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

    [Theory]
    [InlineData(GameUiStateId.MainMenu, GameUiActionKind.OpenMapSelection)]
    [InlineData(GameUiStateId.MapCategorySelect, GameUiActionKind.SelectMapCategory)]
    [InlineData(GameUiStateId.MapGrid, GameUiActionKind.SelectMap)]
    [InlineData(GameUiStateId.DifficultySelect, GameUiActionKind.SelectDifficulty)]
    [InlineData(GameUiStateId.ModeSelect, GameUiActionKind.SelectMode)]
    [InlineData(GameUiStateId.Loading, GameUiActionKind.Wait)]
    [InlineData(GameUiStateId.InLevel, GameUiActionKind.None)]
    [InlineData(GameUiStateId.Victory, GameUiActionKind.CollectReward)]
    public void Navigator_ReturnsExpectedAction(GameUiStateId state, GameUiActionKind expectedAction)
    {
        var target = CreateTarget();
        var snapshot = new GameUiSnapshot { State = state };

        var step = GameUiNavigator.Instance.GetNextStep(target, snapshot);

        Assert.Equal(expectedAction, step.ActionKind);
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

        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = actionExecutor,
            ScriptResolver = scriptResolver,
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
                MaxLoopIterations = 10
            });

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Single(scriptResolver.Queries);
        Assert.Equal("custom-stage.json", scriptResolver.Queries[0].PreferredFilePath);
        Assert.Single(scriptExecutor.ExecutedFilePaths);
        Assert.Equal("custom-stage.json", scriptExecutor.ExecutedFilePaths[0]);
        Assert.Empty(actionExecutor.ExecutedSteps);
        Assert.Equal(1, result.FinalProgress.CompletedStageCount);
    }

    [Fact]
    public async Task Runner_NavigatesBeforeStartingScript_WhenNotYetInLevel()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.MainMenu },
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.InLevel }
        ]);
        var actionExecutor = new RecordingGameUiActionExecutor();
        var scriptResolver = new RecordingAutoTaskScriptResolver("nav-stage.json");
        var scriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult());

        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = actionExecutor,
            ScriptResolver = scriptResolver,
            ScriptExecutor = scriptExecutor
        };

        var runner = new AutoTaskRunner();
        var result = await runner.ExecuteAsync(
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

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Single(actionExecutor.ExecutedSteps);
        Assert.Equal(GameUiActionKind.OpenMapSelection, actionExecutor.ExecutedSteps[0].ActionKind);
        Assert.Single(scriptExecutor.ExecutedFilePaths);
    }

    [Fact]
    public async Task Runner_ResetsUiStateStabilization_BeforeAndAfterExecution()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.InLevel }
        ]);
        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("custom-stage.json"),
            ScriptExecutor = new RecordingAutoTaskScriptExecutor(CreateSuccessfulScriptResult())
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
                MaxLoopIterations = 10
            });

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.Equal(2, uiStateService.ResetCount);
    }

    [Fact]
    public async Task Runner_ForwardsPauseAndResume_ToUnderlyingScriptExecutor()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.InLevel }
        ]);
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("blocking-stage.json"),
            ScriptExecutor = scriptExecutor
        };

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

        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(runner.RequestPause());
        Assert.Equal(1, scriptExecutor.PauseRequestCount);

        Assert.True(runner.Resume());
        Assert.Equal(1, scriptExecutor.ResumeCount);

        scriptExecutor.Complete(CreateSuccessfulScriptResult());

        var result = await executionTask;
        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Runner_CancellationWaitsForUnderlyingStageScriptToExit()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel }
        ]);
        var strategy = new InterruptAwareCollectionStrategy();
        var scriptExecutor = new DelayedCancellationAutoTaskScriptExecutor();
        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("collection-stage.json"),
            ScriptExecutor = scriptExecutor
        };
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

        await scriptExecutor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellationSource.Cancel();
        await scriptExecutor.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(executionTask.IsCompleted);
        scriptExecutor.AllowExit();

        var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Cancelled, result.Status);
        Assert.False(scriptExecutor.IsRunning);
    }

    [Fact]
    public async Task Runner_MonitorFailurePreservesOriginalExceptionAfterScriptExits()
    {
        var uiStateService = new ThrowAfterFirstCaptureGameUiStateService();
        var scriptExecutor = new DelayedCancellationAutoTaskScriptExecutor
        {
            ThrowOnCancellation = true
        };
        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("collection-stage.json"),
            ScriptExecutor = scriptExecutor
        };
        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(new InterruptAwareCollectionStrategy()),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

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
            });

        await scriptExecutor.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(executionTask.IsCompleted);
        scriptExecutor.AllowExit();

        var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AutoTaskExecutionStatus.Failed, result.Status);
        Assert.Equal("monitor capture failed", result.Exception?.Message);
        Assert.False(scriptExecutor.IsRunning);
    }

    [Fact]
    public async Task Runner_InterruptsCollectionScript_WhenDefeatUiDetected()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.Defeat },
            new GameUiSnapshot { State = GameUiStateId.Defeat }
        ]);
        var strategy = new InterruptAwareCollectionStrategy();
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("collection-stage.json"),
            ScriptExecutor = scriptExecutor
        };

        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

        var result = await runner.ExecuteAsync(
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
            });

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.True(scriptExecutor.CancellationObserved);
        Assert.Equal(new[] { GameUiStateId.Defeat }, strategy.InterruptedSnapshots);
    }

    [Fact]
    public async Task Runner_InterruptsCollectionScript_WhenSettlementUiDetected()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.StageSettlement },
            new GameUiSnapshot { State = GameUiStateId.StageSettlement }
        ]);
        var strategy = new InterruptAwareCollectionStrategy();
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("collection-stage.json"),
            ScriptExecutor = scriptExecutor
        };

        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

        var result = await runner.ExecuteAsync(
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
            });

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.True(scriptExecutor.CancellationObserved);
        Assert.Equal(new[] { GameUiStateId.StageSettlement }, strategy.InterruptedSnapshots);
    }

    [Theory]
    [InlineData(GameUiStateId.StageHint)]
    [InlineData(GameUiStateId.StageSettings)]
    public async Task Runner_InterruptsRaceScript_WhenHandledRaceUiDetected(
        GameUiStateId uiState)
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = uiState },
            new GameUiSnapshot { State = uiState }
        ]);
        var strategy = new InterruptAwareRaceStrategy();
        var scriptExecutor = new BlockingAutoTaskScriptExecutor();

        var runtimeServices = new AutoTaskRuntimeServices
        {
            GameUiState = uiStateService,
            Navigator = GameUiNavigator.Instance,
            UiActionExecutor = new RecordingGameUiActionExecutor(),
            ScriptResolver = new RecordingAutoTaskScriptResolver("race-stage.json"),
            ScriptExecutor = scriptExecutor
        };

        var runner = new AutoTaskRunner(
            new SingleStrategyRegistry(strategy),
            runtimeServices,
            AutoTaskRuntimeScriptPreviewService.Instance);

        var result = await runner.ExecuteAsync(
            new AutoTaskRequest
            {
                Kind = AutoTaskKind.Race,
                StageTarget = CreateTarget(),
                PreferredScriptPath = "race-stage.json"
            },
            new AutoTaskExecutionOptions
            {
                RuntimeServices = runtimeServices,
                MaxLoopIterations = 10
            });

        Assert.Equal(AutoTaskExecutionStatus.Completed, result.Status);
        Assert.True(scriptExecutor.CancellationObserved);
        Assert.Equal(new[] { uiState }, strategy.InterruptedSnapshots);
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
        public int ResetCount { get; private set; }

        public QueueGameUiStateService(IEnumerable<GameUiSnapshot> snapshots)
        {
            _snapshots = new Queue<GameUiSnapshot>(snapshots);
            _lastSnapshot = _snapshots.Count > 0 ? _snapshots.Peek() : new GameUiSnapshot { State = GameUiStateId.Unknown };
        }

        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            return true;
        }

        public bool Resume()
        {
            ResumeCount++;
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

        public bool ThrowOnCancellation { get; init; }

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
                if (ThrowOnCancellation)
                {
                    throw;
                }

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

    private sealed class ThrowAfterFirstCaptureGameUiStateService : IGameUiStateService
    {
        private int _captureCount;

        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _captureCount) == 1)
            {
                return Task.FromResult(new GameUiSnapshot { State = GameUiStateId.InLevel });
            }

            throw new InvalidOperationException("monitor capture failed");
        }

        public void ResetStabilizationState()
        {
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
