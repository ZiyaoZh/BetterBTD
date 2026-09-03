using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Core.AutoTasks;

public static class StageChallengeStateTransitions
{
    private static readonly IReadOnlyDictionary<StageChallengeState, IReadOnlySet<StageChallengeState>> Allowed =
        new Dictionary<StageChallengeState, IReadOnlySet<StageChallengeState>>
        {
            [StageChallengeState.Preparing] = States(
                StageChallengeState.EnteringStage,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.EnteringStage] = States(
                StageChallengeState.InStageBeforeScript,
                StageChallengeState.HandlingPopup,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.InStageBeforeScript] = States(
                StageChallengeState.ScriptRunning,
                StageChallengeState.ResultDetected,
                StageChallengeState.HandlingPopup,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.ScriptRunning] = States(
                StageChallengeState.ScriptCompletedWaitingForResult,
                StageChallengeState.ResultDetected,
                StageChallengeState.HandlingPopup,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.ScriptCompletedWaitingForResult] = States(
                StageChallengeState.ResultDetected,
                StageChallengeState.HandlingPopup,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.ResultDetected] = States(
                StageChallengeState.HandlingVictory,
                StageChallengeState.HandlingDefeat,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.HandlingPopup] = States(
                StageChallengeState.EnteringStage,
                StageChallengeState.InStageBeforeScript,
                StageChallengeState.ScriptRunning,
                StageChallengeState.ScriptCompletedWaitingForResult,
                StageChallengeState.ResultDetected,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.HandlingVictory] = States(
                StageChallengeState.Completed,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.HandlingDefeat] = States(
                StageChallengeState.Completed,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled)
        };

    public static bool CanTransition(StageChallengeState from, StageChallengeState to)
    {
        return Allowed.TryGetValue(from, out var nextStates) && nextStates.Contains(to);
    }

    public static StageChallengeStateTransition Create(
        StageChallengeState from,
        StageChallengeState to,
        string reason,
        DateTimeOffset occurredAt,
        long navigationSequence)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Stage challenge cannot transition from {from} to {to}.");
        }

        return new StageChallengeStateTransition(from, to, reason, occurredAt, navigationSequence);
    }

    private static IReadOnlySet<StageChallengeState> States(params StageChallengeState[] states)
    {
        return new HashSet<StageChallengeState>(states);
    }
}
