using System.Net;
using System.Security.Cryptography;
using System.IO;
using System.Reflection;
using BetterBTD.Core.GameControl;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Models.TestApi;
using BetterBTD.Services.ChildSession;

namespace BetterBTD.Core.TestApi;

internal interface ITestApiController
{
    TestApiHealthResponse GetHealth();

    Task<TestApiCaptureStartResponse> StartCaptureAsync(
        TestApiCaptureStartRequest request,
        CancellationToken cancellationToken);

    TestApiScriptValidationResponse ValidateScript(TestApiScriptPathRequest request);

    TestApiScriptExecuteResponse ExecuteScript(TestApiScriptExecuteRequest request);

    TestApiOperationSnapshot GetOperationStatus(string? operationId);

    TestApiOperationLogsResponse GetOperationLogs(string? operationId, long afterSequence, int limit);

    TestApiOperationControlResponse Pause(TestApiOperationControlRequest request);

    TestApiOperationControlResponse Resume(TestApiOperationControlRequest request);

    TestApiOperationControlResponse Cancel(TestApiOperationControlRequest request);
}

internal interface ITestApiRuntimeEnvironment
{
    bool IsScriptExecutorRunning { get; }

    bool IsAutoTaskRunning { get; }

    bool IsRobotTaskRunning { get; }

    ScriptExecutionProgressSnapshot? CurrentProgress { get; }

    event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

    event EventHandler<ScriptExecutionRuntimeLogEntry>? RuntimeLogEmitted;

    TestApiConfigurationSnapshot GetConfigurationSnapshot();

    TestApiCaptureSnapshot GetCaptureSnapshot();

    Task<TestApiCaptureSnapshot> StartCaptureAsync(
        TestApiCaptureStartRequest request,
        CancellationToken cancellationToken);

    bool RequestPause();

    bool Resume();

    void ReleaseAllKeys();

    Task<ScriptExecutionResult> ExecuteAsync(
        ScriptTaskFlow taskFlow,
        ScriptExecutionOptions options,
        CancellationToken cancellationToken);
}

internal sealed class TestApiCoordinator : ITestApiController
{
    private const int MinimumTimeoutMs = 1000;
    private const int MaximumTimeoutMs = 24 * 60 * 60 * 1000;
    private const int MaximumRetainedOperations = 20;
    private const long MaximumScriptFileBytes = 16 * 1024 * 1024;
    private const int MaximumRetainedLogEntries = 10000;

    private static readonly Lazy<TestApiCoordinator> InstanceHolder = new(
        () => new TestApiCoordinator(
            Services.Tasks.TestApi.TestApiRuntimeEnvironment.Instance,
            ScriptTaskFlowService.Instance.LoadFromFile,
            ValidateRegisteredHandlers));

    private readonly object _syncRoot = new();
    private readonly ITestApiRuntimeEnvironment _runtime;
    private readonly Func<string, ScriptTaskFlow> _loadScript;
    private readonly Action<ScriptTaskFlow> _validateTaskFlow;
    private readonly GameControlLeaseCoordinator _gameControlLeaseCoordinator;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly Dictionary<string, OperationState> _operations = new(StringComparer.Ordinal);
    private readonly Queue<string> _operationOrder = new();

    private OperationState? _currentOperation;
    private OperationState? _lastOperation;
    private bool _captureStartInProgress;
    private long _operationSequence;

    internal TestApiCoordinator(
        ITestApiRuntimeEnvironment runtime,
        Func<string, ScriptTaskFlow> loadScript,
        Action<ScriptTaskFlow>? validateTaskFlow = null,
        GameControlLeaseCoordinator? gameControlLeaseCoordinator = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _loadScript = loadScript ?? throw new ArgumentNullException(nameof(loadScript));
        _validateTaskFlow = validateTaskFlow ?? (_ => { });
        _gameControlLeaseCoordinator = gameControlLeaseCoordinator ?? GameControlLeaseCoordinator.Instance;
    }

    public static TestApiCoordinator Instance => InstanceHolder.Value;

