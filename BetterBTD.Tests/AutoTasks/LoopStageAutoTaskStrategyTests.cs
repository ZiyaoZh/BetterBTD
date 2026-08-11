using BetterBTD.Core.AutoTasks.Strategies;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.MyScripts;

namespace BetterBTD.Tests.AutoTasks;

public sealed class LoopStageAutoTaskStrategyTests
{
    [Fact]
    public async Task FreeplayMode_SplitsScriptAroundBoundary()
    {
        var scriptPath = CreateScript(
            ScriptCommandType.Comment,
            ScriptCommandType.FreeplayBoundary,
            ScriptCommandType.Comment);

        try
        {
            var strategy = new LoopStageAutoTaskStrategy();
            var state = new AutoTaskRuntimeState(new AutoTaskRequest
            {
                Kind = AutoTaskKind.LoopStage,
                StageTarget = CreateTarget(),
                PreferredScriptPath = scriptPath,
                LoopStageRunMode = LoopStageRunMode.FreeplayUntilRound,
                ExitAfterRound = 102
            });

            var navigationDecision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.MainMenu });
            Assert.Equal(AutoTaskDecisionKind.Navigate, navigationDecision.Kind);

            var firstScriptDecision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.InLevel });
            Assert.Equal(AutoTaskDecisionKind.StartScriptExecution, firstScriptDecision.Kind);
            Assert.NotNull(firstScriptDecision.ScriptQuery);
            Assert.Equal(0, firstScriptDecision.ScriptQuery!.StartStepIndex);
            Assert.Equal(2, firstScriptDecision.ScriptQuery.EndStepIndexExclusive);

            state.SetProperty(
                LoopStageAutoTaskStateKeys.InterruptedUiState,
                GameUiStateId.StageSettlement);
            state.RecordScriptExecutionResult(CreateSuccessfulScriptResult());

            var promptDecision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.StageSettlement });
            Assert.Equal(AutoTaskDecisionKind.Navigate, promptDecision.Kind);

            var unconfirmedDecision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.InLevel });
            Assert.Equal(AutoTaskDecisionKind.Wait, unconfirmedDecision.Kind);

            state.SetProperty(LoopStageAutoTaskStateKeys.FreeplayPromptConfirmed, true);
            var freeplayScriptDecision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.InLevel });
            Assert.Equal(AutoTaskDecisionKind.StartScriptExecution, freeplayScriptDecision.Kind);
            Assert.NotNull(freeplayScriptDecision.ScriptQuery);
            Assert.Equal(2, freeplayScriptDecision.ScriptQuery!.StartStepIndex);
            Assert.Null(freeplayScriptDecision.ScriptQuery.EndStepIndexExclusive);
        }
        finally
        {
            DeleteScript(scriptPath);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FreeplayMode_RequiresExactlyOneBoundary(int boundaryCount)
    {
        var scriptPath = CreateScript(
            Enumerable.Repeat(ScriptCommandType.FreeplayBoundary, boundaryCount).ToArray());

        try
        {
            var strategy = new LoopStageAutoTaskStrategy();
            var state = new AutoTaskRuntimeState(new AutoTaskRequest
            {
                Kind = AutoTaskKind.LoopStage,
                StageTarget = CreateTarget(),
                PreferredScriptPath = scriptPath,
                LoopStageRunMode = LoopStageRunMode.FreeplayUntilRound,
                ExitAfterRound = 102
            });

            var decision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.MainMenu });

            Assert.Equal(AutoTaskDecisionKind.Fail, decision.Kind);
            Assert.Contains("exactly one FreeplayBoundary", decision.Description, StringComparison.Ordinal);
        }
        finally
        {
            DeleteScript(scriptPath);
        }
    }

    [Fact]
    public async Task StandardMode_DoesNotRestartCompletedScriptWhileStillInLevel()
    {
        var strategy = new LoopStageAutoTaskStrategy();
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.LoopStage,
            StageTarget = CreateTarget(),
            PreferredScriptPath = "standard-stage.btd"
        });
        state.SetProperty(
            LoopStageAutoTaskStateKeys.ResolvedScriptContext,
            new LoopStageAutoTaskScriptContext
            {
                Category = BlackBorderMapCategory.Beginner,
                Target = CreateTarget(),
                Hero = HeroType.Quincy,
                FilePath = "standard-stage.btd",
                FreeplayBoundaryIndex = -1
            });
        state.SetProperty(
            LoopStageAutoTaskStateKeys.ScriptRunState,
            LoopStageScriptRunState.FinishedCurrentStage);

        var decision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.InLevel });

        Assert.Equal(AutoTaskDecisionKind.Wait, decision.Kind);
        Assert.Equal(AutoTaskPhase.SettlingResult, decision.NextPhase);
    }

    [Fact]
    public async Task FreeplayMode_TreatsStageChallengeWithHintAsBlockingUi()
    {
        var strategy = new LoopStageAutoTaskStrategy();
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.LoopStage,
            StageTarget = CreateTarget(),
            PreferredScriptPath = "loop-stage.btd",
            LoopStageRunMode = LoopStageRunMode.FreeplayUntilRound,
            ExitAfterRound = 102
        });
        state.SetProperty(
            LoopStageAutoTaskStateKeys.ResolvedScriptContext,
            new LoopStageAutoTaskScriptContext
            {
                Category = BlackBorderMapCategory.Beginner,
                Target = CreateTarget(),
                Hero = HeroType.Quincy,
                FilePath = "loop-stage.btd",
                FreeplayBoundaryIndex = 1
            });
        state.SetProperty(
            LoopStageAutoTaskStateKeys.ScriptRunState,
            LoopStageScriptRunState.RunningAfterBoundary);
        state.SetProperty(
            LoopStageAutoTaskStateKeys.InterruptedUiState,
            GameUiStateId.StageChallengeWithHint);
        state.RecordScriptExecutionResult(CreateSuccessfulScriptResult());

        var decision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.StageChallengeWithHint });

        Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
        Assert.Equal(LoopStageScriptRunState.WaitingForBlockingUi, GetScriptRunState(state));
    }

    [Fact]
    public async Task FreeplayMode_TreatsStageChallengeWithHintAsTheFreeplayTransition()
    {
        var scriptPath = CreateScript(
            ScriptCommandType.Comment,
            ScriptCommandType.FreeplayBoundary,
            ScriptCommandType.Comment);

        try
        {
            var strategy = new LoopStageAutoTaskStrategy();
            var state = new AutoTaskRuntimeState(new AutoTaskRequest
            {
                Kind = AutoTaskKind.LoopStage,
                StageTarget = CreateTarget(),
                PreferredScriptPath = scriptPath,
                LoopStageRunMode = LoopStageRunMode.FreeplayUntilRound,
                ExitAfterRound = 102
            });

            await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.MainMenu });
            await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.InLevel });

            state.SetProperty(
                LoopStageAutoTaskStateKeys.InterruptedUiState,
                GameUiStateId.StageSettlement);
            state.RecordScriptExecutionResult(CreateSuccessfulScriptResult());

            var decision = await strategy.DecideNextAsync(
                state,
                new GameUiSnapshot { State = GameUiStateId.StageChallengeWithHint });

            Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
            Assert.Equal(LoopStageScriptRunState.WaitingForFreeplayPrompt, GetScriptRunState(state));
        }
        finally
        {
            DeleteScript(scriptPath);
        }
    }

    private static LoopStageScriptRunState GetScriptRunState(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<LoopStageScriptRunState>(
            LoopStageAutoTaskStateKeys.ScriptRunState,
            out var runState)
            ? runState
            : LoopStageScriptRunState.NotStarted;
    }

    private static string CreateScript(params ScriptCommandType[] commandTypes)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "BetterBTD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, "loop-stage.btd");
        ScriptDocumentService.Instance.Save(
            filePath,
            new ScriptDocument
            {
                Instructions = commandTypes
                    .Select(commandType => new ScriptInstructionDocument
                    {
                        CommandType = commandType.ToString()
                    })
                    .ToList()
            });
        return filePath;
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

    private static void DeleteScript(string filePath)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (directoryPath is not null && Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
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
}
