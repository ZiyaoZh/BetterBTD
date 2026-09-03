using BetterBTD.Core.ScriptExecution;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptWorkerContractTests
{
    [Fact]
    public void StartCommand_CarriesExecutionRequestAndCorrelationFields()
    {
        var runId = Guid.NewGuid();
        var options = new ScriptExecutionOptions { StartStepIndex = 3 };
        using var cancellationSource = new CancellationTokenSource();

        var command = new ScriptWorkerCommand(
            ScriptWorkerCommandKind.Start,
            runId,
            17,
            "stage is ready",
            cancellationSource.Token,
            waitForAcknowledgement: true,
            new ScriptWorkerStartRequest("stage.json", options));

        Assert.Equal(runId, command.RunId);
        Assert.Equal(17, command.RequestSequence);
        Assert.Equal(cancellationSource.Token, command.CancellationToken);
        Assert.Equal("stage.json", command.StartRequest?.FilePath);
        Assert.Same(options, command.StartRequest?.Options);
    }

    [Fact]
    public void NonStartCommand_RejectsStartRequest()
    {
        Assert.Throws<ArgumentException>(() => new ScriptWorkerCommand(
            ScriptWorkerCommandKind.Pause,
            Guid.NewGuid(),
            1,
            "popup detected",
            CancellationToken.None,
            waitForAcknowledgement: true,
            new ScriptWorkerStartRequest("stage.json", new ScriptExecutionOptions())));
    }

    [Fact]
    public void AcknowledgementEvent_RequiresAndPreservesRequestSequence()
    {
        var workerEvent = new ScriptWorkerEvent(
            ScriptWorkerEventKind.PauseAcknowledged,
            Guid.NewGuid(),
            ScriptWorkerState.Paused,
            DateTimeOffset.UtcNow,
            requestSequence: 31);

        Assert.Equal(31, workerEvent.RequestSequence);
        Assert.Throws<ArgumentException>(() => new ScriptWorkerEvent(
            ScriptWorkerEventKind.ResumeAcknowledged,
            Guid.NewGuid(),
            ScriptWorkerState.Running,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FailedEvent_RequiresError()
    {
        Assert.Throws<ArgumentException>(() => new ScriptWorkerEvent(
            ScriptWorkerEventKind.Failed,
            Guid.NewGuid(),
            ScriptWorkerState.Failed,
            DateTimeOffset.UtcNow));

        var error = new InvalidOperationException("capture failed");
        var workerEvent = new ScriptWorkerEvent(
            ScriptWorkerEventKind.Failed,
            Guid.NewGuid(),
            ScriptWorkerState.Failed,
            DateTimeOffset.UtcNow,
            error: error);

        Assert.Same(error, workerEvent.Error);
    }

    [Fact]
    public void ScriptWorkerTransitions_MatchesDefinedTransitionGraph()
    {
        var allowed = new HashSet<(ScriptWorkerState From, ScriptWorkerState To)>
        {
            (ScriptWorkerState.NotStarted, ScriptWorkerState.Starting),
            (ScriptWorkerState.NotStarted, ScriptWorkerState.CancellationRequested),
            (ScriptWorkerState.Starting, ScriptWorkerState.Running),
            (ScriptWorkerState.Starting, ScriptWorkerState.CancellationRequested),
            (ScriptWorkerState.Starting, ScriptWorkerState.Failed),
            (ScriptWorkerState.Running, ScriptWorkerState.Pausing),
            (ScriptWorkerState.Running, ScriptWorkerState.CancellationRequested),
            (ScriptWorkerState.Running, ScriptWorkerState.Completed),
            (ScriptWorkerState.Running, ScriptWorkerState.Failed),
            (ScriptWorkerState.Pausing, ScriptWorkerState.Paused),
            (ScriptWorkerState.Pausing, ScriptWorkerState.CancellationRequested),
            (ScriptWorkerState.Pausing, ScriptWorkerState.Completed),
            (ScriptWorkerState.Pausing, ScriptWorkerState.Failed),
            (ScriptWorkerState.Paused, ScriptWorkerState.Running),
            (ScriptWorkerState.Paused, ScriptWorkerState.CancellationRequested),
            (ScriptWorkerState.Paused, ScriptWorkerState.Failed),
            (ScriptWorkerState.CancellationRequested, ScriptWorkerState.Completed),
            (ScriptWorkerState.CancellationRequested, ScriptWorkerState.Cancelled),
            (ScriptWorkerState.CancellationRequested, ScriptWorkerState.Failed)
        };

        foreach (var from in Enum.GetValues<ScriptWorkerState>())
        {
            foreach (var to in Enum.GetValues<ScriptWorkerState>())
            {
                Assert.Equal(allowed.Contains((from, to)), ScriptWorkerStateTransitions.CanTransition(from, to));
            }
        }
    }

    [Theory]
    [InlineData(ScriptWorkerState.Completed)]
    [InlineData(ScriptWorkerState.Paused)]
    [InlineData(ScriptWorkerState.CancellationRequested)]
    public void RequiredWorkerStates_CanBeRepresentedAlongsideStageState(ScriptWorkerState workerState)
    {
        var stageState = workerState switch
        {
            ScriptWorkerState.Completed => BetterBTD.Models.AutoTasks.StageChallengeState.ScriptCompletedWaitingForResult,
            ScriptWorkerState.Paused => BetterBTD.Models.AutoTasks.StageChallengeState.HandlingPopup,
            _ => BetterBTD.Models.AutoTasks.StageChallengeState.HandlingVictory
        };

        Assert.True(Enum.IsDefined(stageState));
        Assert.True(Enum.IsDefined(workerState));
    }
}
