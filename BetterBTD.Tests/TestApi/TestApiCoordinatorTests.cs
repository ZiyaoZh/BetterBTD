using BetterBTD.Core.GameControl;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.TestApi;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Models.TestApi;

namespace BetterBTD.Tests.TestApi;

public sealed class TestApiCoordinatorTests
{
    [Fact]
    public async Task StartCapture_AlreadyRunningWithMatchingSettings_IsIdempotent()
    {
        var runtime = new FakeTestApiRuntimeEnvironment();
        var coordinator = CreateCoordinator(runtime);

        var response = await coordinator.StartCaptureAsync(
            new TestApiCaptureStartRequest
            {
                CaptureModeName = "TestCapture",
                CaptureIntervalMs = 50
            },
            CancellationToken.None);

        Assert.True(response.Started);
        Assert.True(response.AlreadyRunning);
        Assert.True(response.NonOracleDiagnostics.Capture.IsRunning);
    }

    [Fact]
    public async Task StartCapture_AlreadyRunningWithDifferentSettings_ReturnsBusy()
    {
        var runtime = new FakeTestApiRuntimeEnvironment();
        var coordinator = CreateCoordinator(runtime);

        var exception = await Assert.ThrowsAsync<TestApiRequestException>(() => coordinator.StartCaptureAsync(
            new TestApiCaptureStartRequest
            {
                CaptureIntervalMs = 100
            },
            CancellationToken.None));

        Assert.Equal(TestApiErrorCodes.Busy, exception.Code);
    }

