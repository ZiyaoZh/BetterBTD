using BetterBTD.Models.ScriptExecution;

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
        Exception? innerException = null)
        : base(message, innerException)
    {
        StepIndex = stepIndex;
        CommandType = commandType;
        Checkpoint = checkpoint;
        Attempt = attempt;
    }

    public int StepIndex { get; }

    public string CommandType { get; }

    public string Checkpoint { get; }

    public int Attempt { get; }
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
            maxAttempts);
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

            if (await condition(cancellationToken).ConfigureAwait(false))
            {
                successCount++;
                if (successCount >= stableSuccessCount)
                {
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
                throw CreateExecutionException(
                    context,
                    "WaitTimedOut",
                    $"{description}: timeout after {timeoutMilliseconds} ms.",
                    attempt);
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

        var snapshot = await context.RuntimeServices.GameStageState.CaptureSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            throw CreateExecutionException(context, checkpoint, "Game stage snapshot is unavailable.");
        }

        return snapshot;
    }

    private static ScriptExecutionException CreateExecutionException(
        ScriptInstructionExecutionContext context,
        string checkpoint,
        string message,
        int attempt = 0,
        Exception? innerException = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ScriptExecutionException(
            message,
            context.Step.Index,
            context.Step.CommandType.ToString(),
            checkpoint,
            attempt,
            innerException);
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

        await context.ExecutionSession.DelayAsync(milliseconds, cancellationToken).ConfigureAwait(false);
    }
}
