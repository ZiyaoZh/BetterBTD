using BetterBTD.Core.Config;
using BetterBTD.Models.ScriptExecution;
using InputMouseButton = Fischless.WindowsInput.MouseButton;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution;

public sealed class ScriptRetryOptions
{
    public int MaxAttempts { get; init; } = 3;

    public int DelayBetweenAttemptsMilliseconds { get; init; } = 100;

    public string Description { get; init; } = string.Empty;
}

public sealed class ScriptWaitOptions
{
    public int TimeoutMilliseconds { get; init; } = 5000;

    public int PollIntervalMilliseconds { get; init; } = 100;

    public int StableSuccessCount { get; init; } = 1;

    public string Description { get; init; } = string.Empty;
}

public sealed class ScriptExecutionException : InvalidOperationException
{
    public ScriptExecutionException(
        string message,
        int stepIndex,
        string commandType,
        string checkpoint,
        int attempt = 0,
        Exception? innerException = null,
        string messageKey = "",
        object?[]? messageArguments = null)
        : base(message, innerException)
    {
        StepIndex = stepIndex;
        CommandType = commandType;
        Checkpoint = checkpoint;
        Attempt = attempt;
        MessageKey = messageKey;
        MessageArguments = messageArguments ?? [];
    }

    public static T WithLocalization<T>(T exception, string key, params object?[] arguments)
        where T : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        exception.Data["LocalizationKey"] = key;
        exception.Data["LocalizationArguments"] = arguments;
        return exception;
    }

    public int StepIndex { get; }

    public string CommandType { get; }

    public string Checkpoint { get; }

    public int Attempt { get; }

    public string MessageKey { get; }

    public object?[] MessageArguments { get; }
}

public static class ScriptExecutionOperations
{
    public static Task CheckpointAsync(
        ScriptInstructionExecutionContext context,
        string checkpoint,
        string? message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);

