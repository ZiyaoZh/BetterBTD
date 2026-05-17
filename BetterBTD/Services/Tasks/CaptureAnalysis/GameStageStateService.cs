using System.Diagnostics;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;
using WpfPoint = System.Windows.Point;
using OpenCvPoint = OpenCvSharp.Point;
using OpenCvRect = OpenCvSharp.Rect;
using OpenCvSize = OpenCvSharp.Size;

namespace BetterBTD.Services.Tasks.CaptureAnalysis;

public sealed class GameStageStateService : IGameStageStateService
{
    private static readonly Lazy<GameStageStateService> InstanceHolder = new(() => new GameStageStateService());
    private const int DefaultColorTolerance = 50;
    private static readonly OpenCvRect GoldReferenceRect = new(360, 20, 180, 50);
    private static readonly OpenCvRect RoundReferenceRect = new(1370, 25, 195, 70);
    private static readonly OpenCvSize Reference1080p = new(1920, 1080);
    private static readonly ReferenceColor BtdBrown = new(0xB1, 0x81, 0x4A);
    private static readonly ReferenceColor UpgradePanelTop = new(0xBE, 0x92, 0x5A);
    private static readonly ReferenceColor UpgradePanelBottom = new(0xB4, 0x81, 0x49);
    private static readonly ReferenceColor UpgradePanelHeader = new(0x62, 0x38, 0x11);
    private static readonly ReferenceColor UpgradeActive = new(0xFC, 0xFF, 0x00);
    private static readonly ReferenceColor PlacementWhite = new(0xFF, 0xFF, 0xFF);
    private static readonly ReferenceColor PlacementOrange = new(0xFF, 0x79, 0x00);
    private static readonly ReferenceColor HeroAvailable = new(0xFF, 0xCC, 0x00);
    private static readonly ReferencePoint[] InLevelPoints =
    [
        new(1910, 40),
        new(13, 40)
    ];

    private static readonly ReferencePoint[] RightUpgradeVisiblePoints =
    [
        new(1260, 200),
        new(1260, 870),
        new(1619, 76)
    ];

    private static readonly ReferencePoint[] LeftUpgradeVisiblePoints =
    [
        new(415, 120),
        new(415, 870),
        new(397, 76)
    ];
    private static readonly ReferenceColor[] UpgradePanelVisibleColors =
    [
        UpgradePanelTop,
        UpgradePanelBottom,
        UpgradePanelHeader
    ];

    private static readonly (int Level, ReferencePoint Point)[] RightTopUpgradePoints =
    [
        (5, new ReferencePoint(1278, 437)),
        (4, new ReferencePoint(1278, 462)),
        (3, new ReferencePoint(1278, 487)),
        (2, new ReferencePoint(1278, 512)),
        (1, new ReferencePoint(1278, 537))
    ];

    private static readonly (int Level, ReferencePoint Point)[] RightMiddleUpgradePoints =
    [
        (5, new ReferencePoint(1278, 587)),
        (4, new ReferencePoint(1278, 612)),
        (3, new ReferencePoint(1278, 637)),
        (2, new ReferencePoint(1278, 662)),
        (1, new ReferencePoint(1278, 687))
    ];

    private static readonly (int Level, ReferencePoint Point)[] RightBottomUpgradePoints =
    [
        (5, new ReferencePoint(1278, 737)),
        (4, new ReferencePoint(1278, 762)),
        (3, new ReferencePoint(1278, 787)),
        (2, new ReferencePoint(1278, 812)),
        (1, new ReferencePoint(1278, 837))
    ];

    private static readonly (int Level, ReferencePoint Point)[] LeftTopUpgradePoints =
    [
        (5, new ReferencePoint(56, 437)),
        (4, new ReferencePoint(56, 462)),
        (3, new ReferencePoint(56, 487)),
        (2, new ReferencePoint(56, 512)),
        (1, new ReferencePoint(56, 537))
    ];

    private static readonly (int Level, ReferencePoint Point)[] LeftMiddleUpgradePoints =
    [
        (5, new ReferencePoint(56, 587)),
        (4, new ReferencePoint(56, 612)),
        (3, new ReferencePoint(56, 637)),
        (2, new ReferencePoint(56, 662)),
        (1, new ReferencePoint(56, 687))
    ];

