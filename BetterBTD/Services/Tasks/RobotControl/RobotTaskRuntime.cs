using BetterBTD.Core.RobotControl;
using BetterBTD.Core.GameControl;
using BetterBTD.Models.RobotControl;
using BetterBTD.Services.Tasks.Input;
using BetterBTD.Services.ChildSession;

namespace BetterBTD.Services.Tasks.RobotControl;

public sealed class RobotTaskRuntime
{
    private static readonly Lazy<RobotTaskRuntime> InstanceHolder = new(() => new RobotTaskRuntime());

    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly RobotTaskCoordinator _coordinator;
    private readonly RobotTaskHttpServer _httpServer;
    private readonly GameControlLeaseCoordinator _gameControlLeaseCoordinator;
    private readonly Action _releaseAllKeys;

    private CancellationTokenSource? _cancellationSource;
    private Task? _uiAutomationLoopTask;
    private GameControlLease? _gameControlLease;
    private bool _isRunning;

    public RobotTaskRuntime()
        : this(
            RobotTaskCoordinator.Instance,
            new RobotTaskHttpServer(RobotTaskCoordinator.Instance),
            GameControlLeaseCoordinator.Instance,
            ScriptInputSimulationService.Instance.ReleaseAllKeys)
    {
    }

    internal RobotTaskRuntime(
        RobotTaskCoordinator coordinator,
        RobotTaskHttpServer httpServer,
        GameControlLeaseCoordinator? gameControlLeaseCoordinator = null,
        Action? releaseAllKeys = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _httpServer = httpServer ?? throw new ArgumentNullException(nameof(httpServer));
        _gameControlLeaseCoordinator = gameControlLeaseCoordinator ?? GameControlLeaseCoordinator.Instance;
        _releaseAllKeys = releaseAllKeys ?? ScriptInputSimulationService.Instance.ReleaseAllKeys;
    }

    public static RobotTaskRuntime Instance => InstanceHolder.Value;

    public event EventHandler<RobotTaskStatusSnapshot>? StatusChanged
    {
        add => _coordinator.StatusChanged += value;
        remove => _coordinator.StatusChanged -= value;
    }

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

    public RobotTaskStatusSnapshot CurrentStatus => _coordinator.GetStatusSnapshot();

    public async Task StartAsync(
        RobotTaskRuntimeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RobotTaskRuntimeOptions();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StartCoreAsync(options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StartCoreAsync(
        RobotTaskRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ChildSessionRuntimeState.EnsurePrimaryCanControl();

        var leaseOwnerId = $"robot-task-{Guid.NewGuid():N}";
        if (!_gameControlLeaseCoordinator.TryAcquire(
                GameControlOwnerKind.RobotTask,
                leaseOwnerId,
                out var gameControlLease))
        {
            throw new InvalidOperationException(
                "Another BetterBTD game-control operation is already running or input control is unavailable.");
        }

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                gameControlLease.Dispose();
                throw new InvalidOperationException("Robot task runtime is already running.");
            }

            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();
            _gameControlLease = gameControlLease;
        }

        try
        {
            using var gameControlContext = GameControlLeaseContext.Push(leaseOwnerId);
            _coordinator.Start(options.ListenUrl);
            await _httpServer.StartAsync(options.ListenUrl, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var runtimeToken = GetRuntimeToken();
            lock (_syncRoot)
            {
                _uiAutomationLoopTask = Task.Run(
                    () => RunUiAutomationLoopAsync(options.UiAutomationPollIntervalMs, runtimeToken),
                    CancellationToken.None);
            }
        }
        catch
        {
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        Task? uiAutomationLoopTask;
        CancellationTokenSource? cancellationSource;
        GameControlLease? gameControlLease;

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            cancellationSource = _cancellationSource;
            uiAutomationLoopTask = _uiAutomationLoopTask;
            _cancellationSource = null;
            _uiAutomationLoopTask = null;
            gameControlLease = _gameControlLease;
            _gameControlLease = null;
        }

        cancellationSource?.Cancel();

        try
        {
            try
            {
                _coordinator.Stop();
            }
            finally
            {
                try
                {
                    await _httpServer.StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (uiAutomationLoopTask is not null)
                    {
                        try
                        {
                            await uiAutomationLoopTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }
            }
        }
        finally
        {
            try
            {
                _releaseAllKeys();
                gameControlLease?.ConfirmInputReleased();
            }
            catch
            {
                gameControlLease?.MarkPoisoned();
                throw;
            }
            finally
            {
                cancellationSource?.Dispose();
                gameControlLease?.Dispose();
            }
        }
    }

    private async Task RunUiAutomationLoopAsync(int pollIntervalMs, CancellationToken cancellationToken)
    {
        var delayMs = Math.Clamp(pollIntervalMs <= 0 ? 300 : pollIntervalMs, 100, 5000);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _coordinator.TryRunUiAutomationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private CancellationToken GetRuntimeToken()
    {
        lock (_syncRoot)
        {
            return _cancellationSource?.Token ?? CancellationToken.None;
        }
    }
}
