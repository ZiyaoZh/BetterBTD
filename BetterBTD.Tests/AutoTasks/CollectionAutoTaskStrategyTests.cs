using BetterBTD.Core.AutoTasks.Strategies;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Tests.AutoTasks;

public sealed class CollectionAutoTaskStrategyTests
{
    [Fact]
    public async Task DecideNextAsync_DoesNotRestartScript_AfterScriptAlreadyCompletedInLevel()
    {
        var strategy = new CollectionAutoTaskStrategy();
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = CreateTarget(),
            PreferredScriptPath = "collection-stage.json"
        });

        state.SetProperty(
            CollectionAutoTaskStateKeys.ResolvedScriptContext,
            new CollectionAutoTaskScriptContext
            {
                Map = GameMapType.DarkCastle,
                Difficulty = StageDifficulty.Hard,
                Mode = StageMode.Standard,
                Hero = HeroType.Quincy,
                FilePath = "collection-stage.json"
            });
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

    private static StageEntryTarget CreateTarget()
    {
        return new StageEntryTarget
        {
            Map = GameMapType.DarkCastle,
            Difficulty = StageDifficulty.Hard,
            Mode = StageMode.Standard
        };
    }
}
