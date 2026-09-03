using BetterBTD.Core.AutoTasks;
using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class NavigationCoordinationContractTests
{
    [Fact]
    public void NavigationObservation_PreservesSequenceTimestampAndSnapshot()
    {
        var capturedAt = new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
        var snapshot = new GameUiSnapshot
        {
            CapturedAt = capturedAt,
            State = GameUiStateId.InLevel
        };

        var observation = new NavigationObservation(42, capturedAt, snapshot);

        Assert.Equal(42, observation.Sequence);
        Assert.Equal(capturedAt, observation.CapturedAt);
        Assert.Same(snapshot, observation.Snapshot);
    }

    [Fact]
    public void NavigationObservation_RejectsNonPositiveSequence()
    {
        var snapshot = new GameUiSnapshot();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NavigationObservation(0, snapshot.CapturedAt, snapshot));
    }

    [Fact]
    public void StageChallengeTransitions_MatchesDefinedTransitionGraph()
    {
        var allowed = new HashSet<(StageChallengeState From, StageChallengeState To)>
        {
            (StageChallengeState.Navigating, StageChallengeState.InLevel),
            (StageChallengeState.InLevel, StageChallengeState.OffLevelGrace),
            (StageChallengeState.InLevel, StageChallengeState.NavigationFallback),
            (StageChallengeState.OffLevelGrace, StageChallengeState.InLevel),
            (StageChallengeState.OffLevelGrace, StageChallengeState.PausingForRecovery),
            (StageChallengeState.OffLevelGrace, StageChallengeState.NavigationFallback),
            (StageChallengeState.PausingForRecovery, StageChallengeState.Recovering),
            (StageChallengeState.PausingForRecovery, StageChallengeState.InLevel),
            (StageChallengeState.PausingForRecovery, StageChallengeState.NavigationFallback),
            (StageChallengeState.Recovering, StageChallengeState.Resuming),
            (StageChallengeState.Recovering, StageChallengeState.InLevel),
            (StageChallengeState.Recovering, StageChallengeState.NavigationFallback),
            (StageChallengeState.Resuming, StageChallengeState.InLevel)
        };
        var nonTerminalStates = Enum.GetValues<StageChallengeState>()
            .Except([StageChallengeState.NavigationFallback, StageChallengeState.Failed, StageChallengeState.Cancelled]);
        foreach (var state in nonTerminalStates)
        {
            allowed.Add((state, StageChallengeState.Failed));
            allowed.Add((state, StageChallengeState.Cancelled));
        }

        foreach (var from in Enum.GetValues<StageChallengeState>())
        {
            foreach (var to in Enum.GetValues<StageChallengeState>())
            {
                Assert.Equal(allowed.Contains((from, to)), StageChallengeStateTransitions.CanTransition(from, to));
            }
        }
    }

    [Fact]
    public void StageChallengeTransition_RecordsReasonTimeAndNavigationSequence()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 3, 10, 1, 0, TimeSpan.Zero);

        var transition = StageChallengeStateTransitions.Create(
            StageChallengeState.OffLevelGrace,
            StageChallengeState.PausingForRecovery,
            "level-up popup detected",
            occurredAt,
            23);

        Assert.Equal(StageChallengeState.OffLevelGrace, transition.PreviousState);
        Assert.Equal(StageChallengeState.PausingForRecovery, transition.CurrentState);
        Assert.Equal("level-up popup detected", transition.Reason);
        Assert.Equal(occurredAt, transition.OccurredAt);
        Assert.Equal(23, transition.NavigationSequence);
    }

    [Fact]
    public void WorkerCompletion_IsIndependentFromNavigationState()
    {
        Assert.True(Enum.IsDefined(StageChallengeState.InLevel));
        Assert.DoesNotContain(
            Enum.GetNames<StageChallengeState>(),
            name => name.Contains("Result", StringComparison.Ordinal));
    }
}