    private static readonly (int Level, ReferencePoint Point)[] LeftBottomUpgradePoints =
    [
        (5, new ReferencePoint(56, 737)),
        (4, new ReferencePoint(56, 762)),
        (3, new ReferencePoint(56, 787)),
        (2, new ReferencePoint(56, 812)),
        (1, new ReferencePoint(56, 837))
    ];

    private readonly GameCaptureService _gameCaptureService;
    private readonly GameTargetOcrService _gameTargetOcrService;

    private GameStageStateService()
    {
        _gameCaptureService = GameCaptureService.Instance;
        _gameTargetOcrService = GameTargetOcrService.Instance;
    }

    public static GameStageStateService Instance => InstanceHolder.Value;

    public bool IsAvailable => true;

    public (OpenCvRect GoldRegion, OpenCvRect RoundRegion) GetCaptureRegions(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }

        if (frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameHeight));
        }

        return (
            ScaleReferenceRect(GoldReferenceRect, frameWidth, frameHeight),
            ScaleReferenceRect(RoundReferenceRect, frameWidth, frameHeight));
    }

    public CaptureTestOverlayLayout GetCaptureOverlayLayout(int frameWidth, int frameHeight)
    {
        var captureRegions = GetCaptureRegions(frameWidth, frameHeight);

        return new CaptureTestOverlayLayout
        {
            GoldRegion = captureRegions.GoldRegion,
            RoundRegion = captureRegions.RoundRegion,
            PointGroups =
            [
                BuildOverlayPointGroup("InLevel", "CaptureTest.InLevel", frameWidth, frameHeight, InLevelPoints),
                BuildOverlayPointGroup("RightUpgradeVisible", "CaptureTest.RightUpgradeVisible", frameWidth, frameHeight, RightUpgradeVisiblePoints),
                BuildOverlayPointGroup("RightTopUpgrade", "CaptureTest.RightTopUpgrade", frameWidth, frameHeight, RightTopUpgradePoints.Select(static x => x.Point)),
                BuildOverlayPointGroup("RightMiddleUpgrade", "CaptureTest.RightMiddleUpgrade", frameWidth, frameHeight, RightMiddleUpgradePoints.Select(static x => x.Point)),
                BuildOverlayPointGroup("RightBottomUpgrade", "CaptureTest.RightBottomUpgrade", frameWidth, frameHeight, RightBottomUpgradePoints.Select(static x => x.Point)),
                BuildOverlayPointGroup("LeftUpgradeVisible", "CaptureTest.LeftUpgradeVisible", frameWidth, frameHeight, LeftUpgradeVisiblePoints),
                BuildOverlayPointGroup("LeftTopUpgrade", "CaptureTest.LeftTopUpgrade", frameWidth, frameHeight, LeftTopUpgradePoints.Select(static x => x.Point)),
                BuildOverlayPointGroup("LeftMiddleUpgrade", "CaptureTest.LeftMiddleUpgrade", frameWidth, frameHeight, LeftMiddleUpgradePoints.Select(static x => x.Point)),
                BuildOverlayPointGroup("LeftBottomUpgrade", "CaptureTest.LeftBottomUpgrade", frameWidth, frameHeight, LeftBottomUpgradePoints.Select(static x => x.Point)),
                BuildOverlayPointGroup("IsPlacingMonkey", "CaptureTest.IsPlacingMonkey", frameWidth, frameHeight, [new ReferencePoint(1600, 120), new ReferencePoint(1600, 98)]),
                BuildOverlayPointGroup("CanPlaceHero", "CaptureTest.CanPlaceHero", frameWidth, frameHeight, [new ReferencePoint(1757, 272), new ReferencePoint(1670, 274)])
            ]
        };
    }

    public Task<bool?> GetIsInLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureFrameValueAsync(static frame => DetectIsInLevel(frame), default(bool?), cancellationToken);
    }

    public Task<int?> GetGoldAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(ReadGold, default(int?), cancellationToken);
    }

    public Task<int?> GetRoundAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(ReadRound, default(int?), cancellationToken);
    }

    public Task<bool?> GetRightUpgradeVisibleAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(
            static frame => ReadUpgradePanelVisible(frame, RightUpgradeVisiblePoints, UpgradePanelVisibleColors),
            default(bool?),
            cancellationToken);
    }

    public Task<int?> GetRightTopUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => ReadUpgradePathLevel(frame, RightTopUpgradePoints).Level, default(int?), cancellationToken);
    }

    public Task<int?> GetRightMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => ReadUpgradePathLevel(frame, RightMiddleUpgradePoints).Level, default(int?), cancellationToken);
    }

    public Task<int?> GetRightBottomUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => ReadUpgradePathLevel(frame, RightBottomUpgradePoints).Level, default(int?), cancellationToken);
    }

    public Task<bool?> GetLeftUpgradeVisibleAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(
            static frame => ReadUpgradePanelVisible(frame, LeftUpgradeVisiblePoints, UpgradePanelVisibleColors),
            default(bool?),
            cancellationToken);
    }

    public Task<int?> GetLeftTopUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => ReadUpgradePathLevel(frame, LeftTopUpgradePoints).Level, default(int?), cancellationToken);
    }

    public Task<int?> GetLeftMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => ReadUpgradePathLevel(frame, LeftMiddleUpgradePoints).Level, default(int?), cancellationToken);
    }

    public Task<int?> GetLeftBottomUpgradeLevelAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => ReadUpgradePathLevel(frame, LeftBottomUpgradePoints).Level, default(int?), cancellationToken);
    }

    public Task<bool?> GetIsPlacingMonkeyAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => DetectIsPlacingMonkey(frame), default(bool?), cancellationToken);
    }

    public Task<bool?> GetCanPlaceHeroAsync(CancellationToken cancellationToken = default)
    {
        return CaptureInLevelValueAsync(static frame => DetectCanPlaceHero(frame), default(bool?), cancellationToken);
    }

    public Task<bool> IsCoordinateColorMatchAsync(
        WpfPoint scriptCoordinate,
        int expectedR,
        int expectedG,
        int expectedB,
        int tolerance,
        CancellationToken cancellationToken = default)
    {
        return CaptureFrameValueAsync(
            frame => IsCoordinateColorMatch(
                frame,
                scriptCoordinate,
                new ReferenceColor(expectedR, expectedG, expectedB),
                tolerance),
            false,
            cancellationToken);
    }

    public Task<string> GetStageTargetAsync(CancellationToken cancellationToken = default)
    {
        return CaptureFrameValueAsync(static _ => string.Empty, string.Empty, cancellationToken);
    }

    public Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_gameCaptureService.TryCaptureFrame(out var frame))
        {
            return Task.FromResult<GameStageStateSnapshot?>(null);
        }

        using (frame)
        {
            _ = TryCaptureSnapshot(frame, out var snapshot, out _);
            return Task.FromResult<GameStageStateSnapshot?>(snapshot);
        }
    }

    public bool TryCaptureSnapshot(out GameStageStateSnapshot snapshot)
    {
        return TryCaptureSnapshot(out snapshot, out _);
    }

    public bool TryCaptureSnapshot(out GameStageStateSnapshot snapshot, out string failureMessage)
    {
        snapshot = new GameStageStateSnapshot();
        failureMessage = "Capture frame unavailable.";

        if (!_gameCaptureService.TryCaptureFrame(out var frame))
        {
            return false;
        }

        using (frame)
        {
            return TryCaptureSnapshot(frame, out snapshot, out failureMessage);
        }
    }

    public bool TryCaptureSnapshot(Mat frame, out GameStageStateSnapshot snapshot)
    {
        return TryCaptureSnapshot(frame, out snapshot, out _);
    }

    public bool TryCaptureSnapshot(Mat frame, out GameStageStateSnapshot snapshot, out string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Empty())
        {
            snapshot = new GameStageStateSnapshot();
            failureMessage = "Source frame is empty.";
            return false;
        }

        var isInLevel = DetectIsInLevel(frame);
        if (isInLevel != true)
        {
            snapshot = new GameStageStateSnapshot
            {
                IsInLevel = isInLevel,
                StageTarget = string.Empty
            };
            failureMessage = string.Empty;
            return true;
        }

        var captureRegions = GetCaptureRegions(frame.Width, frame.Height);

        using var goldCaptureRegion = new Mat(frame, captureRegions.GoldRegion);
        using var roundCaptureRegion = new Mat(frame, captureRegions.RoundRegion);

        var hasGold = _gameTargetOcrService.TryReadGold(goldCaptureRegion, frame.Width, frame.Height, out var gold);
        var hasRound = _gameTargetOcrService.TryReadRound(roundCaptureRegion, frame.Width, frame.Height, out var round);

        snapshot = new GameStageStateSnapshot
        {
            IsInLevel = isInLevel,
            Gold = hasGold ? gold : null,
            Round = hasRound ? round : null,
            RightUpgradePanel = ReadRightUpgradePanelState(frame),
            LeftUpgradePanel = ReadLeftUpgradePanelState(frame),
            IsPlacingMonkey = DetectIsPlacingMonkey(frame),
            CanPlaceHero = DetectCanPlaceHero(frame),
            StageTarget = string.Empty
        };

        failureMessage = hasGold || hasRound
            ? string.Empty
            : "Gold/Round OCR failed.";

        return hasGold || hasRound;
    }

    private Task<T> CaptureInLevelValueAsync<T>(Func<Mat, T> selector, T fallbackValue, CancellationToken cancellationToken)
    {
        return CaptureFrameValueAsync(
            frame => DetectIsInLevel(frame) == true ? selector(frame) : fallbackValue,
            fallbackValue,
            cancellationToken);
    }

    private Task<T> CaptureFrameValueAsync<T>(Func<Mat, T> selector, T fallbackValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_gameCaptureService.TryCaptureFrame(out var frame))
        {
            return Task.FromResult(fallbackValue);
        }

        using (frame)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(selector(frame));
        }
    }

    private static bool? DetectIsInLevel(Mat frame)
    {
        return MatchesAll(frame, BtdBrown, DefaultColorTolerance, InLevelPoints);
    }

    private static GameStageUpgradePanelState ReadRightUpgradePanelState(Mat frame)
    {
        return ReadUpgradePanelState(
            frame,
            "Right",
            RightUpgradeVisiblePoints,
            UpgradePanelVisibleColors,
            RightTopUpgradePoints,
            RightMiddleUpgradePoints,
            RightBottomUpgradePoints);
    }

    private static GameStageUpgradePanelState ReadLeftUpgradePanelState(Mat frame)
    {
        return ReadUpgradePanelState(
            frame,
            "Left",
            LeftUpgradeVisiblePoints,
            UpgradePanelVisibleColors,
            LeftTopUpgradePoints,
            LeftMiddleUpgradePoints,
            LeftBottomUpgradePoints);
    }

    private static bool? DetectIsPlacingMonkey(Mat frame)
    {
        return IsColorMatch(frame, new ReferencePoint(1600, 120), PlacementWhite, DefaultColorTolerance) &&
               IsColorMatch(frame, new ReferencePoint(1600, 98), PlacementOrange, DefaultColorTolerance) && 
               IsColorMatch(frame, new ReferencePoint(1625, 120), PlacementOrange, DefaultColorTolerance);
    }

    private static bool? DetectCanPlaceHero(Mat frame)
    {
        return IsColorMatch(frame, new ReferencePoint(1757, 272), HeroAvailable, DefaultColorTolerance) &&
               IsColorMatch(frame, new ReferencePoint(1670, 274), HeroAvailable, DefaultColorTolerance);
    }

    private static bool IsCoordinateColorMatch(
        Mat frame,
        WpfPoint scriptCoordinate,
        ReferenceColor expectedColor,
        int tolerance)
    {
        if (frame.Empty())
        {
            return false;
        }

        var actualPoint = ScaleScriptPoint(scriptCoordinate, frame.Width, frame.Height);
        var actualColor = ReadPixel(frame, actualPoint.X, actualPoint.Y);

        return Math.Abs(actualColor.R - expectedColor.R) <= tolerance &&
               Math.Abs(actualColor.G - expectedColor.G) <= tolerance &&
               Math.Abs(actualColor.B - expectedColor.B) <= tolerance;
    }

    private int? ReadGold(Mat frame)
    {
        var captureRegions = GetCaptureRegions(frame.Width, frame.Height);
        using var goldCaptureRegion = new Mat(frame, captureRegions.GoldRegion);
        return _gameTargetOcrService.TryReadGold(goldCaptureRegion, frame.Width, frame.Height, out var gold) ? gold : null;
    }

    private int? ReadRound(Mat frame)
    {
        var captureRegions = GetCaptureRegions(frame.Width, frame.Height);
        using var roundCaptureRegion = new Mat(frame, captureRegions.RoundRegion);
        return _gameTargetOcrService.TryReadRound(roundCaptureRegion, frame.Width, frame.Height, out var round) ? round : null;
    }

    private static bool ReadUpgradePanelVisible(
        Mat frame,
        IReadOnlyList<ReferencePoint> visiblePoints,
        IReadOnlyList<ReferenceColor> visibleColors)
    {
        for (var index = 0; index < visiblePoints.Count; index++)
        {
            if (!IsColorMatch(frame, visiblePoints[index], visibleColors[index], DefaultColorTolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static GameStageUpgradePanelState ReadUpgradePanelState(
        Mat frame,
        string panelName,
        IReadOnlyList<ReferencePoint> visiblePoints,
        IReadOnlyList<ReferenceColor> visibleColors,
        IReadOnlyList<(int Level, ReferencePoint Point)> topPathPoints,
        IReadOnlyList<(int Level, ReferencePoint Point)> middlePathPoints,
        IReadOnlyList<(int Level, ReferencePoint Point)> bottomPathPoints)
    {
        var visibleChecks = new List<ColorMatchSample>(visiblePoints.Count);
        for (var index = 0; index < visiblePoints.Count; index++)
        {
            visibleChecks.Add(SampleColorMatch(frame, visiblePoints[index], visibleColors[index], DefaultColorTolerance));
        }

        var isVisible = ReadUpgradePanelVisible(visibleChecks);
        if (!isVisible)
        {
            var emptyPath = new UpgradePathReadResult(0, []);
            WriteUpgradePanelDebugOutput(panelName, false, visibleChecks, emptyPath, emptyPath, emptyPath);

            return new GameStageUpgradePanelState
            {
                IsVisible = false,
                TopPathLevel = null,
                MiddlePathLevel = null,
                BottomPathLevel = null
            };
        }

        var topPath = ReadUpgradePathLevel(frame, topPathPoints);
        var middlePath = ReadUpgradePathLevel(frame, middlePathPoints);
        var bottomPath = ReadUpgradePathLevel(frame, bottomPathPoints);

        WriteUpgradePanelDebugOutput(panelName, true, visibleChecks, topPath, middlePath, bottomPath);

        return new GameStageUpgradePanelState
        {
            IsVisible = true,
            TopPathLevel = topPath.Level,
            MiddlePathLevel = middlePath.Level,
            BottomPathLevel = bottomPath.Level
        };
    }

    private static UpgradePathReadResult ReadUpgradePathLevel(Mat frame, IReadOnlyList<(int Level, ReferencePoint Point)> pathPoints)
    {
        var checks = new List<ColorMatchSample>(pathPoints.Count);
        foreach (var (level, point) in pathPoints)
        {
            var sample = SampleColorMatch(frame, point, UpgradeActive, DefaultColorTolerance, level);
            checks.Add(sample);
            if (sample.IsMatch)
            {
                return new UpgradePathReadResult(level, checks);
            }
        }

        return new UpgradePathReadResult(0, checks);
    }

    private static bool ReadUpgradePanelVisible(IReadOnlyList<ColorMatchSample> visibleChecks)
    {
        return visibleChecks.All(static x => x.IsMatch);
    }

    private static bool MatchesAll(
        Mat frame,
        ReferenceColor expectedColor,
        int tolerance,
        IReadOnlyList<ReferencePoint> points)
    {
        foreach (var point in points)
        {
            if (!IsColorMatch(frame, point, expectedColor, tolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsColorMatch(Mat frame, ReferencePoint referencePoint, ReferenceColor expectedColor, int tolerance)
    {
        return SampleColorMatch(frame, referencePoint, expectedColor, tolerance).IsMatch;
    }

    private static ColorMatchSample SampleColorMatch(
        Mat frame,
        ReferencePoint referencePoint,
        ReferenceColor expectedColor,
        int tolerance,
        int? level = null)
    {
        var actualPoint = ScaleReferencePoint(referencePoint, frame.Width, frame.Height);
        var pixelColor = ReadPixel(frame, actualPoint);
        var diffR = Math.Abs(pixelColor.R - expectedColor.R);
        var diffG = Math.Abs(pixelColor.G - expectedColor.G);
        var diffB = Math.Abs(pixelColor.B - expectedColor.B);

        return new ColorMatchSample(
            referencePoint,
            actualPoint,
            expectedColor,
            pixelColor,
            tolerance,
            level,
            diffR,
            diffG,
            diffB,
            diffR <= tolerance && diffG <= tolerance && diffB <= tolerance);
    }

    private static CaptureTestOverlayPointGroup BuildOverlayPointGroup(
        string id,
        string labelKey,
        int frameWidth,
        int frameHeight,
        IEnumerable<ReferencePoint> referencePoints)
    {
        return new CaptureTestOverlayPointGroup
        {
            Id = id,
            LabelKey = labelKey,
            Points = referencePoints
                .Select(point => ScaleReferencePoint(point, frameWidth, frameHeight))
                .Select(static point => new OpenCvPoint(point.X, point.Y))
                .ToArray()
        };
    }

    private static ReferenceColor ReadPixel(Mat frame, ReferencePoint point)
    {
        return frame.Channels() switch
        {
            1 => ReadGrayPixel(frame, point),
            3 => ReadBgrPixel(frame, point),
            4 => ReadBgraPixel(frame, point),
            _ => throw new NotSupportedException($"Unsupported frame channel count: {frame.Channels()}.")
        };
    }

    private static ReferenceColor ReadPixel(Mat frame, int x, int y)
    {
        return frame.Channels() switch
        {
            1 => ReadGrayPixel(frame, x, y),
            3 => ReadBgrPixel(frame, x, y),
            4 => ReadBgraPixel(frame, x, y),
            _ => throw new NotSupportedException($"Unsupported frame channel count: {frame.Channels()}.")
        };
    }

    private static ReferenceColor ReadGrayPixel(Mat frame, ReferencePoint point)
    {
        var value = frame.At<byte>(point.Y, point.X);
        return new ReferenceColor(value, value, value);
    }

    private static ReferenceColor ReadGrayPixel(Mat frame, int x, int y)
    {
        var value = frame.At<byte>(y, x);
        return new ReferenceColor(value, value, value);
    }

    private static ReferenceColor ReadBgrPixel(Mat frame, ReferencePoint point)
    {
        var value = frame.At<Vec3b>(point.Y, point.X);
        return new ReferenceColor(value.Item2, value.Item1, value.Item0);
    }

    private static ReferenceColor ReadBgrPixel(Mat frame, int x, int y)
    {
        var value = frame.At<Vec3b>(y, x);
        return new ReferenceColor(value.Item2, value.Item1, value.Item0);
    }

    private static ReferenceColor ReadBgraPixel(Mat frame, ReferencePoint point)
    {
        var value = frame.At<Vec4b>(point.Y, point.X);
        return new ReferenceColor(value.Item2, value.Item1, value.Item0);
    }

    private static ReferenceColor ReadBgraPixel(Mat frame, int x, int y)
    {
        var value = frame.At<Vec4b>(y, x);
        return new ReferenceColor(value.Item2, value.Item1, value.Item0);
    }

    private static ReferencePoint ScaleReferencePoint(ReferencePoint referencePoint, int actualWidth, int actualHeight)
    {
        return new ReferencePoint(
            ScaleReferenceCoordinate(referencePoint.X, Reference1080p.Width, actualWidth),
            ScaleReferenceCoordinate(referencePoint.Y, Reference1080p.Height, actualHeight));
    }

    private static OpenCvPoint ScaleScriptPoint(WpfPoint scriptCoordinate, int actualWidth, int actualHeight)
    {
        return new OpenCvPoint(
            ScaleScriptCoordinate(scriptCoordinate.X, Reference1080p.Width, actualWidth),
            ScaleScriptCoordinate(scriptCoordinate.Y, Reference1080p.Height, actualHeight));
    }

    private static int ScaleReferenceCoordinate(int coordinate, int referenceSize, int actualSize)
    {
        if (actualSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualSize));
        }

        var scaled = (int)Math.Round(coordinate / (double)referenceSize * actualSize);
        return Math.Clamp(scaled, 0, Math.Max(0, actualSize - 1));
    }

    private static int ScaleScriptCoordinate(double coordinate, int referenceSize, int actualSize)
    {
        if (actualSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualSize));
        }

        var scaled = (int)Math.Round(coordinate / referenceSize * actualSize);
        return Math.Clamp(scaled, 0, Math.Max(0, actualSize - 1));
    }

    private static OpenCvRect ScaleReferenceRect(OpenCvRect referenceRect, int actualWidth, int actualHeight)
    {
        var x = (int)Math.Round(referenceRect.X / (double)Reference1080p.Width * actualWidth);
        var y = (int)Math.Round(referenceRect.Y / (double)Reference1080p.Height * actualHeight);
        var right = (int)Math.Round(referenceRect.Right / (double)Reference1080p.Width * actualWidth);
        var bottom = (int)Math.Round(referenceRect.Bottom / (double)Reference1080p.Height * actualHeight);

        x = Math.Clamp(x, 0, Math.Max(0, actualWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, actualHeight - 1));
        right = Math.Clamp(right, x + 1, actualWidth);
        bottom = Math.Clamp(bottom, y + 1, actualHeight);

        return new OpenCvRect(x, y, right - x, bottom - y);
    }

    [Conditional("DEBUG")]
    private static void WriteUpgradePanelDebugOutput(
        string panelName,
        bool isVisible,
        IReadOnlyList<ColorMatchSample> visibleChecks,
        UpgradePathReadResult topPath,
        UpgradePathReadResult middlePath,
        UpgradePathReadResult bottomPath)
    {
        Debug.WriteLine(
            $"[GameStageState][Upgrade][{panelName}] Visible={isVisible} | " +
            $"VisibleChecks={FormatColorChecks(visibleChecks)} | " +
            $"Top=Lv{topPath.Level} {FormatColorChecks(topPath.Checks)} | " +
            $"Middle=Lv{middlePath.Level} {FormatColorChecks(middlePath.Checks)} | " +
            $"Bottom=Lv{bottomPath.Level} {FormatColorChecks(bottomPath.Checks)}");
    }

    private static string FormatColorChecks(IReadOnlyList<ColorMatchSample> checks)
    {
        return string.Join("; ", checks.Select(FormatColorCheck));
    }

    private static string FormatColorCheck(ColorMatchSample sample)
    {
        var levelText = sample.Level.HasValue ? $"L{sample.Level.Value} " : string.Empty;
        return $"{levelText}ref=({sample.ReferencePoint.X},{sample.ReferencePoint.Y}) " +
               $"actual=({sample.ActualPoint.X},{sample.ActualPoint.Y}) " +
               $"expected={sample.ExpectedColor.ToHex()} actual={sample.ActualColor.ToHex()} " +
               $"diff=({sample.DiffR},{sample.DiffG},{sample.DiffB}) tol={sample.Tolerance} match={sample.IsMatch}";
    }

    private readonly record struct ReferencePoint(int X, int Y);

    private readonly record struct ReferenceColor(int R, int G, int B)
    {
        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    }

    private readonly record struct ColorMatchSample(
        ReferencePoint ReferencePoint,
        ReferencePoint ActualPoint,
        ReferenceColor ExpectedColor,
        ReferenceColor ActualColor,
        int Tolerance,
        int? Level,
        int DiffR,
        int DiffG,
        int DiffB,
        bool IsMatch);

    private readonly record struct UpgradePathReadResult(int Level, IReadOnlyList<ColorMatchSample> Checks);
}

