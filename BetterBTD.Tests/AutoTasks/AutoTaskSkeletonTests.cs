using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskSkeletonTests
{
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
    public async Task Runner_StartsScriptImmediately_WhenAlreadyInLevel()
    {
        var uiStateService = new QueueGameUiStateService(
        [
            new GameUiSnapshot { State = GameUiStateId.InLevel },
            new GameUiSnapshot { State = GameUiStateId.InLevel }
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
}
