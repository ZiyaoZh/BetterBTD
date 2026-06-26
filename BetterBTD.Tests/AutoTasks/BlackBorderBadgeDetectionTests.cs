using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services.Tasks.AutoTasks;
using OpenCvSharp;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Tests.AutoTasks;

public sealed class BlackBorderBadgeDetectionTests
{
    [Theory]
    [InlineData(500, 300, 0)]
    [InlineData(900, 300, 1)]
    [InlineData(1400, 300, 2)]
    [InlineData(500, 600, 3)]
    [InlineData(900, 600, 4)]
    [InlineData(1400, 600, 5)]
    public void TryGetMapAreaId_MapsRecognizedMapPointToExpectedArea(
        double x,
        double y,
        int expectedAreaId)
    {
        var found = BlackBorderBadgeDetection.TryGetMapAreaId(new WpfPoint(x, y), out var areaId);

        Assert.True(found);
        Assert.Equal(expectedAreaId, areaId);
    }

    [Fact]
    public void TryIsStageBadgeAcquired_ReturnsFalse_WhenBadgeStillUsesUnacquiredColor()
    {
        using var frame = CreateFrame();
        var target = CreateTarget(StageDifficulty.Hard, StageMode.CHIMPS);
        var mapCenter = new WpfPoint(900, 600);
        Assert.True(BlackBorderBadgeDetection.TryCalculateBadgePoint(
            target.Difficulty,
            target.Mode,
            mapAreaId: 4,
            out var badgePoint));
        SetPixel(frame, badgePoint, 0xB08959);

        var found = BlackBorderBadgeDetection.TryIsStageBadgeAcquired(
            frame,
            target,
            mapCenter,
            out var isAcquired);

        Assert.True(found);
        Assert.False(isAcquired);
    }

    [Fact]
    public void TryIsStageBadgeAcquired_ReturnsTrue_WhenBadgeNoLongerUsesUnacquiredColor()
    {
        using var frame = CreateFrame();
        var target = CreateTarget(StageDifficulty.Medium, StageMode.Reverse);
        var mapCenter = new WpfPoint(1400, 300);
        Assert.True(BlackBorderBadgeDetection.TryCalculateBadgePoint(
            target.Difficulty,
            target.Mode,
            mapAreaId: 2,
            out var badgePoint));
        SetPixel(frame, badgePoint, 0xFFFFFF);

        var found = BlackBorderBadgeDetection.TryIsStageBadgeAcquired(
            frame,
            target,
            mapCenter,
            out var isAcquired);

        Assert.True(found);
        Assert.True(isAcquired);
    }

    [Fact]
    public void BadgeDefinitions_CoverEveryBlackBorderQueuedMode()
    {
        foreach (var difficulty in BlackBorderTaskCatalog.Difficulties)
        {
            foreach (var mode in BlackBorderTaskCatalog.GetModesForDifficulty(difficulty))
            {
                var found = BlackBorderBadgeDetection.TryCalculateBadgePoint(
                    difficulty,
                    mode,
                    mapAreaId: 0,
                    out var point);

                Assert.True(found);
                Assert.InRange(point.X, 0, 1920);
                Assert.InRange(point.Y, 0, 1080);
            }
        }
    }

    private static Mat CreateFrame()
    {
        return new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
    }

    private static StageEntryTarget CreateTarget(StageDifficulty difficulty, StageMode mode)
    {
        return new StageEntryTarget
        {
            Map = GameMapType.MonkeyMeadow,
            Difficulty = difficulty,
            Mode = mode
        };
    }

    private static void SetPixel(Mat frame, WpfPoint point, int rgb)
    {
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);
        frame.Set(y, x, new Vec3b(b, g, r));
    }
}
