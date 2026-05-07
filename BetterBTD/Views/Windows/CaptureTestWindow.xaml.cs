using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BetterBTD.Helpers.Extensions;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using Fischless.GameCapture;
using OpenCvSharp;
using UiFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using Size = OpenCvSharp.Size;

namespace BetterBTD.Views.Windows;

public partial class CaptureTestWindow : UiFluentWindow
{
    private sealed class PointGroupOverlayVisual
    {
        public required Path Path { get; init; }

        public required Border LabelBorder { get; init; }

        public required TextBlock LabelTextBlock { get; init; }

        public required string LabelKey { get; set; }
    }

    private readonly LocalizationService _localizationService = LocalizationService.Instance;
    private readonly GameStageStateService _gameStageStateService = GameStageStateService.Instance;
    private readonly CaptureTestStageStateDisplayService _captureTestStageStateDisplayService = CaptureTestStageStateDisplayService.Instance;
    private readonly Dictionary<string, PointGroupOverlayVisual> _pointGroupVisuals = new(StringComparer.Ordinal);
    private IGameCapture? _capture;
    private Size _cachedFrameSize;
    private readonly Stopwatch _captureStatsUpdateTimer = new();
    private readonly Stopwatch _ocrStatsUpdateTimer = new();
    private long _captureElapsedMilliseconds;
    private long _transferElapsedMilliseconds;
    private long _ocrElapsedMilliseconds;
    private long _captureCount;
    private long _changedFrameCount;
    private long _unchangedFrameCount;
    private long _unchangedFrameStreak;
    private string _captureModeName = "Unknown";
    private string? _lastError;
    private string? _lastOcrError;
    private string? _windowDisplayName;
    private bool _lastCaptureFailed = true;
    private bool _lastOcrFailed = true;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private GameStageStateSnapshot? _lastStageStateSnapshot;
    private ulong? _lastFrameSignature;
    private ulong _currentFrameSignature;
    private const double OverlayCrosshairLength = 10d;
    private const double OverlayCrosshairGap = 4d;

    public CaptureTestWindow()
    {
        InitializeComponent();
        _localizationService.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
        Closed += OnClosed;
    }

