using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Core.AutoTasks.Strategies;

public sealed class RaceAutoTaskStrategy : StageNavigationAutoTaskStrategyBase
{
    public override AutoTaskKind Kind => AutoTaskKind.Race;

    protected override AutoTaskScriptQuery BuildScriptQuery(AutoTaskRuntimeState state)
    {
        return new AutoTaskScriptQuery
        {
            Kind = Kind,
            StageTarget = state.Request.StageTarget,
            SlotId = ManagedScriptSlotIdFactory.CreateRaceCurrentSlotId(),
            RequiredTags = ["race"],
            Description = "Resolve a race-mode script for the selected activity."
        };
    }

    protected override AutoTaskDecision DecideAfterScriptExecution(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        state.ClearPendingScriptOutcome();
        return AutoTaskDecision.Fail("Race task post-stage progression is not implemented yet.");
    }
}
