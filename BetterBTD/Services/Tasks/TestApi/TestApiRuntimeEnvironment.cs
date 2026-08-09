using System.Windows;
using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.TestApi;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Models.TestApi;
using BetterBTD.Services.Tasks.RobotControl;
using Fischless.GameCapture;

namespace BetterBTD.Services.Tasks.TestApi;

internal sealed class TestApiRuntimeEnvironment : ITestApiRuntimeEnvironment
{
    private static readonly Lazy<TestApiRuntimeEnvironment> InstanceHolder = new(
        () => new TestApiRuntimeEnvironment());

    private readonly object _syncRoot = new();
    private readonly ScriptTaskFlowExecutor _scriptExecutor = ScriptTaskFlowExecutor.Instance;
    private readonly GameCaptureService _captureService = GameCaptureService.Instance;
    private readonly ConfigurationService _configurationService = ConfigurationService.Instance;
    private readonly ScriptInputSimulationService _inputService = ScriptInputSimulationService.Instance;

    private bool _captureOwnedByTestApi;

    private TestApiRuntimeEnvironment()
    {
    }

    public static TestApiRuntimeEnvironment Instance => InstanceHolder.Value;

    public bool IsScriptExecutorRunning => _scriptExecutor.IsRunning;

    public bool IsAutoTaskRunning => AutoTaskCoordinator.Instance.IsRunning;

    public bool IsRobotTaskRunning => RobotTaskRuntime.Instance.IsRunning;

    public ScriptExecutionProgressSnapshot? CurrentProgress => _scriptExecutor.CurrentProgress;

    public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged
    {
        add => _scriptExecutor.ProgressChanged += value;
        remove => _scriptExecutor.ProgressChanged -= value;
    }

    public event EventHandler<ScriptExecutionRuntimeLogEntry>? RuntimeLogEmitted
    {
        add => _scriptExecutor.RuntimeLogEmitted += value;
        remove => _scriptExecutor.RuntimeLogEmitted -= value;
    }

    public TestApiConfigurationSnapshot GetConfigurationSnapshot()
    {
        var configuration = _configurationService.Current;
        return new TestApiConfigurationSnapshot
        {
            TargetWindowTitle = configuration.MaskWindowTargetTitle,
            CaptureModeName = configuration.CaptureModeName,
            CaptureIntervalMs = configuration.CaptureIntervalMs,
            GameLanguageCode = configuration.GameLanguageCode,
            KeyboardMouseSimulationModeName = configuration.KeyboardMouseSimulationModeName
        };
    }

    public TestApiCaptureSnapshot GetCaptureSnapshot()
    {
        var options = _captureService.CurrentOptions;
        TestApiWindowSnapshot? window = null;
        if (_captureService.IsRunning && _captureService.TryGetCurrentWindowInfo(out var windowInfo))
        {
            window = new TestApiWindowSnapshot
            {
                Handle = windowInfo.Handle.ToInt64(),
                Title = windowInfo.Title,
                ClientWidth = windowInfo.ClientWidth,
                ClientHeight = windowInfo.ClientHeight,
                ScaleFactor = windowInfo.ScaleFactor
            };
        }

        return new TestApiCaptureSnapshot
        {
            IsRunning = _captureService.IsRunning,
            TargetWindowTitle = _captureService.TargetWindowTitle,
            CurrentWindowTitle = _captureService.CurrentWindowTitle,
            CaptureModeName = options.CaptureModeName,
            CaptureIntervalMs = options.CaptureIntervalMs,
            AutoFixWin11BitBlt = options.AutoFixWin11BitBlt,
            Window = window
        };
    }

    public async Task<TestApiCaptureSnapshot> StartCaptureAsync(
        TestApiCaptureStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = BuildCaptureOptions(request);

        var started = await InvokeOnDispatcherAsync(
                () =>
                {
                    _captureService.Configure(options);
                    return request.WindowHandle is long windowHandle
                        ? _captureService.TryStart(new nint(windowHandle), out _, options)
                        : _captureService.TryStart(options, out _);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!started)
        {
            throw TestApiRequestException.Conflict(
                TestApiErrorCodes.CaptureStartFailed,
                "The target game window was not found or is not available.");
        }

        lock (_syncRoot)
        {
            _captureOwnedByTestApi = true;
        }

        return GetCaptureSnapshot();
    }

    public bool RequestPause() => _scriptExecutor.RequestPause();

    public bool Resume() => _scriptExecutor.Resume();

    public void ReleaseAllKeys() => _inputService.ReleaseAllKeys();

    public Task<ScriptExecutionResult> ExecuteAsync(
        ScriptTaskFlow taskFlow,
        ScriptExecutionOptions options,
        CancellationToken cancellationToken)
    {
        return _scriptExecutor.ExecuteAsync(taskFlow, options, cancellationToken);
    }

    public void StopOwnedCapture()
    {
        lock (_syncRoot)
        {
            if (!_captureOwnedByTestApi)
            {
                return;
            }

            _captureOwnedByTestApi = false;
        }

        _captureService.Stop();
    }

    private GameCaptureOptions BuildCaptureOptions(TestApiCaptureStartRequest request)
    {
        var configuration = _configurationService.Current;
        var requestedMode = string.IsNullOrWhiteSpace(request.CaptureModeName)
            ? configuration.CaptureModeName
            : request.CaptureModeName.Trim();

        var captureMode = _captureService.AvailableCaptureModes.FirstOrDefault(
            mode => string.Equals(mode, requestedMode, StringComparison.OrdinalIgnoreCase));
        if (captureMode is null)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                $"Capture mode '{requestedMode}' is not available.");
        }

        return new GameCaptureOptions
        {
            CaptureModeName = captureMode,
            CaptureIntervalMs = request.CaptureIntervalMs ?? Math.Clamp(configuration.CaptureIntervalMs, 10, 2000),
            AutoFixWin11BitBlt = request.AutoFixWin11BitBlt ?? configuration.AutoFixWin11BitBlt
        };
    }

    private static Task<T> InvokeOnDispatcherAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;
    }
}
