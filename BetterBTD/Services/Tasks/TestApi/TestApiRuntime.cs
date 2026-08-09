using BetterBTD.Core.TestApi;

namespace BetterBTD.Services.Tasks.TestApi;

internal sealed class TestApiRuntime
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly TestApiCoordinator _coordinator;
    private readonly TestApiHttpServer _httpServer;
    private readonly TestApiRuntimeEnvironment _environment;

    private bool _isRunning;

    public TestApiRuntime()
        : this(
            TestApiCoordinator.Instance,
            new TestApiHttpServer(TestApiCoordinator.Instance),
            TestApiRuntimeEnvironment.Instance)
    {
    }

    internal TestApiRuntime(
        TestApiCoordinator coordinator,
        TestApiHttpServer httpServer,
        TestApiRuntimeEnvironment environment)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _httpServer = httpServer ?? throw new ArgumentNullException(nameof(httpServer));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
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

    public async Task StartAsync(
        TestApiLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            throw new ArgumentException("Test API launch options are not enabled.", nameof(options));
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                {
                    throw new InvalidOperationException("The BetterBTD test API is already running.");
                }
            }

            try
            {
                await _httpServer
                    .StartAsync(options.ListenUrl, options.Token, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                try
                {
                    await _httpServer.StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await _coordinator.StopAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        _environment.StopOwnedCapture();
                    }
                }

                throw;
            }

            lock (_syncRoot)
            {
                _isRunning = true;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(bool stopOwnedCapture = true)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(stopOwnedCapture).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void StopOwnedCapture()
    {
        _environment.StopOwnedCapture();
    }

    private async Task StopCoreAsync(bool stopOwnedCapture)
    {
        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
        }

        try
        {
            await _httpServer.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _coordinator.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                if (stopOwnedCapture)
                {
                    _environment.StopOwnedCapture();
                }
            }
        }
    }
}