    [Fact]
    public async Task StartCapture_InProgress_BlocksConcurrentCaptureAndExecute()
    {
        var scriptPath = CreateTemporaryScriptFile("capture-race");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment
            {
                IsCaptureRunning = false,
                CaptureStartRelease = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var coordinator = CreateCoordinator(runtime);
            var captureTask = coordinator.StartCaptureAsync(
                new TestApiCaptureStartRequest(),
                CancellationToken.None);
            await runtime.CaptureStartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var captureException = await Assert.ThrowsAsync<TestApiRequestException>(() =>
                coordinator.StartCaptureAsync(new TestApiCaptureStartRequest(), CancellationToken.None));
            var executeException = Assert.Throws<TestApiRequestException>(() => coordinator.ExecuteScript(
                new TestApiScriptExecuteRequest
                {
                    ScriptPath = scriptPath
                }));

            Assert.Equal(TestApiErrorCodes.Busy, captureException.Code);
            Assert.Equal(TestApiErrorCodes.Busy, executeException.Code);
            runtime.CaptureStartRelease.SetResult();
            var capture = await captureTask;
            Assert.True(capture.Started);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void ValidateScript_ReturnsStableDigestAndNonOracleSummary()
    {
        var scriptPath = CreateTemporaryScriptFile("validation");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment();
            var coordinator = CreateCoordinator(runtime);

            var response = coordinator.ValidateScript(new TestApiScriptPathRequest
            {
                ScriptPath = scriptPath
            });

            Assert.True(response.IsValid);
            Assert.Equal(64, response.Sha256.Length);
            Assert.Equal(1, response.NonOracleDiagnostics.StepCount);
            Assert.Equal("test-script", response.NonOracleDiagnostics.ScriptId);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task ExecuteScript_TracksPauseLogsCancellationAndRecoverGate()
    {
        var scriptPath = CreateTemporaryScriptFile("operation");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment();
            var coordinator = CreateCoordinator(runtime);
            var validation = coordinator.ValidateScript(new TestApiScriptPathRequest
            {
                ScriptPath = scriptPath
            });

            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath,
                ExpectedSha256 = validation.Sha256
            });

            await runtime.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var running = coordinator.GetOperationStatus(accepted.OperationId);
            Assert.Equal(TestApiOperationStatus.Running, running.Status);
            Assert.Equal("BetterBTD", running.InputOwner);
            Assert.False(running.CanGameDriverRecover);

            var pause = coordinator.Pause(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            Assert.Equal(TestApiOperationStatus.PauseRequested, pause.Status);

            runtime.PublishProgress(ScriptExecutionRunState.Paused, "BeforeInstruction", 2);
            var paused = coordinator.GetOperationStatus(accepted.OperationId);
            Assert.Equal(TestApiOperationStatus.Paused, paused.Status);
            Assert.Equal(2, paused.NonOracleDiagnostics.Progress?.CurrentAttempt);

            var resume = coordinator.Resume(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            Assert.Equal(TestApiOperationStatus.Running, resume.Status);

            runtime.PublishLog("first");
            runtime.PublishLog("second");
            var firstPage = coordinator.GetOperationLogs(accepted.OperationId, 0, 1);
            Assert.True(firstPage.HasMore);
            Assert.Single(firstPage.NonOracleDiagnostics.Entries);
            var secondPage = coordinator.GetOperationLogs(
                accepted.OperationId,
                firstPage.NextSequence,
                1);
            Assert.False(secondPage.HasMore);
            Assert.Equal("second", Assert.Single(secondPage.NonOracleDiagnostics.Entries).Message);

            var cancel = coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            Assert.Equal(TestApiOperationStatus.Cancelling, cancel.Status);

            var terminal = await WaitForRecoverableAsync(coordinator, accepted.OperationId);
            Assert.Equal(TestApiOperationStatus.Cancelled, terminal.Status);
            Assert.True(terminal.InputControlReleased);
            Assert.True(terminal.CanGameDriverRecover);
            Assert.Equal("None", terminal.InputOwner);
            Assert.True(runtime.ReleaseAllKeysCallCount >= 2);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Cancel_ImmediatelyAfterExecute_DoesNotRegressToRunning()
    {
        var scriptPath = CreateTemporaryScriptFile("immediate-cancel");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment();
            var coordinator = CreateCoordinator(runtime);
            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath
            });

            var cancel = coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            var afterCancel = coordinator.GetOperationStatus(accepted.OperationId);

            Assert.Equal(TestApiOperationStatus.Cancelling, cancel.Status);
            Assert.NotEqual(TestApiOperationStatus.Running, afterCancel.Status);
            var terminal = await WaitForRecoverableAsync(coordinator, accepted.OperationId);
            Assert.Equal(TestApiOperationStatus.Cancelled, terminal.Status);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Cancel_AcceptedBeforeRuntimeCompletes_RemainsCancelled()
    {
        var scriptPath = CreateTemporaryScriptFile("cancel-completion-race");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment
            {
                ReturnCompletedWhenCancelled = true
            };
            var coordinator = CreateCoordinator(runtime);
            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath
            });
            await runtime.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            var terminal = await WaitForRecoverableAsync(coordinator, accepted.OperationId);

            Assert.Equal(TestApiOperationStatus.Cancelled, terminal.Status);
            Assert.Equal(
                ScriptExecutionStatus.Completed,
                terminal.NonOracleDiagnostics.Result?.Status);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task ExecuteScript_HoldsGlobalControlLeaseUntilCleanupCompletes()
    {
        var scriptPath = CreateTemporaryScriptFile("control-lease");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment();
            var leaseCoordinator = new GameControlLeaseCoordinator();
            var coordinator = CreateCoordinator(runtime, leaseCoordinator);
            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath
            });
            await runtime.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(leaseCoordinator.TryAcquire(
                GameControlOwnerKind.AutoTask,
                "competing-auto-task",
                out _));
            Assert.False(leaseCoordinator.TryAcquire(
                GameControlOwnerKind.RobotTask,
                "competing-robot-task",
                out _));
            Assert.Throws<InvalidOperationException>(() =>
                leaseCoordinator.AcquireOrJoinForScriptExecution());

            coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            await WaitForRecoverableAsync(coordinator, accepted.OperationId);
            Assert.False(leaseCoordinator.HasActiveLease);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Recover_RemainsBlockedWhileAnotherControllerIsReportedRunning()
    {
        var scriptPath = CreateTemporaryScriptFile("recover-controller-gate");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment();
            var coordinator = CreateCoordinator(runtime);
            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath
            });
            await runtime.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runtime.IsAutoTaskRunning = true;

            coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            var terminal = await WaitForTerminalAsync(coordinator, accepted.OperationId);

