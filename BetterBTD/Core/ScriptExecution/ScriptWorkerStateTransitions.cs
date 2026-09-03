using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.ScriptExecution;

public static class ScriptWorkerStateTransitions
{
    private static readonly IReadOnlyDictionary<ScriptWorkerState, IReadOnlySet<ScriptWorkerState>> Allowed =
        new Dictionary<ScriptWorkerState, IReadOnlySet<ScriptWorkerState>>
        {
            [ScriptWorkerState.NotStarted] = States(
                ScriptWorkerState.Starting,
                ScriptWorkerState.CancellationRequested),
            [ScriptWorkerState.Starting] = States(
                ScriptWorkerState.Running,
                ScriptWorkerState.CancellationRequested,
                ScriptWorkerState.Failed),
            [ScriptWorkerState.Running] = States(
                ScriptWorkerState.Pausing,
                ScriptWorkerState.CancellationRequested,
                ScriptWorkerState.Completed,
                ScriptWorkerState.Failed),
            [ScriptWorkerState.Pausing] = States(
                ScriptWorkerState.Paused,
                ScriptWorkerState.CancellationRequested,
                ScriptWorkerState.Completed,
                ScriptWorkerState.Failed),
            [ScriptWorkerState.Paused] = States(
                ScriptWorkerState.Running,
                ScriptWorkerState.CancellationRequested,
                ScriptWorkerState.Failed),
            [ScriptWorkerState.CancellationRequested] = States(
                ScriptWorkerState.Completed,
                ScriptWorkerState.Cancelled,
                ScriptWorkerState.Failed)
        };

    public static bool CanTransition(ScriptWorkerState from, ScriptWorkerState to)
    {
        return Allowed.TryGetValue(from, out var nextStates) && nextStates.Contains(to);
    }

    public static ScriptWorkerStateTransition Create(
        ScriptWorkerState from,
        ScriptWorkerState to,
        string reason,
        DateTimeOffset occurredAt,
        long navigationSequence)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Script worker cannot transition from {from} to {to}.");
        }

        return new ScriptWorkerStateTransition(from, to, reason, occurredAt, navigationSequence);
    }

    private static IReadOnlySet<ScriptWorkerState> States(params ScriptWorkerState[] states)
    {
        return new HashSet<ScriptWorkerState>(states);
    }
}
