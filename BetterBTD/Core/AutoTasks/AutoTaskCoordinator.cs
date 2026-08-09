using BetterBTD.Models.AutoTasks;
using BetterBTD.Core.GameControl;
using BetterBTD.Services.Tasks.Input;

namespace BetterBTD.Core.AutoTasks;

public sealed class AutoTaskCoordinator
{
    private static readonly Lazy<AutoTaskCoordinator> InstanceHolder = new(() => new AutoTaskCoordinator());

    private readonly object _syncRoot = new();
    private readonly AutoTaskRunner _runner;
    private readonly GameControlLeaseCoordinator _gameControlLeaseCoordinator;
    private readonly Action _releaseAllKeys;

    private bool _isRunning;
    private AutoTaskExecutionSession? _currentSession;
    private CancellationTokenSource? _currentCancellationSource;

    private AutoTaskCoordinator()
        : this(
            new AutoTaskRunner(),
            GameControlLeaseCoordinator.Instance,
            ScriptInputSimulationService.Instance.ReleaseAllKeys)
    {
    }

    internal AutoTaskCoordinator(
        AutoTaskRunner runner,
        GameControlLeaseCoordinator? gameControlLeaseCoordinator = null,
        Action? releaseAllKeys = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _gameControlLeaseCoordinator = gameControlLeaseCoordinator ?? GameControlLeaseCoordinator.Instance;
        _releaseAllKeys = releaseAllKeys ?? ScriptInputSimulationService.Instance.ReleaseAllKeys;
    }

    public static AutoTaskCoordinator Instance => InstanceHolder.Value;

    public event EventHandler<AutoTaskProgressSnapshot>? ProgressChanged;

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

    public AutoTaskProgressSnapshot? CurrentProgress
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentSession?.GetSnapshot();
            }
        }
    }

    public bool RequestPause()
    {
        return _runner.RequestPause();
    }

    public bool Resume()
    {
        return _runner.Resume();
    }

    public bool RequestStop()
    {
        lock (_syncRoot)
        {
            if (_currentCancellationSource is null)
            {
                return false;
            }

            _currentCancellationSource.Cancel();
            return true;
        }
    }

    public async Task<AutoTaskExecutionResult> ExecuteAsync(
        AutoTaskRequest request,
        AutoTaskExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var leaseOwnerId = $"auto-task-{Guid.NewGuid():N}";
        if (!_gameControlLeaseCoordinator.TryAcquire(
                GameControlOwnerKind.AutoTask,
                leaseOwnerId,
                out var gameControlLease))
        {
            throw new InvalidOperationException(
                "Another BetterBTD game-control operation is already running or input control is unavailable.");
        }

        using var ownedGameControlLease = gameControlLease;
        using var gameControlContext = GameControlLeaseContext.Push(leaseOwnerId);

        CancellationTokenSource linkedCancellationSource;
        lock (_syncRoot)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Another auto task is already running.");
            }

            linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentCancellationSource = linkedCancellationSource;
            _isRunning = true;
        }

        try
        {
            var executionTask = _runner.ExecuteAsync(request, options, linkedCancellationSource.Token);
            var session = _runner.CurrentSession;
            AttachCurrentSession(session);

            var result = await executionTask.ConfigureAwait(false);
            return result;
        }
        finally
        {
            DetachCurrentSession();

            lock (_syncRoot)
            {
                _currentCancellationSource?.Dispose();
                _currentCancellationSource = null;
                _currentSession = null;
                _isRunning = false;
            }

            try
            {
                _releaseAllKeys();
                gameControlLease.ConfirmInputReleased();
            }
            catch
            {
                gameControlLease.MarkPoisoned();
                throw;
            }
        }
    }

    private void AttachCurrentSession(AutoTaskExecutionSession? session)
    {
        if (session is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            _currentSession = session;
            _currentSession.ProgressChanged += OnCurrentSessionProgressChanged;
        }

        ProgressChanged?.Invoke(this, session.GetSnapshot());
    }

    private void DetachCurrentSession()
    {
        AutoTaskExecutionSession? session;
        lock (_syncRoot)
        {
            session = _currentSession;
            _currentSession = null;
        }

        if (session is not null)
        {
            session.ProgressChanged -= OnCurrentSessionProgressChanged;
        }
    }

    private void OnCurrentSessionProgressChanged(object? sender, AutoTaskProgressSnapshot snapshot)
    {
        ProgressChanged?.Invoke(this, snapshot);
    }
}
