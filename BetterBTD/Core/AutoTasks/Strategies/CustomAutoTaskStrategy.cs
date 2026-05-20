using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Core.AutoTasks.Strategies;

public sealed class CustomAutoTaskStrategy : StageNavigationAutoTaskStrategyBase
{
    public override AutoTaskKind Kind => AutoTaskKind.Custom;

    protected override AutoTaskScriptQuery BuildScriptQuery(AutoTaskRuntimeState state)
    {
        return new AutoTaskScriptQuery
        {
            Kind = Kind,
            StageTarget = state.Request.StageTarget,
            PreferredFilePath = state.Request.PreferredScriptPath,
            SlotId = ManagedScriptSlotIdFactory.CreateCustomDefaultSlotId(),
            Description = "Resolve the preferred custom script for the selected stage."
        };
    }

    protected override AutoTaskDecision DecideAfterScriptExecution(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        state.ClearPendingScriptOutcome();
        return AutoTaskDecision.Complete("Custom auto task completed after script execution.");
    }
}
