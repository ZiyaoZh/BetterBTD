using System.Globalization;
using System.IO;
using System.Text;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Helpers;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services.ChildSession;
using BetterBTD.Services.Start.Capture;
using OpenCvSharp;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class AutoTaskFailureArtifactService : IAutoTaskFailureArtifactWriter
{
    private static readonly Lazy<AutoTaskFailureArtifactService> InstanceHolder =
        new(() => new AutoTaskFailureArtifactService());
    private static readonly object DirectoryCreationSyncRoot = new();

    private readonly string? _userRootDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly Func<Mat?> _captureFrame;
    private readonly Func<string> _sessionDirectoryNameProvider;

    private AutoTaskFailureArtifactService()
        : this(
            null,
            TimeProvider.System,
            CaptureCurrentFrame,
            () => ChildSessionRuntimeState.LogSessionDirectoryName)
    {
    }

    internal AutoTaskFailureArtifactService(
        string? userRootDirectory,
        TimeProvider timeProvider,
        Func<Mat?> captureFrame,
        Func<string>? sessionDirectoryNameProvider = null)
    {
        _userRootDirectory = userRootDirectory;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _captureFrame = captureFrame ?? throw new ArgumentNullException(nameof(captureFrame));
        _sessionDirectoryNameProvider = sessionDirectoryNameProvider ?? (() => "Test-Session");
    }

    public static AutoTaskFailureArtifactService Instance => InstanceHolder.Value;

    public async Task WriteAsync(
        AutoTaskExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != AutoTaskExecutionStatus.Failed)
        {
            throw new ArgumentException("Failure artifacts can only be written for failed tasks.", nameof(result));
        }

        var occurredAt = _timeProvider.GetLocalNow();
        var progress = result.FinalProgress;
        var taskKey = string.IsNullOrWhiteSpace(progress.TaskKey)
            ? progress.TaskKind.ToKey()
            : progress.TaskKey;
        var dateDirectory = ResolveDateDirectory(occurredAt);
        var artifactDirectory = CreateUniqueArtifactDirectory(
            dateDirectory,
            $"{occurredAt:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(taskKey)}");
        var screenshotPath = Path.Combine(artifactDirectory, "game.png");
        var screenshotStatus = SaveScreenshot(screenshotPath);
        var log = BuildLog(result, occurredAt, screenshotPath, screenshotStatus);

        await File.WriteAllTextAsync(
                Path.Combine(artifactDirectory, "task.log"),
                log,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string ResolveDateDirectory(DateTimeOffset occurredAt)
    {
        var segments = new[]
        {
            "Logs",
            "AutoTasks",
            "Errors",
            _sessionDirectoryNameProvider(),
            occurredAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
        };

        return _userRootDirectory is null
            ? UserDataPathHelper.ResolveUserDataDirectory(segments)
            : UserDataPathHelper.ResolveDirectory(_userRootDirectory, segments);
    }

    private static string CreateUniqueArtifactDirectory(string parentDirectory, string baseName)
    {
        lock (DirectoryCreationSyncRoot)
        {
            for (var suffix = 0; ; suffix++)
            {
                var directoryName = suffix == 0 ? baseName : $"{baseName}_{suffix}";
                var directoryPath = Path.Combine(parentDirectory, directoryName);
                if (Directory.Exists(directoryPath))
                {
                    continue;
                }

                Directory.CreateDirectory(directoryPath);
                return directoryPath;
            }
        }
    }

    private string SaveScreenshot(string screenshotPath)
    {
        try
        {
            using var frame = _captureFrame();
            if (frame is null || frame.Empty())
            {
                return "Failed: no current game frame was available.";
            }

            return Cv2.ImWrite(screenshotPath, frame)
                ? "Saved"
                : "Failed: OpenCV did not write the image.";
        }
        catch (Exception ex)
        {
            return $"Failed: {ex.Message}";
        }
    }

    private static string BuildLog(
        AutoTaskExecutionResult result,
        DateTimeOffset occurredAt,
        string screenshotPath,
        string screenshotStatus)
    {
        var progress = result.FinalProgress;
        var failure = result.Failure;
        var builder = new StringBuilder();
        Append(builder, "OccurredAtLocal", occurredAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        Append(builder, "OccurredAtUtc", occurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture));
        Append(builder, "TaskKey", progress.TaskKey);
        Append(builder, "TaskKind", progress.TaskKind.ToString());
        Append(builder, "Status", result.Status.ToString());
        Append(builder, "StartedAt", progress.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        Append(builder, "FailedAt", progress.LastUpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        Append(builder, "Phase", failure?.Phase.ToString() ?? progress.Phase.ToString());
        Append(builder, "Activity", progress.CurrentActivity.ToString());
        Append(builder, "Checkpoint", failure?.Checkpoint ?? progress.CurrentCheckpoint);
        Append(builder, "Attempt", (failure?.Attempt ?? progress.CurrentAttempt).ToString(CultureInfo.InvariantCulture));
        Append(builder, "UiState", (failure?.UiState ?? progress.CurrentUiState).ToString());
        Append(builder, "LoopIteration", progress.LoopIteration.ToString(CultureInfo.InvariantCulture));
        Append(builder, "CompletedStageCount", progress.CompletedStageCount.ToString(CultureInfo.InvariantCulture));
        Append(builder, "ConsecutiveNavigationFailures", progress.ConsecutiveNavigationFailures.ToString(CultureInfo.InvariantCulture));
        Append(builder, "ActiveScript", progress.ActiveScriptPath);
        Append(builder, "ActiveScriptDisplayName", progress.ActiveScriptDisplayName);
        Append(builder, "ScriptRuntimeLog", progress.ActiveScriptProgress?.RuntimeLogFilePath ?? string.Empty);
        Append(builder, "ScriptStepIndex", progress.ActiveScriptProgress?.CurrentStepIndex.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        Append(builder, "ScriptCommand", progress.ActiveScriptProgress?.CurrentCommandType ?? string.Empty);
        Append(builder, "ScriptCheckpoint", progress.ActiveScriptProgress?.CurrentCheckpoint ?? string.Empty);
        Append(builder, "Message", failure?.Message ?? progress.Message);
        Append(builder, "LastUiSummary", progress.LastUiSnapshot?.Summary ?? string.Empty);
        Append(builder, "Screenshot", screenshotPath);
        Append(builder, "ScreenshotStatus", screenshotStatus);

        if (result.Exception is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Exception:");
            builder.AppendLine(result.Exception.ToString());
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append(": ").AppendLine(value ?? string.Empty);
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? "task" : builder.ToString();
    }

    private static Mat? CaptureCurrentFrame()
    {
        return GameCaptureService.Instance.TryCaptureFrame(out var frame) ? frame : null;
    }
}
