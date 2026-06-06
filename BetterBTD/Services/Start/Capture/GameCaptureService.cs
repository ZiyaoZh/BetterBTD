using BetterBTD.Models;
using BetterBTD.Services.Diagnostics;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterBTD.Services.Start.Capture;

public sealed class GameCaptureService
{
    private const int MinimumCaptureIntervalMs = 10;
    private const int MaximumCaptureIntervalMs = 2000;
    private const int CaptureLoopStopTimeoutMs = 1000;
    private const int MinimumFreshFrameAgeMs = 1500;
    private const int MaximumFreshFrameAgeMs = 5000;
    private const int MinimumRecoveryFrameAgeMs = 2000;
    private const int MaximumRecoveryFrameAgeMs = 5000;

    private static readonly Lazy<GameCaptureService> InstanceHolder = new(() => new GameCaptureService());

    private readonly object _syncRoot = new();
    private readonly GameWindowInfoService _gameWindowInfoService;
    private readonly TemplateMatchService _templateMatchService;
    private readonly GameCaptureDiagnosticsService _diagnosticsService;
    private readonly IReadOnlyList<string> _availableCaptureModes;

    private IGameCapture? _gameCapture;
    private CancellationTokenSource? _captureLoopCancellationSource;
    private Task? _captureLoopTask;
    private Mat? _latestFrame;
    private long _captureAttemptSequence;
    private long _publishedFrameSequence;
    private long _latestFrameSequence;
    private long _latestSourceSequence;
    private DateTimeOffset _latestFrameCapturedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _latestFramePublishedAt = DateTimeOffset.MinValue;
    private ulong _latestFrameFingerprint;
    private int _sameFingerprintPublishStreak;
    private bool _recoveryRequested;
    private GameCaptureOptions _currentOptions = new();
    private nint _currentWindowHandle;
    private string _currentWindowTitle = string.Empty;
    private bool _isRunning;