    public TestApiHealthResponse GetHealth()
    {
        OperationState? operation;
        lock (_syncRoot)
        {
            operation = _currentOperation;
        }

        return new TestApiHealthResponse
        {
            ApplicationVersion = typeof(TestApiCoordinator).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? string.Empty,
            StartedAt = _startedAt,
            NonOracleDiagnostics = new TestApiHealthDiagnostics
            {
                Configuration = _runtime.GetConfigurationSnapshot(),
                Capture = _runtime.GetCaptureSnapshot(),
                ScriptExecutor = new TestApiScriptExecutorSnapshot
                {
                    IsRunning = _runtime.IsScriptExecutorRunning,
                    IsOwnedByTestApi = operation is not null,
                    CurrentOperationId = operation?.OperationId,
                    IsAutoTaskRunning = _runtime.IsAutoTaskRunning,
                    IsRobotTaskRunning = _runtime.IsRobotTaskRunning,
                    Progress = operation?.Progress?.Clone() ?? _runtime.CurrentProgress?.Clone()
                }
            }
        };
    }

    public async Task<TestApiCaptureStartResponse> StartCaptureAsync(
        TestApiCaptureStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ChildSessionRuntimeState.EnsurePrimaryCanControl();
        ValidateCaptureRequest(request);

        var leaseOwnerId = $"test-capture-{Guid.NewGuid():N}";
        GameControlLease captureLease;
        lock (_syncRoot)
        {
            if (_captureStartInProgress ||
                _currentOperation is not null ||
                _runtime.IsScriptExecutorRunning ||
                _runtime.IsAutoTaskRunning ||
                _runtime.IsRobotTaskRunning)
            {
                throw TestApiRequestException.Conflict(
                    TestApiErrorCodes.Busy,
                    "Capture settings cannot be changed while a game-control operation is running.");
            }

            if (!_gameControlLeaseCoordinator.TryAcquire(
                    GameControlOwnerKind.TestApiCapture,
                    leaseOwnerId,
                    out captureLease))
            {
                throw TestApiRequestException.Conflict(
                    TestApiErrorCodes.Busy,
                    "Another BetterBTD game-control operation is already running or input control is unavailable.");
            }

            _captureStartInProgress = true;
        }

        try
        {
            var before = _runtime.GetCaptureSnapshot();
            if (before.IsRunning)
            {
                if (!CaptureRequestMatchesSnapshot(request, before))
                {
                    throw TestApiRequestException.Conflict(
                        TestApiErrorCodes.Busy,
                        "The capture service is already running with different settings or a different window.");
                }

                return new TestApiCaptureStartResponse
                {
                    Started = true,
                    AlreadyRunning = true,
                    NonOracleDiagnostics = new TestApiCaptureStartDiagnostics
                    {
                        Capture = before
                    }
                };
            }

            TestApiCaptureSnapshot capture;
            try
            {
                capture = await _runtime.StartCaptureAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TestApiRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TestApiRequestException(
                    HttpStatusCode.Conflict,
                    TestApiErrorCodes.CaptureStartFailed,
                    ex.Message,
                    ex);
            }

            return new TestApiCaptureStartResponse
            {
                Started = capture.IsRunning,
                AlreadyRunning = false,
                NonOracleDiagnostics = new TestApiCaptureStartDiagnostics
                {
                    Capture = capture
                }
            };
        }
        finally
        {
            lock (_syncRoot)
            {
                _captureStartInProgress = false;
            }

            captureLease.Dispose();
        }
    }

    public TestApiScriptValidationResponse ValidateScript(TestApiScriptPathRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (scriptPath, sha256, taskFlow) = LoadAndValidateScript(request.ScriptPath);
        var metadata = taskFlow.Document.Metadata;

        return new TestApiScriptValidationResponse
        {
            IsValid = true,
            ScriptPath = scriptPath,
            Sha256 = sha256,
            NonOracleDiagnostics = new TestApiScriptValidationDiagnostics
            {
                StepCount = taskFlow.Steps.Count,
                MonkeyObjectCount = taskFlow.Document.MonkeyObjects.Count,
                ScriptId = metadata.ScriptId,
                ScriptVersion = metadata.ScriptVersion,
                Map = metadata.Map,
                Difficulty = metadata.Difficulty,
                Mode = metadata.Mode,
                Hero = metadata.Hero
            }
        };
    }

