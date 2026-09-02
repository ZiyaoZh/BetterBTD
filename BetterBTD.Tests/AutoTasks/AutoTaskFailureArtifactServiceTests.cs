using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Services.Tasks.AutoTasks;
using OpenCvSharp;

namespace BetterBTD.Tests.AutoTasks;

public sealed class AutoTaskFailureArtifactServiceTests
{
    [Fact]
    public async Task WriteAsync_CreatesTimestampedLogAndGameScreenshot()
    {
        var userRoot = CreateTemporaryDirectory();
        var occurredAt = new DateTimeOffset(2026, 9, 2, 14, 35, 12, 345, TimeSpan.FromHours(8));
        var service = new AutoTaskFailureArtifactService(
            userRoot,
            new FixedTimeProvider(occurredAt),
            CreateTestFrame,
            () => "Primary-Session-42");

        try
        {
            await service.WriteAsync(CreateFailedResult());

            var artifactDirectory = Assert.Single(Directory.GetDirectories(
                Path.Combine(userRoot, "Logs", "AutoTasks", "Errors", "Primary-Session-42", "20260902")));
            Assert.Equal("20260902_143512_345_collection", Path.GetFileName(artifactDirectory));

            var screenshotPath = Path.Combine(artifactDirectory, "game.png");
            var logPath = Path.Combine(artifactDirectory, "task.log");
            Assert.True(File.Exists(screenshotPath));
            using var screenshot = Cv2.ImRead(screenshotPath);
            Assert.False(screenshot.Empty());
            Assert.Equal(4, screenshot.Width);
            Assert.Equal(3, screenshot.Height);

            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("OccurredAtLocal: 2026-09-02 14:35:12.345 +08:00", log);
            Assert.Contains("TaskKind: Collection", log);
            Assert.Contains("Checkpoint: StuckUiRecovery", log);
            Assert.Contains("UiState: Loading", log);
            Assert.Contains("Message: Game UI did not change.", log);
            Assert.Contains("ScreenshotStatus: Saved", log);
            Assert.Contains("System.InvalidOperationException: recovery failed", log);
        }
        finally
        {
            Directory.Delete(userRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_WhenFrameIsUnavailable_StillWritesFailureLog()
    {
        var userRoot = CreateTemporaryDirectory();
        var service = new AutoTaskFailureArtifactService(
            userRoot,
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            () => null);

        try
        {
            await service.WriteAsync(CreateFailedResult());

            var logPath = Assert.Single(Directory.GetFiles(userRoot, "task.log", SearchOption.AllDirectories));
            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("ScreenshotStatus: Failed: no current game frame was available.", log);
            Assert.Empty(Directory.GetFiles(userRoot, "*.png", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(userRoot, recursive: true);
        }
    }

    private static AutoTaskExecutionResult CreateFailedResult()
    {
        return new AutoTaskExecutionResult
        {
            Status = AutoTaskExecutionStatus.Failed,
            FinalProgress = new AutoTaskProgressSnapshot
            {
                TaskKey = "collection",
                TaskKind = AutoTaskKind.Collection,
                RunState = AutoTaskRunState.Failed,
                Phase = AutoTaskPhase.PreparingStage,
                CurrentUiState = GameUiStateId.Loading,
                LoopIteration = 7,
                Message = "Game UI did not change."
            },
            Failure = new AutoTaskFailureDetails
            {
                Phase = AutoTaskPhase.PreparingStage,
                UiState = GameUiStateId.Loading,
                Checkpoint = "StuckUiRecovery",
                Attempt = 2,
                Message = "Game UI did not change."
            },
            Exception = new InvalidOperationException("recovery failed")
        };
    }

    private static Mat CreateTestFrame()
    {
        return new Mat(3, 4, MatType.CV_8UC3, new Scalar(10, 20, 30));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "BetterBTD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test", now.Offset, "Test", "Test");
    }
}