    private GameCaptureService()
    {
        _gameWindowInfoService = GameWindowInfoService.Instance;
        _templateMatchService = TemplateMatchService.Instance;
        _diagnosticsService = GameCaptureDiagnosticsService.Instance;
        _availableCaptureModes = Array.AsReadOnly(
            GameCaptureFactory
                .ModeNames()
                .OrderBy(modeName => modeName switch
                {
                    nameof(CaptureModes.WindowsGraphicsCapture) => 0,
                    nameof(CaptureModes.WindowsGraphicsCaptureHdr) => 1,
                    nameof(CaptureModes.BitBlt) => 10,
                    nameof(CaptureModes.DwmGetDxSharedSurface) => 20,
                    _ => 100
                })
                .ToArray());
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
            _currentOptions = NormalizeOptions(options);
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
        CancellationTokenSource? captureLoopCancellationSource;
        Task? captureLoopTask;
        Mat? latestFrameToDispose;
        var shouldRaiseEvent = false;

        lock (_syncRoot)
        {
            if (_gameCapture is null && !_isRunning)
            {
                return;
            }

            captureToDispose = _gameCapture;
            captureLoopCancellationSource = _captureLoopCancellationSource;
            captureLoopTask = _captureLoopTask;
            latestFrameToDispose = _latestFrame;
            _gameCapture = null;
            _captureLoopCancellationSource = null;
            _captureLoopTask = null;
            _latestFrame = null;
            ResetLatestFrameStateUnderLock();
            _recoveryRequested = false;
            _currentWindowHandle = nint.Zero;
            _currentWindowTitle = string.Empty;
            shouldRaiseEvent = _isRunning;
            _isRunning = false;
        }

        captureLoopCancellationSource?.Cancel();
        latestFrameToDispose?.Dispose();
        var stopped = WaitForCaptureLoopToStop(
            captureLoopTask,
            TimeSpan.FromMilliseconds(CaptureLoopStopTimeoutMs));
        _diagnosticsService.StopSession(stopped ? "Stopped" : "Stop timed out; cleanup moved to background.");

        if (stopped)
        {
            captureLoopCancellationSource?.Dispose();
            captureToDispose?.Dispose();
        }
        else
        {
            _ = Task.Run(() => CleanupTimedOutCapture(captureToDispose, captureLoopCancellationSource, captureLoopTask));
        }

        if (shouldRaiseEvent)
        {
            RaiseRunningStateChanged(false);
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
        long frameSequence;
        DateTimeOffset publishedAt;
        int width;
        int height;
        ulong fingerprint;
        var shouldRecordFailure = false;
        lock (_syncRoot)
        {
            var latestFrame = _latestFrame;
            if (!_isRunning || latestFrame is null || latestFrame.Empty())
            {
                shouldRecordFailure = true;
            }
            else if (DateTimeOffset.UtcNow - _latestFramePublishedAt > ResolveFreshFrameMaxAge(_currentOptions))
            {
                shouldRecordFailure = true;
            }

            if (shouldRecordFailure)
            {
                _diagnosticsService.RecordFrameRequestFailed(_isRunning, latestFrame is not null && !latestFrame.Empty());
                return false;
            }

            frameSequence = _latestFrameSequence;
            publishedAt = _latestFramePublishedAt;
            width = latestFrame!.Width;
            height = latestFrame.Height;
            fingerprint = _latestFrameFingerprint;
            frame = latestFrame.Clone();
        }

        _diagnosticsService.RecordFrameServed(frameSequence, publishedAt, width, height, fingerprint);
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

        Stop();

        var normalizedOptions = NormalizeOptions(options);
        var capture = GameCaptureFactory.Create(ParseCaptureMode(options.CaptureModeName));
        var captureLoopCancellationSource = new CancellationTokenSource();
        Task? captureLoopTask = null;

        var shouldRaiseEvent = false;

        try
        {
            capture.Start(windowInfo.Handle, CreateCaptureSettings(normalizedOptions));
            _diagnosticsService.StartSession(windowInfo, normalizedOptions);
            captureLoopTask = Task.Factory.StartNew(
                () => RunCaptureLoop(capture, normalizedOptions, captureLoopCancellationSource.Token),
                captureLoopCancellationSource.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            lock (_syncRoot)
            {
                _gameCapture = capture;
                _captureLoopCancellationSource = captureLoopCancellationSource;
                _captureLoopTask = captureLoopTask;
                _captureAttemptSequence = 0;
                _publishedFrameSequence = 0;
                ResetLatestFrameStateUnderLock();
                _recoveryRequested = false;
                _currentOptions = normalizedOptions with { };
                _currentWindowHandle = windowInfo.Handle;
                _currentWindowTitle = windowInfo.Title;
                shouldRaiseEvent = !_isRunning && capture.IsCapturing;
                _isRunning = capture.IsCapturing;
            }
        }
        catch
        {
            captureLoopCancellationSource.Cancel();
            WaitForCaptureLoopToStop(captureLoopTask, TimeSpan.FromMilliseconds(CaptureLoopStopTimeoutMs));
            _diagnosticsService.StopSession("Start failed.");
            captureLoopCancellationSource.Dispose();
            capture.Dispose();
            throw;
        }

        if (shouldRaiseEvent)
        {
            RaiseRunningStateChanged(true);
        }
    }

    private void RaiseRunningStateChanged(bool isRunning)
    {
        var handler = RunningStateChanged;
        if (handler is null)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => handler(this, isRunning));
            return;
        }

        handler(this, isRunning);
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

    private void RunCaptureLoop(
        IGameCapture capture,
        GameCaptureOptions options,
        CancellationToken cancellationToken)
    {
        var captureInterval = TimeSpan.FromMilliseconds(options.CaptureIntervalMs);
        _diagnosticsService.RecordCaptureLoopStarted();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var attemptId = Interlocked.Increment(ref _captureAttemptSequence);
                _diagnosticsService.BeginCaptureAttempt(attemptId);
                var captureStopwatch = System.Diagnostics.Stopwatch.StartNew();
                Mat? capturedFrame = null;
                try
                {
                    capturedFrame = capture.Capture();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    captureStopwatch.Stop();
                    _diagnosticsService.RecordCaptureAttemptCancelled(attemptId, captureStopwatch.Elapsed);
                    break;
                }
                catch (Exception ex)
                {
                    captureStopwatch.Stop();
                    _diagnosticsService.RecordCaptureAttemptFaulted(attemptId, captureStopwatch.Elapsed, ex);
                    ClearLatestFrame(capture, "Capture attempt faulted.");
                }

                if (capturedFrame is not null)
                {
                    captureStopwatch.Stop();
                    if (capturedFrame.Empty())
                    {
                        _diagnosticsService.RecordCaptureAttemptReturnedEmptyFrame(
                            attemptId,
                            captureStopwatch.Elapsed,
                            capturedFrame.Width,
                            capturedFrame.Height);
                        capturedFrame.Dispose();
                        ClearLatestFrame(capture, "Capture returned empty frame.");
                    }
                    else
                    {
                        PublishLatestFrame(capture, capturedFrame, attemptId, captureStopwatch.Elapsed, options);
                    }
                }
                else
                {
                    captureStopwatch.Stop();
                    _diagnosticsService.RecordCaptureAttemptReturnedNull(attemptId, captureStopwatch.Elapsed);
                }

                try
                {
                    Task.Delay(captureInterval, cancellationToken).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _diagnosticsService.RecordCaptureLoopStopped();
        }
    }

    private void PublishLatestFrame(
        IGameCapture capture,
        Mat capturedFrame,
        long attemptId,
        TimeSpan captureElapsed,
        GameCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(capturedFrame);

        var fingerprint = GameCaptureDiagnosticsService.CalculateFingerprint(capturedFrame);
        var publishedAt = DateTimeOffset.UtcNow;
        var hasSourceMetadata = TryGetFrameMetadata(capture, out var sourceMetadata);
        var sourceSequence = hasSourceMetadata ? sourceMetadata.SourceSequence : 0;
        var capturedAt = hasSourceMetadata ? sourceMetadata.CapturedAt : publishedAt;
        var discardReason = string.Empty;
        long frameSequence = 0;
        Mat? previousFrame = null;
        var discarded = false;
        var shouldRecover = false;
        var published = false;
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_gameCapture, capture))
            {
                discardReason = "Capture instance is no longer active.";
                discarded = true;
            }
            else
            {
                var isFirstFrame = _latestFrame is null || _latestFrameSequence <= 0;
                var sourceAdvanced = hasSourceMetadata && sourceSequence != _latestSourceSequence;
                var fingerprintChanged = isFirstFrame || fingerprint != _latestFrameFingerprint;
                var shouldPublish = isFirstFrame || (hasSourceMetadata ? sourceAdvanced : fingerprintChanged);
                if (!shouldPublish)
                {
                    discardReason = hasSourceMetadata
                        ? $"Source sequence unchanged ({sourceSequence})."
                        : "Frame fingerprint unchanged.";
                    discarded = true;
                    shouldRecover = hasSourceMetadata &&
                                    !_recoveryRequested &&
                                    _latestFramePublishedAt != DateTimeOffset.MinValue &&
                                    DateTimeOffset.UtcNow - _latestFramePublishedAt > ResolveRecoveryFrameMaxAge(options);
                }

                if (!discarded)
                {
                    previousFrame = _latestFrame;
                    _latestFrame = capturedFrame;
                    frameSequence = ++_publishedFrameSequence;
                    _latestFrameSequence = frameSequence;
                    _latestSourceSequence = sourceSequence;
                    _latestFrameCapturedAt = capturedAt;
                    _latestFramePublishedAt = publishedAt;
                    _sameFingerprintPublishStreak = fingerprintChanged ? 1 : _sameFingerprintPublishStreak + 1;
                    _latestFrameFingerprint = fingerprint;
                    published = true;
                }
            }

