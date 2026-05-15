using BetterBTD.Models;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterBTD.Services.Start.Capture;

public sealed class GameCaptureService
{
    private static readonly Lazy<GameCaptureService> InstanceHolder = new(() => new GameCaptureService());

    private readonly object _syncRoot = new();
    private readonly GameWindowInfoService _gameWindowInfoService;
    private readonly TemplateMatchService _templateMatchService;
    private readonly IReadOnlyList<string> _availableCaptureModes;

    private IGameCapture? _gameCapture;
    private GameCaptureOptions _currentOptions = new();
    private nint _currentWindowHandle;
    private string _currentWindowTitle = string.Empty;
    private bool _isRunning;

    private GameCaptureService()
    {
        _gameWindowInfoService = GameWindowInfoService.Instance;
        _templateMatchService = TemplateMatchService.Instance;
        _availableCaptureModes = Array.AsReadOnly(GameCaptureFactory.ModeNames());
    }

    public static GameCaptureService Instance => InstanceHolder.Value;

    public event EventHandler<bool>? RunningStateChanged;

    public IReadOnlyList<string> AvailableCaptureModes => _availableCaptureModes;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning;
            }
        }
    }

    public string TargetWindowTitle => _gameWindowInfoService.TargetWindowTitle;

    public string CurrentWindowTitle
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentWindowTitle;
            }
        }
    }

    public GameCaptureOptions CurrentOptions
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentOptions with { };
            }
        }
    }

    public void Configure(GameCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (_syncRoot)
        {
            _currentOptions = options with { };
        }
    }

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        return _gameWindowInfoService.TryGetTargetWindowInfo(out windowInfo);
    }

    public bool TryGetCurrentWindowInfo(out GameWindowInfo windowInfo)
    {
        nint windowHandle;
        lock (_syncRoot)
        {
            windowHandle = _currentWindowHandle;
        }

        if (windowHandle != nint.Zero &&
            _gameWindowInfoService.TryGetWindowInfo(windowHandle, out windowInfo))
        {
            return true;
        }

        return _gameWindowInfoService.TryGetTargetWindowInfo(out windowInfo);
    }

    public void Start()
    {
        if (!TryStart(out _))
        {
            throw new InvalidOperationException(
                $"Target game window '{TargetWindowTitle}' was not found or is not available.");
        }
    }

    public bool TryStart(out GameWindowInfo windowInfo)
    {
        return TryStart(CurrentOptions, out windowInfo);
    }

    public bool TryStart(GameCaptureOptions options, out GameWindowInfo windowInfo)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!_gameWindowInfoService.TryGetTargetWindowInfo(out windowInfo))
        {
            return false;
        }

        StartCore(windowInfo, options);
        return true;
    }

    public void Start(nint windowHandle, GameCaptureOptions? options = null)
    {
        if (!TryStart(windowHandle, out _, options))
        {
            throw new InvalidOperationException("The specified target window handle is not available.");
        }
    }

    public bool TryStart(nint windowHandle, out GameWindowInfo windowInfo, GameCaptureOptions? options = null)
    {
        if (!_gameWindowInfoService.TryGetWindowInfo(windowHandle, out windowInfo))
        {
            return false;
        }

        StartCore(windowInfo, options ?? CurrentOptions);
        return true;
    }

    public void Start(GameWindowInfo windowInfo, GameCaptureOptions? options = null)
    {
        StartCore(windowInfo, options ?? CurrentOptions);
    }

    public void Restart()
    {
        nint currentWindowHandle;
        lock (_syncRoot)
        {
            currentWindowHandle = _currentWindowHandle;
        }

        if (currentWindowHandle != nint.Zero && TryStart(currentWindowHandle, out _))
        {
            return;
        }

        Start();
    }

    public void Stop()
    {
        IGameCapture? captureToDispose;
        var shouldRaiseEvent = false;

        lock (_syncRoot)
        {
            if (_gameCapture is null && !_isRunning)
            {
                return;
            }

            captureToDispose = _gameCapture;
            _gameCapture = null;
            _currentWindowHandle = nint.Zero;
            _currentWindowTitle = string.Empty;
            shouldRaiseEvent = _isRunning;
            _isRunning = false;
        }

        captureToDispose?.Dispose();

        if (shouldRaiseEvent)
        {
            RunningStateChanged?.Invoke(this, false);
        }
    }

    public void Shutdown()
    {
        Stop();
    }

    public Mat CaptureFrame()
    {
        if (!TryCaptureFrame(out var frame))
        {
            throw new InvalidOperationException("Game capture is not running or no frame is currently available.");
        }

        return frame;
    }

    public bool TryCaptureFrame(out Mat frame)
    {
        frame = null!;

        IGameCapture? capture;
        lock (_syncRoot)
        {
            if (!_isRunning || _gameCapture is null)
            {
                return false;
            }

            capture = _gameCapture;
        }

        var capturedFrame = capture.Capture();
        if (capturedFrame is null)
        {
            return false;
        }

        if (capturedFrame.Empty())
        {
            capturedFrame.Dispose();
            return false;
        }

        frame = capturedFrame;
        return true;
    }

    public bool TryCaptureFrame(out GameWindowInfo windowInfo, out Mat frame)
    {
        frame = null!;
        windowInfo = default;

        if (!TryGetCurrentWindowInfo(out windowInfo))
        {
            return false;
        }

        return TryCaptureFrame(out frame);
    }

    public Mat CaptureFrame(Rect captureRegion)
    {
        if (!TryCaptureFrame(out var fullFrame))
        {
            throw new InvalidOperationException("Game capture is not running or no frame is currently available.");
        }

        try
        {
            if (!TryNormalizeCaptureRegion(captureRegion, fullFrame.Width, fullFrame.Height, out var normalizedRegion))
            {
                throw new ArgumentOutOfRangeException(nameof(captureRegion), "The capture region is outside the available frame.");
            }

            using var regionFrame = new Mat(fullFrame, normalizedRegion);
            return regionFrame.Clone();
        }
        finally
        {
            fullFrame.Dispose();
        }
    }

    public bool TryCaptureFrame(Rect captureRegion, out Mat frame)
    {
        frame = null!;

        if (!TryCaptureFrame(out var fullFrame))
        {
            return false;
        }

        try
        {
            if (!TryNormalizeCaptureRegion(captureRegion, fullFrame.Width, fullFrame.Height, out var normalizedRegion))
            {
                return false;
            }

            using var regionFrame = new Mat(fullFrame, normalizedRegion);
            frame = regionFrame.Clone();
            return true;
        }
        finally
        {
            fullFrame.Dispose();
        }
    }

    public TemplateMatchInfo MatchTemplate(Mat templateImage, double threshold = 0.8d)
    {
        using var sourceFrame = CaptureFrame();
        return _templateMatchService.Match(sourceFrame, templateImage, threshold);
    }

    public bool TryMatchTemplate(Mat templateImage, out TemplateMatchInfo matchInfo, double threshold = 0.8d)
    {
        matchInfo = default;

        if (!TryCaptureFrame(out var sourceFrame))
        {
            return false;
        }

        using (sourceFrame)
        {
            return _templateMatchService.TryMatch(sourceFrame, templateImage, out matchInfo, threshold);
        }
    }

    public TemplateMatchInfo MatchTemplate(Rect captureRegion, Mat templateImage, double threshold = 0.8d)
    {
        using var sourceFrame = CaptureFrame(captureRegion);
        return _templateMatchService.Match(sourceFrame, templateImage, threshold);
    }

    public bool TryMatchTemplate(Rect captureRegion, Mat templateImage, out TemplateMatchInfo matchInfo, double threshold = 0.8d)
    {
        matchInfo = default;

        if (!TryCaptureFrame(captureRegion, out var sourceFrame))
        {
            return false;
        }

        using (sourceFrame)
        {
            return _templateMatchService.TryMatch(sourceFrame, templateImage, out matchInfo, threshold);
        }
    }

    private void StartCore(GameWindowInfo windowInfo, GameCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var capture = GameCaptureFactory.Create(ParseCaptureMode(options.CaptureModeName));

        IGameCapture? previousCapture = null;
        var shouldRaiseEvent = false;

        try
        {
            capture.Start(windowInfo.Handle, CreateCaptureSettings(options));

            lock (_syncRoot)
            {
                previousCapture = _gameCapture;
                _gameCapture = capture;
                _currentOptions = options with { };
                _currentWindowHandle = windowInfo.Handle;
                _currentWindowTitle = windowInfo.Title;
                shouldRaiseEvent = !_isRunning && capture.IsCapturing;
                _isRunning = capture.IsCapturing;
            }
        }
        catch
        {
            capture.Dispose();
            throw;
        }

        previousCapture?.Dispose();

        if (shouldRaiseEvent)
        {
            RunningStateChanged?.Invoke(this, true);
        }
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

    private static Dictionary<string, object> CreateCaptureSettings(GameCaptureOptions options)
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["autoFixWin11BitBlt"] = options.AutoFixWin11BitBlt
        };
    }

    private static bool TryNormalizeCaptureRegion(Rect captureRegion, int frameWidth, int frameHeight, out Rect normalizedRegion)
    {
        normalizedRegion = default;

        if (captureRegion.Width <= 0 || captureRegion.Height <= 0)
        {
            return false;
        }

        var x = Math.Max(0, captureRegion.X);
        var y = Math.Max(0, captureRegion.Y);
        var right = Math.Min(frameWidth, captureRegion.Right);
        var bottom = Math.Min(frameHeight, captureRegion.Bottom);
        var width = right - x;
        var height = bottom - y;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        normalizedRegion = new Rect((int)x, (int)y, (int)width, (int)height);
        return true;
    }
}

