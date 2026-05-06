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
    private long _captureElapsedMilliseconds;
    private long _transferElapsedMilliseconds;
    private long _captureCount;
    private string _captureModeName = "Unknown";

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

        _capture = GameCaptureFactory.Create(ParseCaptureMode(options.CaptureModeName));
        _capture.Start(hWnd, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["autoFixWin11BitBlt"] = options.AutoFixWin11BitBlt
        });

        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_capture is null)
        {
            return;
        }

        var captureStopwatch = Stopwatch.StartNew();
        using var capturedFrame = _capture.Capture();
        captureStopwatch.Stop();

        _captureElapsedMilliseconds += captureStopwatch.ElapsedMilliseconds;

        if (capturedFrame is null || capturedFrame.Empty())
        {
            EmptyStateTextBlock.Visibility = Visibility.Visible;
            CaptureStatsTextBlock.Text = $"模式: {_captureModeName} | 最近一次捕获失败";
            return;
        }

        EmptyStateTextBlock.Visibility = Visibility.Collapsed;
        _captureCount++;

        var transferStopwatch = Stopwatch.StartNew();
        if (_cachedFrameSize != capturedFrame.Size())
        {
            DisplayCaptureResultImage.Source = capturedFrame.ToWriteableBitmap();
            _cachedFrameSize = capturedFrame.Size();
        }
        else if (DisplayCaptureResultImage.Source is WriteableBitmap bitmap)
        {
            capturedFrame.UpdateWriteableBitmap(bitmap);
        }
        transferStopwatch.Stop();

        _transferElapsedMilliseconds += transferStopwatch.ElapsedMilliseconds;
        CaptureStatsTextBlock.Text =
            $"模式: {_captureModeName} | 帧尺寸: {capturedFrame.Width}x{capturedFrame.Height} | " +
            $"平均截图: {AverageMilliseconds(_captureElapsedMilliseconds):F2} ms | " +
            $"平均显示: {AverageMilliseconds(_transferElapsedMilliseconds):F2} ms | " +
            $"平均总耗时: {AverageMilliseconds(_captureElapsedMilliseconds + _transferElapsedMilliseconds):F2} ms | " +
            $"样本数: {_captureCount}";
    }

    private double AverageMilliseconds(long totalMilliseconds)
    {
        return _captureCount == 0 ? 0d : totalMilliseconds / (double)_captureCount;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _capture?.Dispose();
        _capture = null;
        _cachedFrameSize = default;
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