            if (shouldRecover)
            {
                _recoveryRequested = true;
            }
        }

        if (discarded)
        {
            capturedFrame.Dispose();
            _diagnosticsService.RecordCaptureAttemptDiscarded(attemptId, captureElapsed, discardReason);
            if (shouldRecover)
            {
                ScheduleCaptureRecovery(capture, discardReason);
            }

            return;
        }

        if (!published)
        {
            capturedFrame.Dispose();
            _diagnosticsService.RecordCaptureAttemptDiscarded(attemptId, captureElapsed, "Frame was not published.");
            return;
        }

        previousFrame?.Dispose();
        _diagnosticsService.RecordFramePublished(
            attemptId,
            frameSequence,
            publishedAt,
            capturedFrame.Width,
            capturedFrame.Height,
            fingerprint,
            captureElapsed,
            options.CaptureIntervalMs);
    }

    private void ClearLatestFrame(IGameCapture capture, string reason)
    {
        ArgumentNullException.ThrowIfNull(capture);

        Mat? frameToDispose;
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_gameCapture, capture))
            {
                return;
            }

            frameToDispose = _latestFrame;
            _latestFrame = null;
            ResetLatestFrameStateUnderLock();
        }

        frameToDispose?.Dispose();
        _diagnosticsService.RecordLatestFrameCleared(reason);
    }

    private static bool WaitForCaptureLoopToStop(Task? captureLoopTask, TimeSpan timeout)
    {
        if (captureLoopTask is null)
        {
            return true;
        }

        try
        {
            return captureLoopTask.Wait(timeout);
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (AggregateException aggregateException) when (aggregateException.InnerExceptions.All(static ex => ex is OperationCanceledException))
        {
            return true;
        }
    }

    private static void CleanupTimedOutCapture(
        IGameCapture? capture,
        CancellationTokenSource? cancellationTokenSource,
        Task? captureLoopTask)
    {
        try
        {
            captureLoopTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException aggregateException) when (aggregateException.InnerExceptions.All(static ex => ex is OperationCanceledException))
        {
        }
        finally
        {
            cancellationTokenSource?.Dispose();
            capture?.Dispose();
        }
    }

    private static bool TryGetFrameMetadata(
        IGameCapture capture,
        out GameCaptureFrameMetadata metadata)
    {
        if (capture is IGameCaptureFrameMetadataProvider metadataProvider &&
            metadataProvider.TryGetFrameMetadata(out metadata) &&
            metadata.SourceSequence > 0)
        {
            return true;
        }

        metadata = default;
        return false;
    }

    private static TimeSpan ResolveFreshFrameMaxAge(GameCaptureOptions options)
    {
        var captureInterval = Math.Clamp(options.CaptureIntervalMs, MinimumCaptureIntervalMs, MaximumCaptureIntervalMs);
        var maxAgeMilliseconds = Math.Clamp(
            captureInterval * 5,
            MinimumFreshFrameAgeMs,
            MaximumFreshFrameAgeMs);
        return TimeSpan.FromMilliseconds(maxAgeMilliseconds);
    }

    private static TimeSpan ResolveRecoveryFrameMaxAge(GameCaptureOptions options)
    {
        var captureInterval = Math.Clamp(options.CaptureIntervalMs, MinimumCaptureIntervalMs, MaximumCaptureIntervalMs);
        var maxAgeMilliseconds = Math.Clamp(
            captureInterval * 20,
            MinimumRecoveryFrameAgeMs,
            MaximumRecoveryFrameAgeMs);
        return TimeSpan.FromMilliseconds(maxAgeMilliseconds);
    }

    private void ScheduleCaptureRecovery(IGameCapture capture, string reason)
    {
        nint windowHandle;
        GameCaptureOptions options;
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_gameCapture, capture))
            {
                return;
            }

            windowHandle = _currentWindowHandle;
            options = _currentOptions with { };
        }

        if (windowHandle == nint.Zero)
        {
            return;
        }

        _diagnosticsService.RecordLatestFrameCleared($"Capture recovery scheduled | reason={reason}");
        _ = Task.Run(() =>
        {
            try
            {
                if (!TryStart(windowHandle, out _, options))
                {
                    lock (_syncRoot)
                    {
                        _recoveryRequested = false;
                    }
                }
            }
            catch
            {
                lock (_syncRoot)
                {
                    _recoveryRequested = false;
                }
            }
        });
    }

    private void ResetLatestFrameStateUnderLock()
    {
        _latestFrameSequence = 0;
        _latestSourceSequence = 0;
        _latestFrameCapturedAt = DateTimeOffset.MinValue;
        _latestFramePublishedAt = DateTimeOffset.MinValue;
        _latestFrameFingerprint = 0;
        _sameFingerprintPublishStreak = 0;
    }

    private static GameCaptureOptions NormalizeOptions(GameCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            CaptureIntervalMs = Math.Clamp(options.CaptureIntervalMs, MinimumCaptureIntervalMs, MaximumCaptureIntervalMs)
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
