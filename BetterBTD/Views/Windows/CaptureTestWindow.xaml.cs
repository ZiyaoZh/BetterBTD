using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterBTD.Helpers.Extensions;
using BetterBTD.Models;
using BetterBTD.Services;
using Fischless.GameCapture;
using OpenCvSharp;
using Wpf.Ui.Controls;
using Size = OpenCvSharp.Size;

namespace BetterBTD.Views.Windows;

public partial class CaptureTestWindow : FluentWindow
{
    private readonly LocalizationService _localizationService = LocalizationService.Instance;
    private IGameCapture? _capture;
    private Size _cachedFrameSize;
    private readonly Stopwatch _statsUpdateTimer = new();
    private long _captureElapsedMilliseconds;
    private long _transferElapsedMilliseconds;
    private long _captureCount;
    private string _captureModeName = "Unknown";
    private string? _lastError;
    private string? _windowDisplayName;
    private bool _lastCaptureFailed = true;
    private int _lastFrameWidth;
    private int _lastFrameHeight;

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
                return;
            }

            _lastError = null;
            _lastCaptureFailed = false;
            EmptyStateTextBlock.Visibility = Visibility.Collapsed;
            _captureCount++;
            _lastFrameWidth = capturedFrame.Width;
            _lastFrameHeight = capturedFrame.Height;

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
        }
    }

    private double AverageMilliseconds(long totalMilliseconds)
    {
        return _captureCount == 0 ? 0d : totalMilliseconds / (double)_captureCount;
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
