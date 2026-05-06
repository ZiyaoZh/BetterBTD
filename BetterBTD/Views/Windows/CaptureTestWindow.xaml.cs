using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BetterBTD.Helpers.Extensions;
using BetterBTD.Models;
using BetterBTD.Services;
using Fischless.GameCapture;
using OpenCvSharp;
using UiFluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfRect = System.Windows.Rect;
using Size = OpenCvSharp.Size;

namespace BetterBTD.Views.Windows;

public partial class CaptureTestWindow : UiFluentWindow
{
    private readonly LocalizationService _localizationService = LocalizationService.Instance;
    private readonly GameTargetOcrService _gameTargetOcrService = GameTargetOcrService.Instance;
    private IGameCapture? _capture;
    private Size _cachedFrameSize;
    private readonly Stopwatch _statsUpdateTimer = new();
    private long _captureElapsedMilliseconds;
    private long _transferElapsedMilliseconds;
    private long _ocrElapsedMilliseconds;
    private long _captureCount;
    private string _captureModeName = "Unknown";
    private string? _lastError;
    private string? _lastOcrError;
    private string _lastLoggedOcrDiagnostics = string.Empty;
    private string? _windowDisplayName;
    private bool _lastCaptureFailed = true;
    private bool _lastOcrFailed = true;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private int? _lastGold;
    private int? _lastRound;

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
        _statsUpdateTimer.Restart();
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
            RunOcr(capturedFrame);

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

    private void RunOcr(Mat capturedFrame)
    {
        if (!_gameTargetOcrService.IsAvailable)
        {
            _lastOcrFailed = true;
            _lastOcrError = _localizationService.T("CaptureTest.OcrUnavailable");
            _lastGold = null;
            _lastRound = null;
            LogOcrDiagnosticsIfNeeded($"OCR unavailable. AssetRoot={System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "OcrDigits")}");
            return;
        }

        var ocrStopwatch = Stopwatch.StartNew();
        try
        {
            if (_gameTargetOcrService.TryCaptureSnapshot(capturedFrame, out var snapshot, out var diagnostics))
            {
                _lastOcrFailed = false;
                _lastOcrError = null;
                _lastGold = snapshot.Gold;
                _lastRound = snapshot.Round;
                _lastLoggedOcrDiagnostics = string.Empty;
            }
            else
            {
                _lastOcrFailed = true;
                _lastOcrError = _localizationService.T("CaptureTest.OcrFailedRecent");
                _lastGold = null;
                _lastRound = null;
                LogOcrDiagnosticsIfNeeded(diagnostics);
            }
        }
        catch (Exception ex)
        {
            _lastOcrFailed = true;
            _lastOcrError = ex.Message;
            _lastGold = null;
            _lastRound = null;
            LogOcrDiagnosticsIfNeeded($"OCR exception:{Environment.NewLine}{ex}");
        }
        finally
        {
            ocrStopwatch.Stop();
            _ocrElapsedMilliseconds += ocrStopwatch.ElapsedMilliseconds;
        }
    }

    private void LogOcrDiagnosticsIfNeeded(string diagnostics)
    {
        if (string.IsNullOrWhiteSpace(diagnostics) ||
            string.Equals(_lastLoggedOcrDiagnostics, diagnostics, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedOcrDiagnostics = diagnostics;
        Debug.WriteLine($"[CaptureTest][OCR]{Environment.NewLine}{diagnostics}");
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
    }

    private void UpdateStatsText(bool failed, int width = 0, int height = 0, bool force = false)
    {
        if (!force && _statsUpdateTimer.IsRunning && _statsUpdateTimer.ElapsedMilliseconds < 250)
        {
            return;
        }

        _statsUpdateTimer.Restart();
        var modeLabel = _localizationService.T("CaptureTest.Mode");
        CaptureStatsTextBlock.Text = failed
            ? $"{modeLabel}: {_captureModeName} | {_localizationService.T("CaptureTest.CaptureFailedRecent")}"
            : $"{modeLabel}: {_captureModeName} | {_localizationService.T("CaptureTest.FrameSize")}: {width}x{height} | " +
              $"{_localizationService.T("CaptureTest.AvgCapture")}: {AverageMilliseconds(_captureElapsedMilliseconds):F2} ms | " +
              $"{_localizationService.T("CaptureTest.AvgDisplay")}: {AverageMilliseconds(_transferElapsedMilliseconds):F2} ms | " +
              $"{_localizationService.T("CaptureTest.AvgTotal")}: {AverageMilliseconds(_captureElapsedMilliseconds + _transferElapsedMilliseconds):F2} ms | " +
              $"{_localizationService.T("CaptureTest.SampleCount")}: {_captureCount}";
    }

    private void UpdateOcrStatsText(bool failed, bool force = false)
    {
        if (!force && _statsUpdateTimer.IsRunning && _statsUpdateTimer.ElapsedMilliseconds < 250)
        {
            return;
        }

        var ocrLabel = _localizationService.T("CaptureTest.Ocr");
        if (!_gameTargetOcrService.IsAvailable)
        {
            OcrStatsTextBlock.Text = $"{ocrLabel}: {_localizationService.T("CaptureTest.OcrUnavailable")}";
            return;
        }

        OcrStatsTextBlock.Text = failed
            ? $"{ocrLabel}: {(_lastOcrError ?? _localizationService.T("CaptureTest.OcrFailedRecent"))}"
            : $"{ocrLabel}: {_localizationService.T("CaptureTest.Gold")}: {FormatNullableValue(_lastGold)} | " +
              $"{_localizationService.T("CaptureTest.Round")}: {FormatNullableValue(_lastRound)} | " +
              $"{_localizationService.T("CaptureTest.AvgOcr")}: {AverageMilliseconds(_ocrElapsedMilliseconds):F2} ms";
    }

    private void UpdateOverlayRegions(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || CaptureOverlayCanvas.ActualWidth <= 0 || CaptureOverlayCanvas.ActualHeight <= 0)
        {
            HideOverlayRegions();
            return;
        }

        var overlayBounds = CalculateDisplayedFrameBounds(frameWidth, frameHeight, CaptureOverlayCanvas.ActualWidth, CaptureOverlayCanvas.ActualHeight);
        var captureRegions = _gameTargetOcrService.GetCaptureRegions(frameWidth, frameHeight);

        ApplyOverlayRegion(
            GoldRegionRectangle,
            GoldRegionLabelBorder,
            overlayBounds,
            captureRegions.GoldRegion,
            frameWidth,
            frameHeight);

        ApplyOverlayRegion(
            RoundRegionRectangle,
            RoundRegionLabelBorder,
            overlayBounds,
            captureRegions.RoundRegion,
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

    private void HideOverlayRegions()
    {
        GoldRegionRectangle.Visibility = Visibility.Collapsed;
        GoldRegionLabelBorder.Visibility = Visibility.Collapsed;
        RoundRegionRectangle.Visibility = Visibility.Collapsed;
        RoundRegionLabelBorder.Visibility = Visibility.Collapsed;
    }

    private static string FormatNullableValue(int? value)
    {
        return value?.ToString() ?? "--";
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
        _cachedFrameSize = default;
        _statsUpdateTimer.Stop();
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
