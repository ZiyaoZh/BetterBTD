using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Core.AutoTasks;

public static class StageChallengeStateTransitions
{
    private static readonly IReadOnlyDictionary<StageChallengeState, IReadOnlySet<StageChallengeState>> Allowed =
        new Dictionary<StageChallengeState, IReadOnlySet<StageChallengeState>>
        {
            [StageChallengeState.Navigating] = States(
                StageChallengeState.InLevel,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.InLevel] = States(
                StageChallengeState.OffLevelGrace,
                StageChallengeState.NavigationFallback,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.OffLevelGrace] = States(
                StageChallengeState.InLevel,
                StageChallengeState.PausingForRecovery,
                StageChallengeState.NavigationFallback,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.PausingForRecovery] = States(
                StageChallengeState.InLevel,
                StageChallengeState.Recovering,
                StageChallengeState.NavigationFallback,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.Recovering] = States(
                StageChallengeState.InLevel,
                StageChallengeState.Resuming,
                StageChallengeState.NavigationFallback,
                StageChallengeState.Failed,
                StageChallengeState.Cancelled),
            [StageChallengeState.Resuming] = States(
                StageChallengeState.InLevel,
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
