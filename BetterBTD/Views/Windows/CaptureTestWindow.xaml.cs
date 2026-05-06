using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterBTD.Helpers.Extensions;
using BetterBTD.Models;
using Fischless.GameCapture;
using OpenCvSharp;
using Wpf.Ui.Controls;
using Size = OpenCvSharp.Size;

namespace BetterBTD.Views.Windows;

public partial class CaptureTestWindow : FluentWindow
{
    private IGameCapture? _capture;
    private Size _cachedFrameSize;
    private readonly Stopwatch _statsUpdateTimer = new();
    private long _captureElapsedMilliseconds;
    private long _transferElapsedMilliseconds;
    private long _captureCount;
    private string _captureModeName = "Unknown";
    private string? _lastError;

    public CaptureTestWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    public void StartCapture(nint hWnd, GameCaptureOptions options, string? windowDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (hWnd == nint.Zero)
        {
            throw new ArgumentException("The selected window handle is invalid.", nameof(hWnd));
        }

        CaptureTargetTextBlock.Text = string.IsNullOrWhiteSpace(windowDisplayName)
            ? "目标窗口"
            : $"目标窗口: {windowDisplayName}";
        CaptureStatsTextBlock.Text = $"模式: {options.CaptureModeName}";
        _captureModeName = options.CaptureModeName;
        _statsUpdateTimer.Restart();

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
            EmptyStateTextBlock.Text = _lastError;
            EmptyStateTextBlock.Visibility = Visibility.Visible;
            CaptureStatsTextBlock.Text = $"模式: {_captureModeName} | 启动失败";
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
                EmptyStateTextBlock.Visibility = Visibility.Visible;
                EmptyStateTextBlock.Text = string.IsNullOrWhiteSpace(_lastError)
                    ? "等待捕获帧... / 捕获失败"
                    : _lastError;
                UpdateStatsText(failed: true);
                return;
            }

            _lastError = null;
            EmptyStateTextBlock.Visibility = Visibility.Collapsed;
            _captureCount++;

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

    private void UpdateStatsText(bool failed, int width = 0, int height = 0)
    {
        if (_statsUpdateTimer.IsRunning && _statsUpdateTimer.ElapsedMilliseconds < 250)
        {
            return;
        }

        _statsUpdateTimer.Restart();
        CaptureStatsTextBlock.Text = failed
            ? $"模式: {_captureModeName} | 最近一次捕获失败"
            : $"模式: {_captureModeName} | 帧尺寸: {width}x{height} | " +
              $"平均截图: {AverageMilliseconds(_captureElapsedMilliseconds):F2} ms | " +
              $"平均显示: {AverageMilliseconds(_transferElapsedMilliseconds):F2} ms | " +
              $"平均总耗时: {AverageMilliseconds(_captureElapsedMilliseconds + _transferElapsedMilliseconds):F2} ms | " +
              $"样本数: {_captureCount}";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
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
