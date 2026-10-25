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
    public async Task InputReleaseFailure_PoisonsLeaseWithoutOverridingExecutionResult()
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

        var result = await executor.ExecuteAsync(taskFlow, options);

        Assert.Equal(ScriptExecutionStatus.Completed, result.Status);
        Assert.Equal(1, input.ReleaseAllKeysCallCount);
        Assert.True(leaseCoordinator.IsPoisoned);
        Assert.False(leaseCoordinator.HasActiveLease);
        Assert.False(executor.IsRunning);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesOnlyRequestedSegmentIncludingFreeplayBoundary()
    {
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var input = new RecordingScriptInputService();
        var executor = new ScriptTaskFlowExecutor(leaseCoordinator);
        var instructions = new[]
        {
            new ScriptInstructionDocument { CommandType = ScriptCommandType.Comment.ToString() },
            new ScriptInstructionDocument { CommandType = ScriptCommandType.FreeplayBoundary.ToString() },
            new ScriptInstructionDocument { CommandType = ScriptCommandType.Comment.ToString() }
        };
        var taskFlow = new ScriptTaskFlow
        {
            SourceFilePath = "freeplay-boundary-test.json",
            Document = new ScriptDocument { Instructions = [.. instructions] },
            Steps =
            [
                new ScriptTaskFlowStep { Index = 0, CommandType = ScriptCommandType.Comment, Instruction = instructions[0] },
                new ScriptTaskFlowStep { Index = 1, CommandType = ScriptCommandType.FreeplayBoundary, Instruction = instructions[1] },
                new ScriptTaskFlowStep { Index = 2, CommandType = ScriptCommandType.Comment, Instruction = instructions[2] }
            ],
            MonkeyObjectsByBindingId = new Dictionary<string, ScriptMonkeyObjectDocument>()
        };

        var result = await executor.ExecuteAsync(
            taskFlow,
            new ScriptExecutionOptions
            {
                StartStepIndex = 1,
                EndStepIndexExclusive = 2,
                RequireCaptureService = false,
                RequireTargetWindow = false,
                RuntimeServices = new ScriptExecutionRuntimeServices
                {
                    Capture = new NullScriptCaptureService(),
                    Input = input,
                    GameStageState = new QueueGameStageStateService([null])
                }
            });

        Assert.Equal(ScriptExecutionStatus.Completed, result.Status);
        Assert.Equal(1, result.ExecutedStepCount);
        Assert.Equal(1, result.LastCompletedStepIndex);
        Assert.Empty(input.Clicks);
        Assert.Empty(input.PressedKeys);
        Assert.False(leaseCoordinator.HasActiveLease);
    }

    [Fact]
    public async Task InstructionFailure_PreservesDiagnosticMessageAndPropagatesLocalizationMetadata()
    {
        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.Wait.ToString(),
            WaitMode = WaitModeType.CoordinateColor.ToString(),
            WaitColorHex = "invalid-color"
        };

        var result = await ExecuteSingleInstructionAsync(instruction, new NullScriptCaptureService());

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        var failure = Assert.IsType<ScriptExecutionFailureDetails>(result.Failure);
        Assert.Equal(0, failure.StepIndex);
        Assert.Equal("WaitColorHex", failure.Checkpoint);
        Assert.Equal("Unsupported wait color 'invalid-color'. Expected format '#RRGGBB'.", failure.Message);
        Assert.Equal("Editor.Runtime.Error.InvalidWaitColor", failure.MessageKey);
        Assert.Equal(new object?[] { "invalid-color" }, failure.MessageArguments);
        var exception = Assert.IsType<ScriptExecutionException>(result.Exception);
        Assert.Equal(exception.Message, failure.Message);
        Assert.Equal(exception.MessageKey, failure.MessageKey);
        Assert.Equal(exception.MessageArguments, failure.MessageArguments);
    }

    [Fact]
    public async Task RuntimeFailureWithoutLocalizationMetadata_PreservesOriginalExceptionAndMessage()
    {
        var expectedException = new InvalidOperationException("Capture backend failed.");
        var instruction = new ScriptInstructionDocument { CommandType = ScriptCommandType.Comment.ToString() };

        var result = await ExecuteSingleInstructionAsync(instruction, new FailingCaptureService(expectedException));

        Assert.Equal(ScriptExecutionStatus.Failed, result.Status);
        Assert.Same(expectedException, result.Exception);
        var failure = Assert.IsType<ScriptExecutionFailureDetails>(result.Failure);
        Assert.Equal(expectedException.Message, failure.Message);
        Assert.Empty(failure.MessageKey);
        Assert.Empty(failure.MessageArguments);
    }

    private static Task<ScriptExecutionResult> ExecuteSingleInstructionAsync(
        ScriptInstructionDocument instruction,
        IScriptCaptureService capture)
    {
        var taskFlow = ScriptTaskFlowService.Instance.Build(new ScriptDocument { Instructions = [instruction] });
        return new ScriptTaskFlowExecutor(new GameControlLeaseCoordinator()).ExecuteAsync(
            taskFlow,
            new ScriptExecutionOptions
            {
                RequireCaptureService = true,
                RequireTargetWindow = false,
                RuntimeServices = new ScriptExecutionRuntimeServices
                {
                    Capture = capture,
                    Input = new RecordingScriptInputService(),
                    GameStageState = new QueueGameStageStateService([])
                }
            });
    }

    private sealed class FailingCaptureService(Exception exception) : IScriptCaptureService
    {
        public bool IsRunning => throw exception;

        public bool TryGetCurrentWindowInfo(out BetterBTD.Models.GameWindowInfo windowInfo)
            => throw new NotSupportedException();

        public bool TryCaptureFrame(out BetterBTD.Models.GameWindowInfo windowInfo, out OpenCvSharp.Mat frame)
            => throw new NotSupportedException();
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