        return context.ExecutionSession.ReachCheckpointAsync(checkpoint, message, null, cancellationToken);
    }

    public static Task DelayAsync(
        ScriptInstructionExecutionContext context,
        int milliseconds,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);

        return DelayCoreAsync(context, milliseconds, checkpoint, cancellationToken);
    }

    public static void MoveMouseToScriptCoordinate(
        ScriptInstructionExecutionContext context,
        WpfPoint scriptCoordinate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();
        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Move mouse to script coordinate {FormatPoint(scriptCoordinate)}.");
        context.RuntimeServices.Input.MoveMouseToScriptCoordinate(scriptCoordinate);
    }

    public static void ClickMouseAtScriptCoordinate(
        ScriptInstructionExecutionContext context,
        WpfPoint scriptCoordinate,
        CancellationToken cancellationToken,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = 50)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();
        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Click mouse at script coordinate {FormatPoint(scriptCoordinate)} | button={button} | clickCount={clickCount} | hold={holdMilliseconds} ms.");
        context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(
            scriptCoordinate,
            button,
            clickCount,
            holdMilliseconds);
    }

    public static void PressHotkey(
        ScriptInstructionExecutionContext context,
        HotkeyBinding hotkey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hotkey);

        cancellationToken.ThrowIfCancellationRequested();
        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Press hotkey '{hotkey.DisplayName}'.");
        context.RuntimeServices.Input.PressHotkey(hotkey);
    }

    public static void PressKey(
        ScriptInstructionExecutionContext context,
        KeyId key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();
        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Press key '{key}'.");
        context.RuntimeServices.Input.PressKey(key);
    }

    public static void KeyDown(
        ScriptInstructionExecutionContext context,
        KeyId key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();
        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Key down '{key}'.");
        context.RuntimeServices.Input.KeyDown(key);
    }

    public static void KeyUp(
        ScriptInstructionExecutionContext context,
        KeyId key)
    {
        ArgumentNullException.ThrowIfNull(context);

        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.Action,
            $"Key up '{key}'.");
        context.RuntimeServices.Input.KeyUp(key);
    }

    public static async Task<T> RetryAsync<T>(
        ScriptInstructionExecutionContext context,
        ScriptRetryOptions options,
        Func<int, CancellationToken, Task<T>> operation,
        Func<T, bool>? isSuccess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operation);

        var maxAttempts = Math.Max(1, options.MaxAttempts);
        var delayBetweenAttemptsMilliseconds = Math.Max(0, options.DelayBetweenAttemptsMilliseconds);
        var description = string.IsNullOrWhiteSpace(options.Description) ? "operation" : options.Description;

        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Polling,
            $"Retry operation '{description}' started | maxAttempts={maxAttempts} | delayBetweenAttempts={delayBetweenAttemptsMilliseconds} ms.");

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await context.ExecutionSession
                .ReachCheckpointAsync(
                    "RetryAttempt",
                    $"{description}: attempt {attempt}/{maxAttempts}.",
                    attempt,
                    cancellationToken)
                .ConfigureAwait(false);

            T result;
            try
            {
                result = await operation(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                ScriptExecutionRuntimeDiagnostics.Warning(
                    ScriptExecutionRuntimeLogCategory.Polling,
                    $"Retry operation '{description}' attempt {attempt}/{maxAttempts} failed with '{ex.Message}'.");
                await context.ExecutionSession
                    .ReachCheckpointAsync(
                        "RetryAttemptFailed",
                        $"{description}: attempt {attempt} failed with '{ex.Message}'.",
                        attempt,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (delayBetweenAttemptsMilliseconds > 0)
                {
                    await context.ExecutionSession.DelayAsync(delayBetweenAttemptsMilliseconds, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (isSuccess is null || isSuccess(result))
            {
                ScriptExecutionRuntimeDiagnostics.Info(
                    ScriptExecutionRuntimeLogCategory.Polling,
                    $"Retry operation '{description}' succeeded on attempt {attempt}/{maxAttempts}.");
                await context.ExecutionSession
                    .ReachCheckpointAsync(
                        "RetryAttemptSucceeded",
                        $"{description}: attempt {attempt} succeeded.",
                        attempt,
                        cancellationToken)
                    .ConfigureAwait(false);

                return result;
            }

            if (attempt < maxAttempts && delayBetweenAttemptsMilliseconds > 0)
            {
                await context.ExecutionSession.DelayAsync(delayBetweenAttemptsMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw CreateExecutionException(
            context,
            "RetryFailed",
            $"{description}: exceeded {maxAttempts} attempts.",
            maxAttempts,
            messageKey: "Editor.Runtime.Error.RetryFailed",
            messageArguments: [maxAttempts]);
    }

    public static async Task WaitUntilAsync(
        ScriptInstructionExecutionContext context,
        ScriptWaitOptions options,
        Func<CancellationToken, Task<bool>> condition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(condition);

        var timeoutMilliseconds = Math.Max(0, options.TimeoutMilliseconds);
        var pollIntervalMilliseconds = Math.Max(10, options.PollIntervalMilliseconds);
        var stableSuccessCount = Math.Max(1, options.StableSuccessCount);
        var description = string.IsNullOrWhiteSpace(options.Description) ? "condition" : options.Description;
        var startedAt = DateTimeOffset.UtcNow;
        var successCount = 0;
        var attempt = 0;
        var operationKey = $"{context.Step.Index}:{context.Step.CommandType}:{description}";
        var pollingScope = ScriptExecutionRuntimeDiagnostics.Current?.CreatePollingScope(
            operationKey,
            description,
            timeoutMilliseconds,
            pollIntervalMilliseconds,
            stableSuccessCount);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            await context.ExecutionSession
                .ReachCheckpointAsync(
                    "WaitPolling",
                    $"{description}: poll {attempt}.",
                    attempt,
                    cancellationToken)
                .ConfigureAwait(false);

            pollingScope?.ReportAttempt(
                attempt,
                successCount,
                $"step=#{context.Step.Index + 1:000} command={context.Step.CommandType}");

            if (await condition(cancellationToken).ConfigureAwait(false))
            {
                successCount++;
                if (successCount >= stableSuccessCount)
                {
                    pollingScope?.Complete(
                        attempt,
                        successCount,
                        $"duration={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0} ms");
                    await context.ExecutionSession
                        .ReachCheckpointAsync(
                            "WaitSatisfied",
                            $"{description}: satisfied after {attempt} polls.",
                            attempt,
                            cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }
            }
            else
            {
                successCount = 0;
            }

            if ((DateTimeOffset.UtcNow - startedAt).TotalMilliseconds >= timeoutMilliseconds)
            {
                pollingScope?.Timeout(
                    attempt,
                    successCount,
                    $"timeout={timeoutMilliseconds} ms");
                throw CreateExecutionException(
                    context,
                    "WaitTimedOut",
                    $"{description}: timeout after {timeoutMilliseconds} ms.",
                    attempt,
                    messageKey: "Editor.Runtime.Error.WaitTimedOut",
                    messageArguments: [timeoutMilliseconds]);
            }

            await context.ExecutionSession.DelayAsync(pollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<GameStageStateSnapshot> CaptureRequiredSnapshotAsync(
        ScriptInstructionExecutionContext context,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);

        await context.ExecutionSession
            .ReachCheckpointAsync(checkpoint, "Capturing stage snapshot.", null, cancellationToken)
            .ConfigureAwait(false);

        var captureStartedAt = DateTimeOffset.UtcNow;
        var snapshot = await context.RuntimeServices.GameStageState.CaptureSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            ScriptExecutionRuntimeDiagnostics.Warning(
                ScriptExecutionRuntimeLogCategory.Capture,
                $"Stage snapshot capture failed at checkpoint '{checkpoint}'.");
            throw CreateExecutionException(context, checkpoint, "Game stage snapshot is unavailable.",
                messageKey: "Editor.Runtime.Error.SnapshotUnavailable");
        }

        ScriptExecutionRuntimeDiagnostics.Info(
            ScriptExecutionRuntimeLogCategory.Capture,
            $"Stage snapshot captured at checkpoint '{checkpoint}' | elapsed={(DateTimeOffset.UtcNow - captureStartedAt).TotalMilliseconds:F0} ms | inLevel={snapshot.IsInLevel?.ToString() ?? "null"} | gold={snapshot.Gold?.ToString() ?? "null"} | round={snapshot.Round?.ToString() ?? "null"}.");
        return snapshot;
    }

    private static ScriptExecutionException CreateExecutionException(
        ScriptInstructionExecutionContext context,
        string checkpoint,
        string message,
        int attempt = 0,
        Exception? innerException = null,
        string messageKey = "",
        object?[]? messageArguments = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ScriptExecutionException(
            message,
            context.Step.Index,
            context.Step.CommandType.ToString(),
            checkpoint,
            attempt,
            innerException,
            messageKey,
            messageArguments);
    }

    private static async Task DelayCoreAsync(
        ScriptInstructionExecutionContext context,
        int milliseconds,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        await context.ExecutionSession
            .ReachCheckpointAsync(checkpoint, $"Delaying {Math.Max(0, milliseconds)} ms.", null, cancellationToken)
            .ConfigureAwait(false);

        ScriptExecutionRuntimeDiagnostics.Trace(
            ScriptExecutionRuntimeLogCategory.State,
            $"Delay requested at checkpoint '{checkpoint}' | duration={Math.Max(0, milliseconds)} ms.");
        await context.ExecutionSession.DelayAsync(milliseconds, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatPoint(WpfPoint point)
    {
        return $"({point.X:0.##}, {point.Y:0.##})";
    }
}
