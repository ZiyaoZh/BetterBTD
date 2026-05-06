using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterBTD.Models;
using Fischless.GameCapture;
using OpenCvSharp;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public partial class CaptureTestWindow : FluentWindow
{
    private IGameCapture? _capture;
    private WriteableBitmap? _writeableBitmap;
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
        using var displayFrame = CreateDisplayFrame(capturedFrame);
        UpdatePreview(displayFrame);
        transferStopwatch.Stop();

        _transferElapsedMilliseconds += transferStopwatch.ElapsedMilliseconds;
        CaptureStatsTextBlock.Text =
            $"模式: {_captureModeName} | 帧尺寸: {displayFrame.Width}x{displayFrame.Height} | " +
            $"平均截图: {AverageMilliseconds(_captureElapsedMilliseconds):F2} ms | " +
            $"平均显示: {AverageMilliseconds(_transferElapsedMilliseconds):F2} ms | " +
            $"平均总耗时: {AverageMilliseconds(_captureElapsedMilliseconds + _transferElapsedMilliseconds):F2} ms | " +
            $"样本数: {_captureCount}";
    }

    private void UpdatePreview(Mat frame)
    {
        var pixelFormat = frame.Channels() == 4 ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
        var stride = checked((int)frame.Step());
        var bufferSize = stride * frame.Rows;

        if (_writeableBitmap is null ||
            _writeableBitmap.PixelWidth != frame.Width ||
            _writeableBitmap.PixelHeight != frame.Height ||
            _writeableBitmap.Format != pixelFormat)
        {
            _writeableBitmap = new WriteableBitmap(
                frame.Width,
                frame.Height,
                96,
                96,
                pixelFormat,
                null);
            DisplayCaptureResultImage.Source = _writeableBitmap;
        }

        _writeableBitmap.WritePixels(
            new Int32Rect(0, 0, frame.Width, frame.Height),
            frame.Data,
            bufferSize,
            stride);
    }

    private static Mat CreateDisplayFrame(Mat sourceFrame)
    {
        return sourceFrame.Channels() switch
        {
            4 when sourceFrame.Depth() == MatType.CV_8U => sourceFrame.Clone(),
            3 when sourceFrame.Depth() == MatType.CV_8U => sourceFrame.Clone(),
            1 => ConvertGrayToBgr(sourceFrame),
            _ => ConvertUnsupportedFrame(sourceFrame)
        };
    }

    private static Mat ConvertGrayToBgr(Mat sourceFrame)
    {
        var converted = new Mat();
        Cv2.CvtColor(sourceFrame, converted, ColorConversionCodes.GRAY2BGR);
        return converted;
    }

    private static Mat ConvertUnsupportedFrame(Mat sourceFrame)
    {
        var normalized = new Mat();
        sourceFrame.ConvertTo(normalized, MatType.CV_8UC(sourceFrame.Channels()));

        if (normalized.Channels() == 4)
        {
            return normalized;
        }

        if (normalized.Channels() == 3)
        {
            return normalized;
        }

        normalized.Dispose();
        throw new NotSupportedException($"Unsupported capture frame format: channels={sourceFrame.Channels()}, depth={sourceFrame.Depth()}.");
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
        _writeableBitmap = null;
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
