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
            (StageChallengeState.Preparing, StageChallengeState.EnteringStage),
            (StageChallengeState.EnteringStage, StageChallengeState.InStageBeforeScript),
            (StageChallengeState.EnteringStage, StageChallengeState.HandlingPopup),
            (StageChallengeState.InStageBeforeScript, StageChallengeState.ScriptRunning),
            (StageChallengeState.InStageBeforeScript, StageChallengeState.ResultDetected),
            (StageChallengeState.InStageBeforeScript, StageChallengeState.HandlingPopup),
            (StageChallengeState.ScriptRunning, StageChallengeState.ScriptCompletedWaitingForResult),
            (StageChallengeState.ScriptRunning, StageChallengeState.ResultDetected),
            (StageChallengeState.ScriptRunning, StageChallengeState.HandlingPopup),
            (StageChallengeState.ScriptCompletedWaitingForResult, StageChallengeState.ResultDetected),
            (StageChallengeState.ScriptCompletedWaitingForResult, StageChallengeState.HandlingPopup),
            (StageChallengeState.ResultDetected, StageChallengeState.HandlingVictory),
            (StageChallengeState.ResultDetected, StageChallengeState.HandlingDefeat),
            (StageChallengeState.HandlingPopup, StageChallengeState.EnteringStage),
            (StageChallengeState.HandlingPopup, StageChallengeState.InStageBeforeScript),
            (StageChallengeState.HandlingPopup, StageChallengeState.ScriptRunning),
            (StageChallengeState.HandlingPopup, StageChallengeState.ScriptCompletedWaitingForResult),
            (StageChallengeState.HandlingPopup, StageChallengeState.ResultDetected),
            (StageChallengeState.HandlingVictory, StageChallengeState.Completed),
            (StageChallengeState.HandlingDefeat, StageChallengeState.Completed)
        };
        var nonTerminalStates = Enum.GetValues<StageChallengeState>()
            .Except([StageChallengeState.Completed, StageChallengeState.Failed, StageChallengeState.Cancelled]);
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
            StageChallengeState.ScriptRunning,
            StageChallengeState.HandlingPopup,
            "level-up popup detected",
            occurredAt,
            23);

        Assert.Equal(StageChallengeState.ScriptRunning, transition.PreviousState);
        Assert.Equal(StageChallengeState.HandlingPopup, transition.CurrentState);
        Assert.Equal("level-up popup detected", transition.Reason);
        Assert.Equal(occurredAt, transition.OccurredAt);
        Assert.Equal(23, transition.NavigationSequence);
    }

    [Fact]
    public void WorkerCompletion_DoesNotCompleteStageChallenge()
    {
        Assert.False(StageChallengeStateTransitions.CanTransition(
            StageChallengeState.ScriptCompletedWaitingForResult,
            StageChallengeState.Completed));
    }
}