            Assert.False(terminal.CanGameDriverRecover);
            Assert.Equal("BetterBTD", terminal.InputOwner);
            runtime.IsAutoTaskRunning = false;
            Assert.True(coordinator.GetOperationStatus(accepted.OperationId).CanGameDriverRecover);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task Cancel_ReleaseFailureIsRetriedDuringFinalCleanup()
    {
        var scriptPath = CreateTemporaryScriptFile("release-retry");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment
            {
                ReleaseAllKeysFailuresRemaining = 1
            };
            var coordinator = CreateCoordinator(runtime);
            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath
            });
            await runtime.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            var terminal = await WaitForRecoverableAsync(coordinator, accepted.OperationId);

            Assert.Equal(TestApiOperationStatus.Cancelled, terminal.Status);
            Assert.True(terminal.InputControlReleased);
            Assert.True(runtime.ReleaseAllKeysCallCount >= 2);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task FinalReleaseFailure_DoesNotWedgeShutdownAndPoisonsInputControl()
    {
        var scriptPath = CreateTemporaryScriptFile("release-failure");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment
            {
                AlwaysFailReleaseAllKeys = true
            };
            var leaseCoordinator = new GameControlLeaseCoordinator();
            var coordinator = CreateCoordinator(runtime, leaseCoordinator);
            var accepted = coordinator.ExecuteScript(new TestApiScriptExecuteRequest
            {
                ScriptPath = scriptPath
            });
            await runtime.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            coordinator.Cancel(new TestApiOperationControlRequest
            {
                OperationId = accepted.OperationId
            });
            await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var terminal = coordinator.GetOperationStatus(accepted.OperationId);

            Assert.Equal(TestApiOperationStatus.Failed, terminal.Status);
            Assert.False(terminal.InputControlReleased);
            Assert.False(terminal.CanGameDriverRecover);
            Assert.Contains("release failure", terminal.NonOracleDiagnostics.InputReleaseFailure);
            Assert.Contains(
                "Failed to release game input control",
                terminal.NonOracleDiagnostics.Result?.Failure?.Message);
            Assert.False(coordinator.GetHealth().NonOracleDiagnostics.ScriptExecutor.IsOwnedByTestApi);
            Assert.True(leaseCoordinator.IsPoisoned);
            Assert.False(leaseCoordinator.HasActiveLease);
            Assert.False(leaseCoordinator.TryAcquire(
                GameControlOwnerKind.ScriptExecution,
                "future-script",
                out _));
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void ExecuteScript_ExpectedDigestMismatch_IsRejectedBeforeInput()
    {
        var scriptPath = CreateTemporaryScriptFile("digest");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment();
            var coordinator = CreateCoordinator(runtime);

            var exception = Assert.Throws<TestApiRequestException>(() => coordinator.ExecuteScript(
                new TestApiScriptExecuteRequest
                {
                    ScriptPath = scriptPath,
                    ExpectedSha256 = new string('0', 64)
                }));

            Assert.Equal(TestApiErrorCodes.ScriptInvalid, exception.Code);
            Assert.False(runtime.IsScriptExecutorRunning);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void ExecuteScript_SharedControllerIsRunning_ReturnsBusy()
    {
        var scriptPath = CreateTemporaryScriptFile("busy");
        try
        {
            var runtime = new FakeTestApiRuntimeEnvironment
            {
                IsAutoTaskRunning = true
            };
            var coordinator = CreateCoordinator(runtime);

            var exception = Assert.Throws<TestApiRequestException>(() => coordinator.ExecuteScript(
                new TestApiScriptExecuteRequest
                {
                    ScriptPath = scriptPath
                }));

            Assert.Equal(TestApiErrorCodes.Busy, exception.Code);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    private static TestApiCoordinator CreateCoordinator(
        FakeTestApiRuntimeEnvironment runtime,
        GameControlLeaseCoordinator? gameControlLeaseCoordinator = null)
    {
        return new TestApiCoordinator(
            runtime,
            _ => CreateTaskFlow(),
            _ => { },
            gameControlLeaseCoordinator ?? new GameControlLeaseCoordinator());
    }

    private static ScriptTaskFlow CreateTaskFlow()
    {
        var instruction = new ScriptInstructionDocument
        {
            CommandType = ScriptCommandType.Comment.ToString(),
            CommentContent = "test"
        };
        var document = new ScriptDocument
        {
            Metadata = new ScriptMetadataDocument
            {
                ScriptId = "test-script",
                ScriptVersion = "1.0.0",
                Map = "MonkeyMeadow",
                Difficulty = "Easy",
                Mode = "Standard",
                Hero = "Quincy"
            },
            Instructions = [instruction]
        };

        return new ScriptTaskFlow
        {
            SourceFilePath = "test.json",
            Document = document,
            Steps =
            [
                new ScriptTaskFlowStep
                {
                    Index = 0,
                    CommandType = ScriptCommandType.Comment,
                    Instruction = instruction
                }
            ],
            MonkeyObjectsByBindingId = new Dictionary<string, ScriptMonkeyObjectDocument>()
        };
    }

    private static string CreateTemporaryScriptFile(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"betterbtd-test-api-{name}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"test\":true}");
        return path;
    }

    private static async Task<TestApiOperationSnapshot> WaitForRecoverableAsync(
        TestApiCoordinator coordinator,
        string operationId)
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = coordinator.GetOperationStatus(operationId);
            if (snapshot.CanGameDriverRecover)
            {
                return snapshot;
            }

            await Task.Delay(10, cancellationSource.Token);
        }
    }

    private static async Task<TestApiOperationSnapshot> WaitForTerminalAsync(
        TestApiCoordinator coordinator,
        string operationId)
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = coordinator.GetOperationStatus(operationId);
            if (snapshot.CompletedAt.HasValue)
            {
                return snapshot;
            }

            await Task.Delay(10, cancellationSource.Token);
        }
    }

    private sealed class FakeTestApiRuntimeEnvironment : ITestApiRuntimeEnvironment
    {
        public bool IsScriptExecutorRunning { get; private set; }

        public bool IsAutoTaskRunning { get; set; }

        public bool IsRobotTaskRunning { get; set; }

        public bool IsCaptureRunning { get; set; } = true;

        public ScriptExecutionProgressSnapshot? CurrentProgress { get; private set; }

        public int ReleaseAllKeysCallCount { get; private set; }

        public int ReleaseAllKeysFailuresRemaining { get; set; }

        public bool AlwaysFailReleaseAllKeys { get; set; }

        public bool ReturnCompletedWhenCancelled { get; set; }

        public TaskCompletionSource CaptureStartEntered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? CaptureStartRelease { get; init; }

        public TaskCompletionSource ExecutionStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

        public event EventHandler<ScriptExecutionRuntimeLogEntry>? RuntimeLogEmitted;

        public TestApiConfigurationSnapshot GetConfigurationSnapshot() => new()
        {
            TargetWindowTitle = "BloonsTD6",
            CaptureModeName = "TestCapture",
            CaptureIntervalMs = 50,
            GameLanguageCode = "zh-CN",
            KeyboardMouseSimulationModeName = "SendInput"
        };

        public TestApiCaptureSnapshot GetCaptureSnapshot() => new()
        {
            IsRunning = IsCaptureRunning,
            TargetWindowTitle = "BloonsTD6",
            CurrentWindowTitle = "BloonsTD6",
            CaptureModeName = "TestCapture",
            CaptureIntervalMs = 50
        };

        public async Task<TestApiCaptureSnapshot> StartCaptureAsync(
            TestApiCaptureStartRequest request,
            CancellationToken cancellationToken)
        {
            CaptureStartEntered.TrySetResult();
            if (CaptureStartRelease is not null)
            {
                await CaptureStartRelease.Task.WaitAsync(cancellationToken);
            }

            IsCaptureRunning = true;
            return GetCaptureSnapshot();
        }

        public bool RequestPause() => IsScriptExecutorRunning;

        public bool Resume() => IsScriptExecutorRunning;

        public void ReleaseAllKeys()
        {
            ReleaseAllKeysCallCount++;
            if (ReleaseAllKeysFailuresRemaining > 0)
            {
                ReleaseAllKeysFailuresRemaining--;
                throw new InvalidOperationException("release failure");
            }

            if (AlwaysFailReleaseAllKeys)
            {
                throw new InvalidOperationException("release failure");
            }
        }

        public async Task<ScriptExecutionResult> ExecuteAsync(
            ScriptTaskFlow taskFlow,
            ScriptExecutionOptions options,
            CancellationToken cancellationToken)
        {
            IsScriptExecutorRunning = true;
            PublishProgress(ScriptExecutionRunState.Running, "BeforeInstruction", 0);
            ExecutionStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The fake executor delay unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                var progress = CurrentProgress ?? new ScriptExecutionProgressSnapshot();
                progress.RunState = ScriptExecutionRunState.Cancelled;
                return new ScriptExecutionResult
                {
                    Status = ReturnCompletedWhenCancelled
                        ? ScriptExecutionStatus.Completed
                        : ScriptExecutionStatus.Cancelled,
                    ExecutedStepCount = progress.CompletedStepCount,
                    LastCompletedStepIndex = progress.LastCompletedStepIndex,
                    FinalProgress = progress.Clone()
                };
            }
            finally
            {
                IsScriptExecutorRunning = false;
            }
        }

        public void PublishProgress(
            ScriptExecutionRunState runState,
            string checkpoint,
            int attempt)
        {
            CurrentProgress = new ScriptExecutionProgressSnapshot
            {
                SourceFilePath = "test.json",
                RunState = runState,
                CurrentCheckpoint = checkpoint,
                CurrentAttempt = attempt,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            ProgressChanged?.Invoke(this, CurrentProgress.Clone());
        }

        public void PublishLog(string message)
        {
            RuntimeLogEmitted?.Invoke(this, new ScriptExecutionRuntimeLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = ScriptExecutionRuntimeLogLevel.Info,
                Category = ScriptExecutionRuntimeLogCategory.Session,
                Message = message
            });
        }
    }
}
