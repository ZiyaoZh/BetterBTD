using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using OpenCvSharp;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal static class BlackBorderBadgeDetection
{
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1080;
    private const int BadgeColorTolerance = 8;

    private static readonly IReadOnlyList<BadgeDefinition> BadgeDefinitions =
    [
        new(StageDifficulty.Easy, StageMode.Standard, 406, 366, [0xA88859]),
        new(StageDifficulty.Medium, StageMode.Standard, 492, 366, [0xA88859, 0xC37503]),
        new(StageDifficulty.Hard, StageMode.Standard, 580, 366, [0xA88859, 0xA9B9C4, 0xBE7813]),
        new(StageDifficulty.Easy, StageMode.PrimaryOnly, 382, 398, [0xB08959]),
        new(StageDifficulty.Medium, StageMode.MilitaryOnly, 468, 399, [0xB08959, 0xC37503]),
        new(StageDifficulty.Hard, StageMode.MagicOnly, 543, 377, [0xB08959, 0xA9BCCA, 0xC37503]),
        new(StageDifficulty.Easy, StageMode.Deflation, 436, 398, [0xB08959]),
        new(StageDifficulty.Medium, StageMode.Apopalypse, 495, 398, [0xB08959, 0xC37503]),
        new(StageDifficulty.Medium, StageMode.Reverse, 520, 398, [0xB08959, 0xC37503]),
        new(StageDifficulty.Hard, StageMode.HalfCash, 607, 398, [0xB08959, 0xA9BCCA, 0xC37503]),
        new(StageDifficulty.Hard, StageMode.DoubleHpMoabs, 550, 398, [0xB08959, 0xA9BCCA, 0xC37503]),
        new(StageDifficulty.Hard, StageMode.AlternateBloonsRounds, 615, 377, [0xB08959, 0xA9BCCA, 0xC37503]),
        new(StageDifficulty.Hard, StageMode.Impoppable, 666, 366, [0xA88859, 0xA9B9C4, 0xBE7813]),
        new(StageDifficulty.Hard, StageMode.CHIMPS, 693, 398, [0xB08959, 0xA9BCCA, 0xC37503])
    ];

    public static bool TryIsStageBadgeAcquired(
        Mat frame,
        StageEntryTarget target,
        WpfPoint mapCenterPoint1080p,
        out bool isAcquired)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(target);

        isAcquired = false;

        if (frame.Empty() ||
            !TryGetMapAreaId(mapCenterPoint1080p, out var mapAreaId) ||
            !TryGetBadgeDefinition(target.Difficulty, target.Mode, out var definition))
        {
            return false;
        }

        var badgePoint = CalculateBadgePoint(definition, mapAreaId);
        var pixelX = ScaleReferenceCoordinate(badgePoint.X, ReferenceWidth, frame.Width);
        var pixelY = ScaleReferenceCoordinate(badgePoint.Y, ReferenceHeight, frame.Height);
        var pixel = frame.Get<Vec3b>(pixelY, pixelX);

        isAcquired = !definition.UnacquiredColors.Any(color => IsColorMatch(pixel, color));
        return true;
    }

    public static IReadOnlyList<BlackBorderBadgeState> AnalyzeMapAreaBadges(Mat frame, int mapAreaId)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Empty() || mapAreaId is < 0 or > 5)
        {
            return [];
        }

        var states = new List<BlackBorderBadgeState>(BadgeDefinitions.Count);
        foreach (var definition in BadgeDefinitions)
        {
            var badgePoint = CalculateBadgePoint(definition, mapAreaId);
            var pixelX = ScaleReferenceCoordinate(badgePoint.X, ReferenceWidth, frame.Width);
            var pixelY = ScaleReferenceCoordinate(badgePoint.Y, ReferenceHeight, frame.Height);
            var pixel = frame.Get<Vec3b>(pixelY, pixelX);
            var isAcquired = !definition.UnacquiredColors.Any(color => IsColorMatch(pixel, color));

            states.Add(new BlackBorderBadgeState(
                definition.Difficulty,
                definition.Mode,
                isAcquired,
                badgePoint));
        }

        return states;
    }

    public static bool TryGetMapAreaId(WpfPoint mapCenterPoint1080p, out int mapAreaId)
    {
        var x = mapCenterPoint1080p.X;
        var y = mapCenterPoint1080p.Y;

        mapAreaId = -1;

        var row = y switch
        {
            > 120 and < 430 => 0,
            > 430 and < 750 => 1,
            _ => -1
        };
        if (row < 0)
        {
            return false;
        }

        var column = x switch
        {
            < 740 => 0,
            >= 740 and < 1170 => 1,
            >= 1170 and < 1600 => 2,
            _ => -1
        };
        if (column < 0)
        {
            return false;
        }

        mapAreaId = row * 3 + column;
        return true;
    }

    public static bool TryCalculateBadgePoint(
        StageDifficulty difficulty,
        StageMode mode,
        int mapAreaId,
        out WpfPoint point)
    {
        point = default;

        if (mapAreaId is < 0 or > 5 ||
            !TryGetBadgeDefinition(difficulty, mode, out var definition))
        {
            return false;
        }

        point = CalculateBadgePoint(definition, mapAreaId);
        return true;
    }

    private static bool TryGetBadgeDefinition(
        StageDifficulty difficulty,
        StageMode mode,
        out BadgeDefinition definition)
    {
        foreach (var candidate in BadgeDefinitions)
        {
            if (candidate.Difficulty == difficulty && candidate.Mode == mode)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    private static WpfPoint CalculateBadgePoint(BadgeDefinition definition, int mapAreaId)
    {
        var deltaX = mapAreaId % 3 * 423;
        var deltaY = mapAreaId / 3 * 313;
        return new WpfPoint(definition.BaseX + deltaX, definition.BaseY + deltaY);
    }

    private static int ScaleReferenceCoordinate(double referenceCoordinate, int referenceSize, int actualSize)
    {
        var scaled = (int)Math.Round(referenceCoordinate / referenceSize * actualSize);
        return Math.Clamp(scaled, 0, Math.Max(0, actualSize - 1));
    }

    private static bool IsColorMatch(Vec3b pixel, int rgb)
    {
        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);

        return Math.Abs(pixel.Item2 - r) <= BadgeColorTolerance &&
               Math.Abs(pixel.Item1 - g) <= BadgeColorTolerance &&
               Math.Abs(pixel.Item0 - b) <= BadgeColorTolerance;
    }

    private readonly record struct BadgeDefinition(
        StageDifficulty Difficulty,
        StageMode Mode,
        int BaseX,
        int BaseY,
        IReadOnlyList<int> UnacquiredColors);
}

internal sealed record BlackBorderBadgeState(
    StageDifficulty Difficulty,
    StageMode Mode,
    bool IsAcquired,
    WpfPoint ReferencePoint1080p);
