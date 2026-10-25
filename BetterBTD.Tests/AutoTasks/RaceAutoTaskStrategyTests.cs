using BetterBTD.Core.AutoTasks.Strategies;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Tests.AutoTasks;

public sealed class RaceAutoTaskStrategyTests
{
    [Fact]
    public async Task DecideNextAsync_InLevel_StartsConfiguredRaceScript()
    {
        var strategy = new RaceAutoTaskStrategy();
        var state = CreateState("race-stage.btd6");

        var decision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.InLevel });

        Assert.Equal(AutoTaskDecisionKind.StartScriptExecution, decision.Kind);
        Assert.Equal(AutoTaskPhase.ExecutingScript, decision.NextPhase);
        Assert.NotNull(decision.ScriptQuery);
        Assert.Equal(AutoTaskKind.Race, decision.ScriptQuery.Kind);
        Assert.Equal("race-stage.btd6", decision.ScriptQuery.PreferredFilePath);
        Assert.True(state.TryGetProperty<RaceAutoTaskScriptRunState>(
            RaceAutoTaskStateKeys.ScriptRunState,
            out var runState));
        Assert.Equal(RaceAutoTaskScriptRunState.Running, runState);
    }

    [Fact]
    public async Task DecideNextAsync_MainMenu_FailsWithRaceEntryPrompt()
    {
        var strategy = new RaceAutoTaskStrategy();
        var state = CreateState("race-stage.btd6");

        var decision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.MainMenu });

        Assert.Equal(AutoTaskDecisionKind.Fail, decision.Kind);
        Assert.Contains("Enter the race stage", decision.Description);
    }

    [Fact]
    public async Task DecideNextAsync_LoadingBeforeRaceUi_FailsWithRaceEntryPrompt()
    {
        var strategy = new RaceAutoTaskStrategy();
        var state = CreateState("race-stage.btd6");

        var decision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.Loading });

        Assert.Equal(AutoTaskDecisionKind.Fail, decision.Kind);
        Assert.Contains("Enter the race stage", decision.Description);
    }

    [Fact]
    public async Task DecideNextAsync_DoesNotRestartScript_AfterScriptAlreadyCompletedInLevel()
    {
        var strategy = new RaceAutoTaskStrategy();
        var state = CreateState("race-stage.btd6");
        state.RecordScriptExecutionResult(new ScriptExecutionResult
        {
            Status = ScriptExecutionStatus.Completed,
            ExecutedStepCount = 1,
            LastCompletedStepIndex = 0,
            FinalProgress = new ScriptExecutionProgressSnapshot()
        });

        var firstDecision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.InLevel });
        var secondDecision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = GameUiStateId.InLevel });

        Assert.Equal(AutoTaskDecisionKind.Wait, firstDecision.Kind);
        Assert.Equal(AutoTaskPhase.SettlingResult, firstDecision.NextPhase);
        Assert.Equal(AutoTaskDecisionKind.Wait, secondDecision.Kind);
        Assert.Equal(AutoTaskPhase.SettlingResult, secondDecision.NextPhase);
    }

    [Theory]
    [InlineData(GameUiStateId.StageSettlement)]
    [InlineData(GameUiStateId.StageSettings)]
    [InlineData(GameUiStateId.StageHint)]
    [InlineData(GameUiStateId.NetworkUnavailableDialog)]
    public async Task DecideNextAsync_HandledOverlayScreen_RequestsHandledOverlayNavigation(
        GameUiStateId uiState)
    {
        var strategy = new RaceAutoTaskStrategy();
        var state = CreateState("race-stage.btd6");
        state.SetProperty(RaceAutoTaskStateKeys.ScriptRunState, RaceAutoTaskScriptRunState.Running);

        var decision = await strategy.DecideNextAsync(
            state,
            new GameUiSnapshot { State = uiState });

        Assert.Equal(AutoTaskDecisionKind.Navigate, decision.Kind);
        Assert.Equal(AutoTaskPhase.AdvancingObjective, decision.NextPhase);
        Assert.True(state.TryGetProperty<RaceAutoTaskScriptRunState>(
            RaceAutoTaskStateKeys.ScriptRunState,
            out var runState));
        Assert.Equal(RaceAutoTaskScriptRunState.NotStarted, runState);
    }

    [Theory]
    [InlineData(GameUiStateId.StageHint)]
    [InlineData(GameUiStateId.NetworkUnavailableDialog)]
    public async Task DecideNextAsync_AfterScriptExecution_ConfirmationResetsAttemptAndAllowsRestart(
        GameUiStateId uiState)
    {
        var strategy = new RaceAutoTaskStrategy();
        var state = CreateState("race-stage.btd6");
        await strategy.DecideNextAsync(state, new GameUiSnapshot { State = GameUiStateId.InLevel });
        state.RecordScriptExecutionResult(new ScriptExecutionResult
        {
            Status = ScriptExecutionStatus.Completed,
            ExecutedStepCount = 1,
            LastCompletedStepIndex = 0,
            FinalProgress = new ScriptExecutionProgressSnapshot()
        });

        var confirmation = await strategy.DecideNextAsync(state, new GameUiSnapshot { State = uiState });

        Assert.Equal(AutoTaskDecisionKind.Navigate, confirmation.Kind);
        Assert.Equal(AutoTaskPhase.AdvancingObjective, confirmation.NextPhase);
        Assert.False(state.HasPendingScriptOutcome);
        Assert.True(state.TryGetProperty<RaceAutoTaskScriptRunState>(
            RaceAutoTaskStateKeys.ScriptRunState, out var runState));
        Assert.Equal(RaceAutoTaskScriptRunState.NotStarted, runState);

        var restart = await strategy.DecideNextAsync(state, new GameUiSnapshot { State = GameUiStateId.InLevel });

        Assert.Equal(AutoTaskDecisionKind.StartScriptExecution, restart.Kind);
    }

    private static AutoTaskRuntimeState CreateState(string scriptPath)
    {
        return new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.Race,
            StageTarget = new StageEntryTarget
            {
                Map = GameMapType.MonkeyMeadow,
                Difficulty = StageDifficulty.Easy,
                Mode = StageMode.Standard
            },
            PreferredScriptPath = scriptPath
        });
    }
}
