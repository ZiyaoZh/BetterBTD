using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Models.TestApi;

public static class TestApiConstants
{
    public const string ApiVersion = "v1";
    public const string RoutePrefix = "/api/test/v1";
    public const string DefaultListenUrl = "http://127.0.0.1:18767/";
    public const int MinimumTokenLength = 32;
    public const int MaximumRequestBodyBytes = 1024 * 1024;
}

public static class TestApiErrorCodes
{
    public const string InvalidRequest = "invalidRequest";
    public const string InvalidToken = "invalidToken";
    public const string NotFound = "notFound";
    public const string Busy = "busy";
    public const string ScriptInvalid = "scriptInvalid";
    public const string OperationNotFound = "operationNotFound";
    public const string InvalidOperationState = "invalidOperationState";
    public const string CaptureStartFailed = "captureStartFailed";
    public const string InternalError = "internalError";
}

public enum TestApiOperationStatus
{
    Starting,
    Running,
    PauseRequested,
    Paused,
    Cancelling,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

public sealed class TestApiHealthResponse
{
    public string Status { get; init; } = "ready";

    public string ApiVersion { get; init; } = TestApiConstants.ApiVersion;

    public string ApplicationVersion { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public required TestApiHealthDiagnostics NonOracleDiagnostics { get; init; }
}

public sealed class TestApiHealthDiagnostics
{
    public required TestApiConfigurationSnapshot Configuration { get; init; }

    public required TestApiCaptureSnapshot Capture { get; init; }

    public required TestApiScriptExecutorSnapshot ScriptExecutor { get; init; }
}

public sealed class TestApiConfigurationSnapshot
{
    public string TargetWindowTitle { get; init; } = string.Empty;

    public string CaptureModeName { get; init; } = string.Empty;

    public int CaptureIntervalMs { get; init; }

    public string GameLanguageCode { get; init; } = string.Empty;

    public string KeyboardMouseSimulationModeName { get; init; } = string.Empty;
}

public sealed class TestApiCaptureSnapshot
{
    public bool IsRunning { get; init; }

    public string TargetWindowTitle { get; init; } = string.Empty;

    public string CurrentWindowTitle { get; init; } = string.Empty;

    public string CaptureModeName { get; init; } = string.Empty;

    public int CaptureIntervalMs { get; init; }

    public bool AutoFixWin11BitBlt { get; init; }

    public TestApiWindowSnapshot? Window { get; init; }
}

public sealed class TestApiWindowSnapshot
{
    public long Handle { get; init; }

    public string Title { get; init; } = string.Empty;

    public int ClientWidth { get; init; }

    public int ClientHeight { get; init; }

    public double ScaleFactor { get; init; }
}

public sealed class TestApiScriptExecutorSnapshot
{
    public bool IsRunning { get; init; }

    public bool IsOwnedByTestApi { get; init; }

    public string? CurrentOperationId { get; init; }

    public bool IsAutoTaskRunning { get; init; }

    public bool IsRobotTaskRunning { get; init; }

    public ScriptExecutionProgressSnapshot? Progress { get; init; }
}

public sealed class TestApiCaptureStartRequest
{
    public long? WindowHandle { get; init; }

    public string? CaptureModeName { get; init; }

    public int? CaptureIntervalMs { get; init; }

    public bool? AutoFixWin11BitBlt { get; init; }
}

public sealed class TestApiCaptureStartResponse
{
    public bool Started { get; init; }

    public bool AlreadyRunning { get; init; }

    public required TestApiCaptureStartDiagnostics NonOracleDiagnostics { get; init; }
}

public sealed class TestApiCaptureStartDiagnostics
{
    public required TestApiCaptureSnapshot Capture { get; init; }
}

public sealed class TestApiScriptPathRequest
{
    public string ScriptPath { get; init; } = string.Empty;
}

public sealed class TestApiScriptValidationResponse
{
    public bool IsValid { get; init; }

    public string ScriptPath { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public required TestApiScriptValidationDiagnostics NonOracleDiagnostics { get; init; }
}

public sealed class TestApiScriptValidationDiagnostics
{
    public int StepCount { get; init; }

    public int MonkeyObjectCount { get; init; }

    public string ScriptId { get; init; } = string.Empty;

    public string ScriptVersion { get; init; } = string.Empty;

    public string Map { get; init; } = string.Empty;

    public string Difficulty { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string Hero { get; init; } = string.Empty;
}

public sealed class TestApiScriptExecuteRequest
{
    public string ScriptPath { get; init; } = string.Empty;

    public string? ExpectedSha256 { get; init; }

    public int StartStepIndex { get; init; }

    public ScriptExecutionOperationIntervalStrategy IntervalStrategy { get; init; } =
        ScriptExecutionOperationIntervalStrategy.InstructionCustom;

    public int CommonOperationIntervalMs { get; init; } = 200;

    public int? TimeoutMs { get; init; }
}

public sealed class TestApiScriptExecuteResponse
{
    public string OperationId { get; init; } = string.Empty;

    public TestApiOperationStatus Status { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }
}

public sealed class TestApiOperationControlRequest
{
    public string OperationId { get; init; } = string.Empty;
}

public sealed class TestApiOperationControlResponse
{
    public string OperationId { get; init; } = string.Empty;

    public bool Accepted { get; init; }

    public TestApiOperationStatus Status { get; init; }
}

public sealed class TestApiOperationSnapshot
{
    public string OperationId { get; init; } = string.Empty;

    public TestApiOperationStatus Status { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }

    public DateTimeOffset LastUpdatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string InputOwner { get; init; } = "BetterBTD";

    public bool InputControlReleased { get; init; }

    public bool CanGameDriverRecover { get; init; }

    public required TestApiOperationDiagnostics NonOracleDiagnostics { get; init; }
}

public sealed class TestApiOperationDiagnostics
{
    public string ScriptPath { get; init; } = string.Empty;

    public string InputReleaseFailure { get; init; } = string.Empty;

    public ScriptExecutionProgressSnapshot? Progress { get; init; }

    public TestApiScriptExecutionResultSnapshot? Result { get; init; }
}

public sealed class TestApiScriptExecutionResultSnapshot
{
    public ScriptExecutionStatus Status { get; init; }

    public int ExecutedStepCount { get; init; }

    public int LastCompletedStepIndex { get; init; }

    public ScriptExecutionFailureDetails? Failure { get; init; }
}

public sealed class TestApiOperationLogsResponse
{
    public string OperationId { get; init; } = string.Empty;

    public long NextSequence { get; init; }

    public bool HasMore { get; init; }

    public bool IsTruncated { get; init; }

    public long FirstAvailableSequence { get; init; }

    public required TestApiOperationLogDiagnostics NonOracleDiagnostics { get; init; }
}

public sealed class TestApiOperationLogDiagnostics
{
    public required IReadOnlyList<TestApiOperationLogEntry> Entries { get; init; }
}

public sealed class TestApiOperationLogEntry
{
    public long Sequence { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public ScriptExecutionRuntimeLogLevel Level { get; init; }

    public ScriptExecutionRuntimeLogCategory Category { get; init; }

    public string Message { get; init; } = string.Empty;

    public string AggregationKey { get; init; } = string.Empty;

    public bool ReplaceExisting { get; init; }
}

public sealed class TestApiErrorResponse
{
    public string Code { get; init; } = TestApiErrorCodes.InternalError;

    public string Message { get; init; } = string.Empty;
}