    public TestApiScriptExecuteResponse ExecuteScript(TestApiScriptExecuteRequest request)
    {
        ChildSessionRuntimeState.EnsurePrimaryCanControl();
        ArgumentNullException.ThrowIfNull(request);
        ValidateExecuteRequest(request);
        var (scriptPath, sha256, taskFlow) = LoadAndValidateScript(request.ScriptPath);

        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256) &&
            !string.Equals(request.ExpectedSha256.Trim(), sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw TestApiRequestException.Conflict(
                TestApiErrorCodes.ScriptInvalid,
                "The script file does not match expectedSha256.");
        }

        if (taskFlow.Steps.Count == 0 && request.StartStepIndex != 0 ||
            taskFlow.Steps.Count > 0 && request.StartStepIndex >= taskFlow.Steps.Count)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                $"startStepIndex {request.StartStepIndex} is outside the script step range.");
        }

        var acceptedAt = DateTimeOffset.UtcNow;
        var options = new ScriptExecutionOptions
        {
            StartStepIndex = request.StartStepIndex,
            IntervalStrategy = request.IntervalStrategy,
            CommonOperationIntervalMs = request.CommonOperationIntervalMs,
            RequireCaptureService = true,
            RequireTargetWindow = true
        };
        OperationState operation;
        lock (_syncRoot)
        {
            if (_captureStartInProgress ||
                _currentOperation is not null ||
                _runtime.IsScriptExecutorRunning ||
                _runtime.IsAutoTaskRunning ||
                _runtime.IsRobotTaskRunning)
            {
                throw TestApiRequestException.Conflict(
                    TestApiErrorCodes.Busy,
                    "Another BetterBTD game-control operation is already running.");
            }

            var operationId = $"test-{acceptedAt:yyyyMMddHHmmss}-{++_operationSequence:000000}";
            if (!_gameControlLeaseCoordinator.TryAcquire(
                    GameControlOwnerKind.TestApiOperation,
                    operationId,
                    out var controlLease))
            {
                throw TestApiRequestException.Conflict(
                    TestApiErrorCodes.Busy,
                    "Another BetterBTD game-control operation is already running or input control is unavailable.");
            }

            operation = new OperationState(
                operationId,
                scriptPath,
                acceptedAt,
                request.TimeoutMs,
                controlLease);
            _operations.Add(operationId, operation);
            _operationOrder.Enqueue(operationId);
            _currentOperation = operation;
            TrimOperationHistoryUnderLock();
            using (GameControlLeaseContext.Push(operationId))
            {
                operation.ExecutionTask = Task.Run(
                    () => RunOperationAsync(operation, taskFlow, options),
                    CancellationToken.None);
            }
        }

        return new TestApiScriptExecuteResponse
        {
            OperationId = operation.OperationId,
            Status = operation.Status,
            AcceptedAt = operation.AcceptedAt
        };
    }

    public TestApiOperationSnapshot GetOperationStatus(string? operationId)
    {
        var operation = GetRequiredOperation(operationId);
        lock (_syncRoot)
        {
            return CreateOperationSnapshot(operation);
        }
    }

    public TestApiOperationLogsResponse GetOperationLogs(string? operationId, long afterSequence, int limit)
    {
        if (afterSequence < 0)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "afterSequence must be zero or greater.");
        }

        if (limit is < 1 or > 1000)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "limit must be between 1 and 1000.");
        }

        var operation = GetRequiredOperation(operationId);
        lock (_syncRoot)
        {
            var matchingEntries = operation.LogEntries
                .Where(entry => entry.Sequence > afterSequence)
                .Take(limit + 1)
                .ToArray();
            var hasMore = matchingEntries.Length > limit;
            var entries = matchingEntries.Take(limit).ToArray();
            return new TestApiOperationLogsResponse
            {
                OperationId = operation.OperationId,
                NextSequence = entries.LastOrDefault()?.Sequence ?? afterSequence,
                HasMore = hasMore,
                IsTruncated = operation.LogEntriesTruncated,
                FirstAvailableSequence = operation.LogEntries.FirstOrDefault()?.Sequence ?? operation.NextLogSequence,
                NonOracleDiagnostics = new TestApiOperationLogDiagnostics
                {
                    Entries = entries
                }
            };
        }
    }

    public TestApiOperationControlResponse Pause(TestApiOperationControlRequest request)
    {
        ChildSessionRuntimeState.EnsurePrimaryCanControl();
        var operation = GetControllableOperation(request);
        bool accepted;
        lock (_syncRoot)
        {
            if (operation.Status is not TestApiOperationStatus.Running)
            {
                throw InvalidOperationState(operation, "pause");
            }

            accepted = _runtime.RequestPause();
            if (accepted)
            {
                operation.Status = TestApiOperationStatus.PauseRequested;
                operation.LastUpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        if (!accepted)
        {
            throw InvalidOperationState(operation, "pause");
        }

        return CreateControlResponse(operation, true);
    }

    public TestApiOperationControlResponse Resume(TestApiOperationControlRequest request)
    {
        ChildSessionRuntimeState.EnsurePrimaryCanControl();
        var operation = GetControllableOperation(request);
        bool accepted;
        lock (_syncRoot)
        {
            if (operation.Status is not (TestApiOperationStatus.PauseRequested or TestApiOperationStatus.Paused))
            {
                throw InvalidOperationState(operation, "resume");
            }

            accepted = _runtime.Resume();
            if (accepted)
            {
                operation.Status = TestApiOperationStatus.Running;
                operation.LastUpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        if (!accepted)
        {
            throw InvalidOperationState(operation, "resume");
        }

        return CreateControlResponse(operation, true);
    }

    public TestApiOperationControlResponse Cancel(TestApiOperationControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ChildSessionRuntimeState.EnsurePrimaryCanControl();
        if (string.IsNullOrWhiteSpace(request.OperationId))
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "operationId is required.");
        }

        var operation = GetRequiredOperation(request.OperationId);
        var releaseKeys = false;
        lock (_syncRoot)
        {
            if (operation.Status == TestApiOperationStatus.Cancelling)
            {
                return CreateControlResponse(operation, true);
            }

            if (IsTerminal(operation.Status))
            {
                return CreateControlResponse(operation, false);
            }

            if (!ReferenceEquals(_currentOperation, operation))
            {
                throw InvalidOperationState(operation, "cancel");
            }

            operation.CancellationSource.Cancel();
            operation.Status = TestApiOperationStatus.Cancelling;
            operation.LastUpdatedAt = DateTimeOffset.UtcNow;
            releaseKeys = operation.HasAcquiredInputControl;
        }

        if (releaseKeys)
        {
            TryReleaseAllKeys(operation, finalAttempt: false);
        }

        return CreateControlResponse(operation, true);
    }

    internal async Task StopAsync()
    {
        OperationState? operation;
        Task? executionTask;
        var releaseKeys = false;
        lock (_syncRoot)
        {
            operation = _currentOperation;
            executionTask = operation?.ExecutionTask;
            operation?.CancellationSource.Cancel();
            releaseKeys = operation?.HasAcquiredInputControl == true;
        }

        if (releaseKeys && operation is not null)
        {
            TryReleaseAllKeys(operation, finalAttempt: false);
        }

        if (executionTask is not null)
        {
            await executionTask.ConfigureAwait(false);
        }
    }

    private async Task RunOperationAsync(
        OperationState operation,
        ScriptTaskFlow taskFlow,
        ScriptExecutionOptions options)
    {
        using var timeoutSource = new CancellationTokenSource();
        if (operation.TimeoutMs is int timeoutMs)
        {
            timeoutSource.CancelAfter(timeoutMs);
        }

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            operation.CancellationSource.Token,
            timeoutSource.Token);

        EventHandler<ScriptExecutionProgressSnapshot> progressHandler = (_, snapshot) =>
            RecordProgress(operation, snapshot);
        EventHandler<ScriptExecutionRuntimeLogEntry> logHandler = (_, entry) =>
            RecordLog(operation, entry);

        _runtime.ProgressChanged += progressHandler;
        _runtime.RuntimeLogEmitted += logHandler;

        try
        {
            lock (_syncRoot)
            {
                operation.Status = operation.CancellationSource.IsCancellationRequested
                    ? TestApiOperationStatus.Cancelling
                    : TestApiOperationStatus.Running;
                operation.LastUpdatedAt = DateTimeOffset.UtcNow;
            }

            var result = await _runtime
                .ExecuteAsync(taskFlow, options, linkedSource.Token)
                .ConfigureAwait(false);

            lock (_syncRoot)
            {
                operation.Result = CreateResultSnapshot(result);
                operation.Progress = result.FinalProgress?.Clone() ?? operation.Progress;
                operation.Status = operation.CancellationSource.IsCancellationRequested
                    ? TestApiOperationStatus.Cancelled
                    : result.Status switch
                    {
                        ScriptExecutionStatus.Completed => TestApiOperationStatus.Completed,
                        ScriptExecutionStatus.Cancelled when timeoutSource.IsCancellationRequested =>
                            TestApiOperationStatus.TimedOut,
                        ScriptExecutionStatus.Cancelled => TestApiOperationStatus.Cancelled,
                        _ => TestApiOperationStatus.Failed
                    };
                operation.LastUpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                operation.Status = timeoutSource.IsCancellationRequested
                    ? TestApiOperationStatus.TimedOut
                    : operation.CancellationSource.IsCancellationRequested
                        ? TestApiOperationStatus.Cancelled
                        : TestApiOperationStatus.Failed;
                operation.FailureMessage = ex.Message;
                operation.LastUpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _runtime.ProgressChanged -= progressHandler;
            _runtime.RuntimeLogEmitted -= logHandler;

            var inputControlReleased = TryReleaseAllKeys(operation, finalAttempt: true);
            if (!inputControlReleased)
            {
                operation.ControlLease.MarkPoisoned();
            }

            operation.ControlLease.Dispose();

            lock (_syncRoot)
            {
                operation.InputControlReleased = inputControlReleased;
                operation.ControlLeaseReleased = true;
                operation.CompletedAt = DateTimeOffset.UtcNow;
                operation.LastUpdatedAt = operation.CompletedAt.Value;
                _lastOperation = operation;
                if (ReferenceEquals(_currentOperation, operation))
                {
                    _currentOperation = null;
                }
            }

            operation.CancellationSource.Dispose();
        }
    }

    private bool TryReleaseAllKeys(OperationState operation, bool finalAttempt)
    {
        try
        {
            _runtime.ReleaseAllKeys();
            operation.ControlLease.ConfirmInputReleased();
            if (finalAttempt)
            {
                lock (_syncRoot)
                {
                    operation.PendingInputReleaseFailureMessage = string.Empty;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            lock (_syncRoot)
            {
                operation.PendingInputReleaseFailureMessage = ex.Message;
                if (finalAttempt)
                {
                    operation.Status = TestApiOperationStatus.Failed;
                    operation.Result = null;
                    operation.FailureMessage = string.IsNullOrWhiteSpace(operation.FailureMessage)
                        ? $"Failed to release game input control: {ex.Message}"
                        : $"{operation.FailureMessage} Failed to release game input control: {ex.Message}";
                    operation.LastUpdatedAt = DateTimeOffset.UtcNow;
                }
            }

            return false;
        }
    }

    private void RecordProgress(OperationState operation, ScriptExecutionProgressSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_currentOperation, operation))
            {
                return;
            }

            operation.Progress = snapshot.Clone();
            operation.HasAcquiredInputControl = true;
            if (operation.CancellationSource.IsCancellationRequested)
            {
                operation.Status = TestApiOperationStatus.Cancelling;
            }
            else
            {
                operation.Status = snapshot.RunState switch
                {
                    ScriptExecutionRunState.Running => TestApiOperationStatus.Running,
                    ScriptExecutionRunState.PauseRequested => TestApiOperationStatus.PauseRequested,
                    ScriptExecutionRunState.Paused => TestApiOperationStatus.Paused,
                    _ => operation.Status
                };
            }
            operation.LastUpdatedAt = snapshot.LastUpdatedAt;
        }
    }

    private void RecordLog(OperationState operation, ScriptExecutionRuntimeLogEntry entry)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_currentOperation, operation))
            {
                return;
            }

            var sequence = ++operation.NextLogSequence;
            operation.LogEntries.Add(new TestApiOperationLogEntry
            {
                Sequence = sequence,
                Timestamp = entry.Timestamp,
                Level = entry.Level,
                Category = entry.Category,
                Message = entry.Message,
                AggregationKey = entry.AggregationKey,
                ReplaceExisting = entry.ReplaceExisting
            });
            if (operation.LogEntries.Count > MaximumRetainedLogEntries)
            {
                operation.LogEntries.RemoveAt(0);
                operation.LogEntriesTruncated = true;
            }
        }
    }

    private (string ScriptPath, string Sha256, ScriptTaskFlow TaskFlow) LoadAndValidateScript(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "scriptPath is required.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(scriptPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw TestApiRequestException.BadRequest(TestApiErrorCodes.ScriptInvalid, ex.Message);
        }

        try
        {
            var fileInfo = new FileInfo(normalizedPath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Script document was not found.", normalizedPath);
            }

            if (fileInfo.Length > MaximumScriptFileBytes)
            {
                throw new InvalidDataException(
                    $"Script document exceeds the {MaximumScriptFileBytes} byte test API limit.");
            }

            var hashBeforeLoad = CalculateFileSha256(normalizedPath);
            var taskFlow = _loadScript(normalizedPath);
            _validateTaskFlow(taskFlow);
            var hashAfterLoad = CalculateFileSha256(normalizedPath);
            if (!string.Equals(hashBeforeLoad, hashAfterLoad, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Script document changed while it was being validated.");
            }

            return (normalizedPath, hashAfterLoad, taskFlow);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                System.Text.Json.JsonException or
                ArgumentException or
                KeyNotFoundException)
        {
            throw TestApiRequestException.BadRequest(TestApiErrorCodes.ScriptInvalid, ex.Message);
        }
    }

    private OperationState GetRequiredOperation(string? operationId)
    {
        lock (_syncRoot)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return _currentOperation ?? _lastOperation ?? throw TestApiRequestException.NotFound(
                    TestApiErrorCodes.OperationNotFound,
                    "No test API operation is available.");
            }

            if (_operations.TryGetValue(operationId.Trim(), out var operation))
            {
                return operation;
            }
        }

        throw TestApiRequestException.NotFound(
            TestApiErrorCodes.OperationNotFound,
            $"Operation '{operationId}' was not found.");
    }

    private OperationState GetControllableOperation(TestApiOperationControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationId))
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "operationId is required.");
        }

        var operation = GetRequiredOperation(request.OperationId);
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_currentOperation, operation))
            {
                throw InvalidOperationState(operation, "control");
            }
        }

        return operation;
    }

    private TestApiOperationSnapshot CreateOperationSnapshot(OperationState operation)
    {
        var result = operation.Result;
        if (result is null && !string.IsNullOrWhiteSpace(operation.FailureMessage))
        {
            result = new TestApiScriptExecutionResultSnapshot
            {
                Status = ScriptExecutionStatus.Failed,
                ExecutedStepCount = operation.Progress?.CompletedStepCount ?? 0,
                LastCompletedStepIndex = operation.Progress?.LastCompletedStepIndex ?? -1,
                Failure = new ScriptExecutionFailureDetails
                {
                    StepIndex = operation.Progress?.CurrentStepIndex ?? -1,
                    CommandType = operation.Progress?.CurrentCommandType ?? string.Empty,
                    Checkpoint = operation.Progress?.CurrentCheckpoint ?? string.Empty,
                    Attempt = operation.Progress?.CurrentAttempt ?? 0,
                    Message = operation.FailureMessage
                }
            };
        }

        var inputControlReleased = IsTerminal(operation.Status) &&
                                   operation.InputControlReleased &&
                                   operation.ControlLeaseReleased &&
                                   !_gameControlLeaseCoordinator.HasActiveLease &&
                                   !_gameControlLeaseCoordinator.IsPoisoned &&
                                   !_runtime.IsScriptExecutorRunning &&
                                   !_runtime.IsAutoTaskRunning &&
                                   !_runtime.IsRobotTaskRunning;
        return new TestApiOperationSnapshot
        {
            OperationId = operation.OperationId,
            Status = operation.Status,
            AcceptedAt = operation.AcceptedAt,
            LastUpdatedAt = operation.LastUpdatedAt,
            CompletedAt = operation.CompletedAt,
            InputOwner = inputControlReleased ? "None" : "BetterBTD",
            InputControlReleased = inputControlReleased,
            CanGameDriverRecover = inputControlReleased,
            NonOracleDiagnostics = new TestApiOperationDiagnostics
            {
                ScriptPath = operation.ScriptPath,
                InputReleaseFailure = operation.PendingInputReleaseFailureMessage,
                Progress = operation.Progress?.Clone(),
                Result = result
            }
        };
    }

    private static TestApiScriptExecutionResultSnapshot CreateResultSnapshot(ScriptExecutionResult result)
    {
        return new TestApiScriptExecutionResultSnapshot
        {
            Status = result.Status,
            ExecutedStepCount = result.ExecutedStepCount,
            LastCompletedStepIndex = result.LastCompletedStepIndex,
            Failure = result.Failure
        };
    }

    private static TestApiOperationControlResponse CreateControlResponse(
        OperationState operation,
        bool accepted)
    {
        return new TestApiOperationControlResponse
        {
            OperationId = operation.OperationId,
            Accepted = accepted,
            Status = operation.Status
        };
    }

    private static TestApiRequestException InvalidOperationState(OperationState operation, string action)
    {
        return TestApiRequestException.Conflict(
            TestApiErrorCodes.InvalidOperationState,
            $"Operation '{operation.OperationId}' cannot {action} while its status is '{operation.Status}'.");
    }

    private static void ValidateCaptureRequest(TestApiCaptureStartRequest request)
    {
        if (request.WindowHandle <= 0)
        {
            if (request.WindowHandle.HasValue)
            {
                throw TestApiRequestException.BadRequest(
                    TestApiErrorCodes.InvalidRequest,
                    "windowHandle must be greater than zero when provided.");
            }
        }

        if (request.CaptureIntervalMs is < 10 or > 2000)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "captureIntervalMs must be between 10 and 2000.");
        }
    }

    private static void ValidateExecuteRequest(TestApiScriptExecuteRequest request)
    {
        if (request.StartStepIndex < 0)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "startStepIndex must be zero or greater.");
        }

        if (!Enum.IsDefined(request.IntervalStrategy))
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "intervalStrategy is invalid.");
        }

        if (request.CommonOperationIntervalMs is < 50 or > 1000)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "commonOperationIntervalMs must be between 50 and 1000.");
        }

        if (request.TimeoutMs is < MinimumTimeoutMs or > MaximumTimeoutMs)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                $"timeoutMs must be between {MinimumTimeoutMs} and {MaximumTimeoutMs} when provided.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256) &&
            (request.ExpectedSha256.Trim().Length != 64 ||
             request.ExpectedSha256.Trim().Any(static value => !Uri.IsHexDigit(value))))
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "expectedSha256 must be a 64-character hexadecimal SHA-256 digest.");
        }
    }

    private static bool CaptureRequestMatchesSnapshot(
        TestApiCaptureStartRequest request,
        TestApiCaptureSnapshot snapshot)
    {
        return (!request.WindowHandle.HasValue || request.WindowHandle == snapshot.Window?.Handle) &&
               (string.IsNullOrWhiteSpace(request.CaptureModeName) ||
                string.Equals(request.CaptureModeName.Trim(), snapshot.CaptureModeName, StringComparison.OrdinalIgnoreCase)) &&
               (!request.CaptureIntervalMs.HasValue || request.CaptureIntervalMs == snapshot.CaptureIntervalMs) &&
               (!request.AutoFixWin11BitBlt.HasValue ||
                request.AutoFixWin11BitBlt == snapshot.AutoFixWin11BitBlt);
    }

    private static bool IsTerminal(TestApiOperationStatus status)
    {
        return status is TestApiOperationStatus.Completed or
            TestApiOperationStatus.Failed or
            TestApiOperationStatus.Cancelled or
            TestApiOperationStatus.TimedOut;
    }

    private static string CalculateFileSha256(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ValidateRegisteredHandlers(ScriptTaskFlow taskFlow)
    {
        foreach (var step in taskFlow.Steps)
        {
            _ = ScriptInstructionHandlerRegistry.Instance.GetRequiredHandler(step.CommandType);
        }
    }

    private void TrimOperationHistoryUnderLock()
    {
        while (_operationOrder.Count > MaximumRetainedOperations)
        {
            var operationId = _operationOrder.Dequeue();
            if (_currentOperation?.OperationId == operationId)
            {
                _operationOrder.Enqueue(operationId);
                break;
            }

            _operations.Remove(operationId);
        }
    }

    private sealed class OperationState
    {
        public OperationState(
            string operationId,
            string scriptPath,
            DateTimeOffset acceptedAt,
            int? timeoutMs,
            GameControlLease controlLease)
        {
            OperationId = operationId;
            ScriptPath = scriptPath;
            AcceptedAt = acceptedAt;
            TimeoutMs = timeoutMs;
            ControlLease = controlLease ?? throw new ArgumentNullException(nameof(controlLease));
            LastUpdatedAt = acceptedAt;
        }

        public string OperationId { get; }

        public string ScriptPath { get; }

        public DateTimeOffset AcceptedAt { get; }

        public int? TimeoutMs { get; }

        public CancellationTokenSource CancellationSource { get; } = new();

        public GameControlLease ControlLease { get; }

        public TestApiOperationStatus Status { get; set; } = TestApiOperationStatus.Starting;

        public DateTimeOffset? CompletedAt { get; set; }

        public DateTimeOffset LastUpdatedAt { get; set; }

        public ScriptExecutionProgressSnapshot? Progress { get; set; }

        public TestApiScriptExecutionResultSnapshot? Result { get; set; }

        public string FailureMessage { get; set; } = string.Empty;

        public string PendingInputReleaseFailureMessage { get; set; } = string.Empty;

        public List<TestApiOperationLogEntry> LogEntries { get; } = [];

        public long NextLogSequence { get; set; }

        public bool LogEntriesTruncated { get; set; }

        public Task? ExecutionTask { get; set; }

        public bool HasAcquiredInputControl { get; set; }

        public bool InputControlReleased { get; set; }

        public bool ControlLeaseReleased { get; set; }
    }
}

internal sealed class TestApiRequestException : Exception
{
    public TestApiRequestException(
        HttpStatusCode statusCode,
        string code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode StatusCode { get; }

    public string Code { get; }

    public static TestApiRequestException BadRequest(string code, string message) =>
        new(HttpStatusCode.BadRequest, code, message);

    public static TestApiRequestException Conflict(string code, string message) =>
        new(HttpStatusCode.Conflict, code, message);

    public static TestApiRequestException NotFound(string code, string message) =>
        new(HttpStatusCode.NotFound, code, message);
}
