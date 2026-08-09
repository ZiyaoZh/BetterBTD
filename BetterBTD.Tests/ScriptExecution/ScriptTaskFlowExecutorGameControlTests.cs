using BetterBTD.Core.GameControl;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Tests.TestDoubles;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptTaskFlowExecutorGameControlTests
{
    [Fact]
    public async Task RequestStop_CancelsRunningScriptBeforeReleasingLease()
    {
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var input = new RecordingScriptInputService();
        var executor = new ScriptTaskFlowExecutor(leaseCoordinator);
        var waitInstruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.Wait.ToString(),
            WaitMode = WaitModeType.Time.ToString(),
            WaitTimeMilliseconds = int.MaxValue
        };
        var taskFlow = new ScriptTaskFlow
        {
            SourceFilePath = "wait-test.json",
            Document = new ScriptDocument
            {
                Instructions = [waitInstruction]
            },
            Steps =
            [
                new ScriptTaskFlowStep
                {
                    Index = 0,
                    CommandType = ScriptCommandType.Wait,
                    Instruction = waitInstruction
                }
            ],
            MonkeyObjectsByBindingId = new Dictionary<string, ScriptMonkeyObjectDocument>()
        };
        var options = new ScriptExecutionOptions
        {
            RequireCaptureService = false,
            RequireTargetWindow = false,
            RuntimeServices = new ScriptExecutionRuntimeServices
            {
                Capture = new NullScriptCaptureService(),
                Input = input,
                GameStageState = new QueueGameStageStateService([null])
            }
        };

        var executionTask = executor.ExecuteAsync(taskFlow, options);
        await WaitUntilAsync(() => executor.IsRunning);

        Assert.True(executor.RequestStop());
        var result = await executionTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ScriptExecutionStatus.Cancelled, result.Status);
        Assert.False(executor.IsRunning);
        Assert.False(leaseCoordinator.HasActiveLease);
        Assert.Equal(1, input.ReleaseAllKeysCallCount);
    }

    [Fact]
    public async Task InputReleaseFailure_PoisonsLeaseAfterExecutorCleanup()
    {
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var input = new RecordingScriptInputService
        {
            ReleaseAllKeysException = new InvalidOperationException("release failed")
        };
        var executor = new ScriptTaskFlowExecutor(leaseCoordinator);
        var taskFlow = new ScriptTaskFlow
        {
            SourceFilePath = "empty-test.json",
            Document = new ScriptDocument(),
            Steps = [],
            MonkeyObjectsByBindingId = new Dictionary<string, ScriptMonkeyObjectDocument>()
        };
        var options = new ScriptExecutionOptions
        {
            RequireCaptureService = false,
            RequireTargetWindow = false,
            RuntimeServices = new ScriptExecutionRuntimeServices
            {
                Capture = new NullScriptCaptureService(),
                Input = input,
                GameStageState = new QueueGameStageStateService([null])
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(taskFlow, options));

        Assert.Equal("release failed", exception.Message);
        Assert.Equal(1, input.ReleaseAllKeysCallCount);
        Assert.True(leaseCoordinator.IsPoisoned);
        Assert.False(leaseCoordinator.HasActiveLease);
        Assert.False(executor.IsRunning);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(10, cancellationSource.Token);
        }
    }
}