    public void StartCapture(nint hWnd, GameCaptureOptions options, string? windowDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (hWnd == nint.Zero)
        {
            throw new ArgumentException("The selected window handle is invalid.", nameof(hWnd));
        }

        _windowDisplayName = windowDisplayName;
        _captureModeName = options.CaptureModeName;
        ResetCaptureDiagnostics();
        _captureStatsUpdateTimer.Restart();
        _ocrStatsUpdateTimer.Restart();
        ApplyLocalization();

        _capture = GameCaptureFactory.Create(ParseCaptureMode(options.CaptureModeName));
        try
        {
            _capture.Start(hWnd, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["autoFixWin11BitBlt"] = options.AutoFixWin11BitBlt
            });
        }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            _lastCaptureFailed = true;
            EmptyStateTextBlock.Text = _lastError;
            EmptyStateTextBlock.Visibility = Visibility.Visible;
            CaptureStatsTextBlock.Text = $"{_localizationService.T("CaptureTest.Mode")}: {_captureModeName} | {_localizationService.T("CaptureTest.StartFailed")}";
            UpdateOcrStatsText(failed: true, force: true);
            throw;
        }

        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_capture is null)
        {
            return;
        }

        Mat? capturedFrame = null;
        var captureStopwatch = Stopwatch.StartNew();
        try
        {
            capturedFrame = _capture.Capture();
        }
        catch (Exception ex)
        {
            captureStopwatch.Stop();
            _lastError = ex.ToString();
            _lastCaptureFailed = true;
            EmptyStateTextBlock.Text = _lastError;
            EmptyStateTextBlock.Visibility = Visibility.Visible;
            UpdateStatsText(failed: true);
            UpdateOcrStatsText(failed: true);
            return;
        }

        using (capturedFrame)
        {
            captureStopwatch.Stop();
            _captureElapsedMilliseconds += captureStopwatch.ElapsedMilliseconds;

            if (capturedFrame is null || capturedFrame.Empty())
            {
                _lastCaptureFailed = true;
                EmptyStateTextBlock.Visibility = Visibility.Visible;
                EmptyStateTextBlock.Text = string.IsNullOrWhiteSpace(_lastError)
                    ? _localizationService.T("CaptureTest.WaitingOrFailed")
                    : _lastError;
                UpdateStatsText(failed: true);
                UpdateOcrStatsText(failed: true);
                HideOverlayRegions();
                return;
            }

            _lastError = null;
            _lastCaptureFailed = false;
            EmptyStateTextBlock.Visibility = Visibility.Collapsed;
            _captureCount++;
            _lastFrameWidth = capturedFrame.Width;
            _lastFrameHeight = capturedFrame.Height;
            UpdateFrameDiagnostics(capturedFrame);
            RunStageStateCapture(capturedFrame);

            var transferStopwatch = Stopwatch.StartNew();
            if (_cachedFrameSize != capturedFrame.Size() || DisplayCaptureResultImage.Source is not WriteableBitmap bitmap)
            {
                DisplayCaptureResultImage.Source = capturedFrame.ToWriteableBitmap();
                _cachedFrameSize = capturedFrame.Size();
            }
            else
            {
                capturedFrame.UpdateWriteableBitmap(bitmap);
            }

            transferStopwatch.Stop();
            _transferElapsedMilliseconds += transferStopwatch.ElapsedMilliseconds;

            UpdateStatsText(failed: false, width: capturedFrame.Width, height: capturedFrame.Height);
            UpdateOcrStatsText(_lastOcrFailed);
            UpdateOverlayRegions(capturedFrame.Width, capturedFrame.Height);
        }
    }

    private double AverageMilliseconds(long totalMilliseconds)
    {
        return _captureCount == 0 ? 0d : totalMilliseconds / (double)_captureCount;
    }

    private void RunStageStateCapture(Mat capturedFrame)
    {
        if (!_gameStageStateService.IsAvailable)
        {
            _lastOcrFailed = true;
            _lastOcrError = _localizationService.T("CaptureTest.OcrUnavailable");
            _lastStageStateSnapshot = null;
            return;
        }

        var ocrStopwatch = Stopwatch.StartNew();
        try
        {
            if (_gameStageStateService.TryCaptureSnapshot(capturedFrame, out var snapshot, out var failureMessage))
            {
                _lastOcrFailed = false;
                _lastOcrError = null;
                _lastStageStateSnapshot = snapshot;
            }
            else
            {
                _lastOcrFailed = true;
                _lastOcrError = string.IsNullOrWhiteSpace(failureMessage)
                    ? _localizationService.T("CaptureTest.OcrFailedRecent")
                    : failureMessage;
                _lastStageStateSnapshot = snapshot;
            }
        }
        catch (Exception ex)
        {
            _lastOcrFailed = true;
            _lastOcrError = ex.Message;
            _lastStageStateSnapshot = null;
        }
        finally
        {
            ocrStopwatch.Stop();
            _ocrElapsedMilliseconds += ocrStopwatch.ElapsedMilliseconds;
        }
    }

    private void ApplyLocalization()
    {
        var title = _localizationService.T("CaptureTest.WindowTitle");
        Title = title;
        TestTitleBar.Title = title;

        var targetLabel = _localizationService.T("CaptureTest.TargetWindow");
        CaptureTargetTextBlock.Text = string.IsNullOrWhiteSpace(_windowDisplayName)
            ? targetLabel
            : $"{targetLabel}: {_windowDisplayName}";

        if (string.IsNullOrWhiteSpace(_lastError))
        {
            EmptyStateTextBlock.Text = _localizationService.T("CaptureTest.WaitingOrFailed");
        }

        UpdateStatsText(_lastCaptureFailed, _lastFrameWidth, _lastFrameHeight, force: true);
        UpdateOcrStatsText(_lastOcrFailed, force: true);
        GoldRegionLabelTextBlock.Text = _localizationService.T("CaptureTest.RegionGold");
        RoundRegionLabelTextBlock.Text = _localizationService.T("CaptureTest.RegionRound");

        foreach (var pointGroupVisual in _pointGroupVisuals.Values)
        {
            pointGroupVisual.LabelTextBlock.Text = _localizationService.T(pointGroupVisual.LabelKey);
        }
    }

    private void UpdateStatsText(bool failed, int width = 0, int height = 0, bool force = false)
    {
        if (!force && _captureStatsUpdateTimer.IsRunning && _captureStatsUpdateTimer.ElapsedMilliseconds < 250)
        {
            return;
        }

        _captureStatsUpdateTimer.Restart();
        var modeLabel = _localizationService.T("CaptureTest.Mode");
        var changedSamplesLabel = _localizationService.T("CaptureTest.ChangedSamples");
        var unchangedSamplesLabel = _localizationService.T("CaptureTest.UnchangedSamples");
        var unchangedStreakLabel = _localizationService.T("CaptureTest.UnchangedStreak");
        CaptureStatsTextBlock.Text = failed
            ? $"{modeLabel}: {_captureModeName} | {_localizationService.T("CaptureTest.CaptureFailedRecent")}"
            : $"{modeLabel}: {_captureModeName} | {_localizationService.T("CaptureTest.FrameSize")}: {width}x{height} | " +
              $"{_localizationService.T("CaptureTest.AvgCapture")}: {AverageMilliseconds(_captureElapsedMilliseconds):F2} ms | " +
              $"{_localizationService.T("CaptureTest.AvgDisplay")}: {AverageMilliseconds(_transferElapsedMilliseconds):F2} ms | " +
              $"{_localizationService.T("CaptureTest.AvgTotal")}: {AverageMilliseconds(_captureElapsedMilliseconds + _transferElapsedMilliseconds):F2} ms | " +
              $"{_localizationService.T("CaptureTest.SampleCount")}: {_captureCount} | " +
              $"{changedSamplesLabel}: {_changedFrameCount} | " +
              $"{unchangedSamplesLabel}: {_unchangedFrameCount} | " +
              $"{unchangedStreakLabel}: {_unchangedFrameStreak} | " +
              $"Sig: 0x{_currentFrameSignature:X16}";
    }

    private void UpdateOcrStatsText(bool failed, bool force = false)
    {
        if (!force && _ocrStatsUpdateTimer.IsRunning && _ocrStatsUpdateTimer.ElapsedMilliseconds < 250)
        {
            return;
        }

        _ocrStatsUpdateTimer.Restart();
        var displayModel = _captureTestStageStateDisplayService.Build(
            _localizationService,
            _gameStageStateService.IsAvailable,
            failed,
            _lastOcrError,
            _lastStageStateSnapshot,
            AverageMilliseconds(_ocrElapsedMilliseconds));

        StageStateDetailsTextBlock.Text = displayModel.DetailsText;
    }

    private void UpdateOverlayRegions(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || CaptureOverlayCanvas.ActualWidth <= 0 || CaptureOverlayCanvas.ActualHeight <= 0)
        {
            HideOverlayRegions();
            return;
        }

        var overlayBounds = CalculateDisplayedFrameBounds(frameWidth, frameHeight, CaptureOverlayCanvas.ActualWidth, CaptureOverlayCanvas.ActualHeight);
        var overlayLayout = _gameStageStateService.GetCaptureOverlayLayout(frameWidth, frameHeight);

        ApplyOverlayRegion(
            GoldRegionRectangle,
            GoldRegionLabelBorder,
            overlayBounds,
            overlayLayout.GoldRegion,
            frameWidth,
            frameHeight);

        ApplyOverlayRegion(
            RoundRegionRectangle,
            RoundRegionLabelBorder,
            overlayBounds,
            overlayLayout.RoundRegion,
            frameWidth,
            frameHeight);

        ApplyOverlayPointGroups(
            overlayBounds,
            overlayLayout.PointGroups,
            frameWidth,
            frameHeight);
    }

    private static WpfRect CalculateDisplayedFrameBounds(int frameWidth, int frameHeight, double containerWidth, double containerHeight)
    {
        var scale = Math.Min(containerWidth / frameWidth, containerHeight / frameHeight);
        var width = frameWidth * scale;
        var height = frameHeight * scale;
        var x = (containerWidth - width) / 2d;
        var y = (containerHeight - height) / 2d;
        return new WpfRect(x, y, width, height);
    }

    private static void ApplyOverlayRegion(
        Rectangle rectangle,
        Border labelBorder,
        WpfRect displayedFrameBounds,
        OpenCvSharp.Rect region,
        int frameWidth,
        int frameHeight)
    {
        var scaleX = displayedFrameBounds.Width / frameWidth;
        var scaleY = displayedFrameBounds.Height / frameHeight;
        var x = displayedFrameBounds.X + region.X * scaleX;
        var y = displayedFrameBounds.Y + region.Y * scaleY;
        var width = region.Width * scaleX;
        var height = region.Height * scaleY;

        rectangle.Width = width;
        rectangle.Height = height;
        rectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);

        labelBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(labelBorder, x);
        Canvas.SetTop(labelBorder, Math.Max(displayedFrameBounds.Y, y - 28d));
    }

    private void ApplyOverlayPointGroups(
        WpfRect displayedFrameBounds,
        IReadOnlyList<CaptureTestOverlayPointGroup> pointGroups,
        int frameWidth,
        int frameHeight)
    {
        var activeGroupIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pointGroup in pointGroups)
        {
            activeGroupIds.Add(pointGroup.Id);
            ApplyOverlayPointGroup(displayedFrameBounds, pointGroup, frameWidth, frameHeight);
        }

        foreach (var entry in _pointGroupVisuals)
        {
            if (!activeGroupIds.Contains(entry.Key))
            {
                HidePointGroupVisual(entry.Value);
            }
        }
    }

    private void ApplyOverlayPointGroup(
        WpfRect displayedFrameBounds,
        CaptureTestOverlayPointGroup pointGroup,
        int frameWidth,
        int frameHeight)
    {
        if (pointGroup.Points.Count == 0)
        {
            return;
        }

        var pointGroupVisual = EnsurePointGroupVisual(pointGroup.Id, pointGroup.LabelKey);
        var geometry = new GeometryGroup();

        foreach (var point in pointGroup.Points)
        {
            AppendCrosshairGeometry(geometry, MapFramePoint(displayedFrameBounds, point, frameWidth, frameHeight));
        }

        pointGroupVisual.Path.Stroke = new SolidColorBrush(GetPointGroupColor(pointGroup.Id));
        pointGroupVisual.Path.Data = geometry;
        pointGroupVisual.Path.Visibility = Visibility.Visible;

        pointGroupVisual.LabelKey = pointGroup.LabelKey;
        pointGroupVisual.LabelTextBlock.Text = _localizationService.T(pointGroup.LabelKey);
        pointGroupVisual.LabelBorder.Visibility = Visibility.Visible;
        pointGroupVisual.LabelBorder.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        var labelAnchor = MapFramePoint(displayedFrameBounds, pointGroup.Points[0], frameWidth, frameHeight);
        var labelLocation = CalculatePointGroupLabelLocation(
            displayedFrameBounds,
            labelAnchor,
            pointGroupVisual.LabelBorder.DesiredSize.Width,
            pointGroupVisual.LabelBorder.DesiredSize.Height);

        Canvas.SetLeft(pointGroupVisual.LabelBorder, labelLocation.X);
        Canvas.SetTop(pointGroupVisual.LabelBorder, labelLocation.Y);
    }

    private PointGroupOverlayVisual EnsurePointGroupVisual(string groupId, string labelKey)
    {
        if (_pointGroupVisuals.TryGetValue(groupId, out var pointGroupVisual))
        {
            pointGroupVisual.LabelKey = labelKey;
            return pointGroupVisual;
        }

        var path = new Path
        {
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        var labelTextBlock = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Text = _localizationService.T(labelKey)
        };

        var labelBorder = new Border
        {
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(204, 26, 32, 44)),
            CornerRadius = new CornerRadius(4),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = labelTextBlock
        };

        CaptureOverlayCanvas.Children.Add(path);
        CaptureOverlayCanvas.Children.Add(labelBorder);

        pointGroupVisual = new PointGroupOverlayVisual
        {
            Path = path,
            LabelBorder = labelBorder,
            LabelTextBlock = labelTextBlock,
            LabelKey = labelKey
        };

        _pointGroupVisuals[groupId] = pointGroupVisual;
        return pointGroupVisual;
    }

    private static void AppendCrosshairGeometry(GeometryGroup geometryGroup, WpfPoint center)
    {
        var diagonalOffsetInner = OverlayCrosshairGap / Math.Sqrt(2d);
        var diagonalOffsetOuter = (OverlayCrosshairGap + OverlayCrosshairLength) / Math.Sqrt(2d);

        geometryGroup.Children.Add(new LineGeometry(
            new WpfPoint(center.X - diagonalOffsetOuter, center.Y - diagonalOffsetOuter),
            new WpfPoint(center.X - diagonalOffsetInner, center.Y - diagonalOffsetInner)));
        geometryGroup.Children.Add(new LineGeometry(
            new WpfPoint(center.X + diagonalOffsetInner, center.Y + diagonalOffsetInner),
            new WpfPoint(center.X + diagonalOffsetOuter, center.Y + diagonalOffsetOuter)));
        geometryGroup.Children.Add(new LineGeometry(
            new WpfPoint(center.X - diagonalOffsetOuter, center.Y + diagonalOffsetOuter),
            new WpfPoint(center.X - diagonalOffsetInner, center.Y + diagonalOffsetInner)));
        geometryGroup.Children.Add(new LineGeometry(
            new WpfPoint(center.X + diagonalOffsetInner, center.Y - diagonalOffsetInner),
            new WpfPoint(center.X + diagonalOffsetOuter, center.Y - diagonalOffsetOuter)));
    }

    private static WpfPoint MapFramePoint(WpfRect displayedFrameBounds, OpenCvSharp.Point point, int frameWidth, int frameHeight)
    {
        var scaleX = displayedFrameBounds.Width / frameWidth;
        var scaleY = displayedFrameBounds.Height / frameHeight;

        return new WpfPoint(
            displayedFrameBounds.X + point.X * scaleX,
            displayedFrameBounds.Y + point.Y * scaleY);
    }

    private static WpfPoint CalculatePointGroupLabelLocation(WpfRect displayedFrameBounds, WpfPoint anchor, double labelWidth, double labelHeight)
    {
        var x = anchor.X + 12d;
        if (x + labelWidth > displayedFrameBounds.Right)
        {
            x = anchor.X - labelWidth - 12d;
        }

        var y = anchor.Y - labelHeight - 10d;
        if (y < displayedFrameBounds.Top)
        {
            y = anchor.Y + 10d;
        }

        x = Math.Clamp(x, displayedFrameBounds.Left, Math.Max(displayedFrameBounds.Left, displayedFrameBounds.Right - labelWidth));
        y = Math.Clamp(y, displayedFrameBounds.Top, Math.Max(displayedFrameBounds.Top, displayedFrameBounds.Bottom - labelHeight));
        return new WpfPoint(x, y);
    }

    private static Color GetPointGroupColor(string groupId)
    {
        return groupId switch
        {
            "InLevel" => Color.FromRgb(125, 211, 252),
            "RightUpgradeVisible" => Color.FromRgb(251, 191, 36),
            "RightTopUpgrade" => Color.FromRgb(245, 158, 11),
            "RightMiddleUpgrade" => Color.FromRgb(249, 115, 22),
            "RightBottomUpgrade" => Color.FromRgb(234, 88, 12),
            "LeftUpgradeVisible" => Color.FromRgb(52, 211, 153),
            "LeftTopUpgrade" => Color.FromRgb(34, 197, 94),
            "LeftMiddleUpgrade" => Color.FromRgb(16, 185, 129),
            "LeftBottomUpgrade" => Color.FromRgb(5, 150, 105),
            "IsPlacingMonkey" => Color.FromRgb(244, 114, 182),
            "CanPlaceHero" => Color.FromRgb(250, 204, 21),
            _ => Colors.White
        };
    }

    private static void HidePointGroupVisual(PointGroupOverlayVisual pointGroupVisual)
    {
        pointGroupVisual.Path.Visibility = Visibility.Collapsed;
        pointGroupVisual.LabelBorder.Visibility = Visibility.Collapsed;
    }

    private void HideOverlayRegions()
    {
        GoldRegionRectangle.Visibility = Visibility.Collapsed;
        GoldRegionLabelBorder.Visibility = Visibility.Collapsed;
        RoundRegionRectangle.Visibility = Visibility.Collapsed;
        RoundRegionLabelBorder.Visibility = Visibility.Collapsed;

        foreach (var pointGroupVisual in _pointGroupVisuals.Values)
        {
            HidePointGroupVisual(pointGroupVisual);
        }
    }

    private void ResetCaptureDiagnostics()
    {
        _cachedFrameSize = default;
        _captureElapsedMilliseconds = 0;
        _transferElapsedMilliseconds = 0;
        _ocrElapsedMilliseconds = 0;
        _captureCount = 0;
        _changedFrameCount = 0;
        _unchangedFrameCount = 0;
        _unchangedFrameStreak = 0;
        _lastFrameSignature = null;
        _currentFrameSignature = 0;
        _lastError = null;
        _lastOcrError = null;
        _lastCaptureFailed = true;
        _lastOcrFailed = true;
        _lastFrameWidth = 0;
        _lastFrameHeight = 0;
        _lastStageStateSnapshot = null;
    }

    private void UpdateFrameDiagnostics(Mat capturedFrame)
    {
        var signature = ComputeFrameSignature(capturedFrame);
        if (_lastFrameSignature.HasValue)
        {
            if (_lastFrameSignature.Value == signature)
            {
                _unchangedFrameCount++;
                _unchangedFrameStreak++;
            }
            else
            {
                _changedFrameCount++;
                _unchangedFrameStreak = 0;
            }
        }

        _lastFrameSignature = signature;
        _currentFrameSignature = signature;
    }

    private static unsafe ulong ComputeFrameSignature(Mat frame)
    {
        var bytesPerPixel = frame.ElemSize();
        var rowStep = Math.Max(1, frame.Rows / 32);
        var colStep = Math.Max(1, frame.Cols / 32);
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        hash = (hash ^ (uint)frame.Rows) * prime;
        hash = (hash ^ (uint)frame.Cols) * prime;
        hash = (hash ^ (uint)bytesPerPixel) * prime;

        var basePtr = (byte*)frame.Data.ToPointer();
        var stride = frame.Step();

        for (var row = 0; row < frame.Rows; row += rowStep)
        {
            var rowPtr = basePtr + row * stride;
            for (var col = 0; col < frame.Cols; col += colStep)
            {
                var pixelPtr = rowPtr + col * bytesPerPixel;
                for (var channel = 0; channel < bytesPerPixel; channel++)
                {
                    hash ^= pixelPtr[channel];
                    hash *= prime;
                }
            }
        }

        return hash;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ApplyLocalization);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
        CompositionTarget.Rendering -= OnRendering;
        DisplayCaptureResultImage.Source = null;
        _capture?.Stop();
        _capture?.Dispose();
        _capture = null;
        ResetCaptureDiagnostics();
        _captureStatsUpdateTimer.Stop();
        _ocrStatsUpdateTimer.Stop();
        HideOverlayRegions();
    }

    private static CaptureModes ParseCaptureMode(string captureModeName)
    {
        if (string.IsNullOrWhiteSpace(captureModeName) ||
            !Enum.TryParse<CaptureModes>(captureModeName, true, out var captureMode))
        {
            throw new ArgumentOutOfRangeException(nameof(captureModeName), captureModeName, "Unsupported capture mode.");
        }

        return captureMode;
    }
}
