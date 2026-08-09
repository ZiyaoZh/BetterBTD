using System.Threading;

namespace BetterBTD.Core.GameControl;

internal enum GameControlOwnerKind
{
    ScriptExecution,
    AutoTask,
    RobotTask,
    TestApiOperation,
    TestApiCapture
}

internal sealed class GameControlLeaseCoordinator
{
    private static readonly Lazy<GameControlLeaseCoordinator> InstanceHolder = new(
        () => new GameControlLeaseCoordinator());

    private readonly object _syncRoot = new();

    private string? _ownerId;
    private GameControlOwnerKind? _ownerKind;
    private int _referenceCount;
    private bool _isPoisoned;
    private TaskCompletionSource _idleSource = CreateCompletedIdleSource();

    internal GameControlLeaseCoordinator()
    {
    }

    public static GameControlLeaseCoordinator Instance => InstanceHolder.Value;

    public bool IsPoisoned
    {
        get
        {
            lock (_syncRoot)
            {
                return _isPoisoned;
            }
        }
    }

    public bool HasActiveLease
    {
        get
        {
            lock (_syncRoot)
            {
                return _ownerId is not null;
            }
        }
    }

    public bool TryAcquire(
        GameControlOwnerKind ownerKind,
        string ownerId,
        out GameControlLease lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        lock (_syncRoot)
        {
            if (_isPoisoned || _ownerId is not null)
            {
                lease = GameControlLease.None;
                return false;
            }

            _ownerId = ownerId;
            _ownerKind = ownerKind;
            _referenceCount = 1;
            _idleSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lease = new GameControlLease(this, ownerKind, ownerId);
            return true;
        }
    }

    public GameControlExecutionScope AcquireOrJoinForScriptExecution()
    {
        var ambientOwnerId = GameControlLeaseContext.CurrentOwnerId;
        lock (_syncRoot)
        {
            if (!string.IsNullOrWhiteSpace(ambientOwnerId) &&
                string.Equals(_ownerId, ambientOwnerId, StringComparison.Ordinal) &&
                _ownerKind is GameControlOwnerKind ownerKind)
            {
                _referenceCount++;
                return new GameControlExecutionScope(
                    new GameControlLease(this, ownerKind, ambientOwnerId),
                    contextScope: null);
            }
        }

        var ownerId = $"script-{Guid.NewGuid():N}";
        if (!TryAcquire(GameControlOwnerKind.ScriptExecution, ownerId, out var lease))
        {
            throw new InvalidOperationException(
                IsPoisoned
                    ? "Game input control is unavailable because a previous input release failed. Restart BetterBTD before running another controller."
                    : "Another BetterBTD game-control operation is already running.");
        }

        return new GameControlExecutionScope(lease, GameControlLeaseContext.Push(ownerId));
    }

    public bool IsHeldBy(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return false;
        }

        lock (_syncRoot)
        {
            return string.Equals(_ownerId, ownerId, StringComparison.Ordinal);
        }
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        Task idleTask;
        lock (_syncRoot)
        {
            idleTask = _idleSource.Task;
        }

        return idleTask.WaitAsync(cancellationToken);
    }

    internal void Release(GameControlOwnerKind ownerKind, string ownerId)
    {
        lock (_syncRoot)
        {
            if (_ownerKind != ownerKind ||
                !string.Equals(_ownerId, ownerId, StringComparison.Ordinal))
            {
                return;
            }

            _referenceCount--;
            if (_referenceCount > 0)
            {
                return;
            }

            _ownerId = null;
            _ownerKind = null;
            _referenceCount = 0;
            _idleSource.TrySetResult();
        }
    }

    internal void MarkPoisoned(GameControlOwnerKind ownerKind, string ownerId)
    {
        lock (_syncRoot)
        {
            if (_ownerKind == ownerKind &&
                string.Equals(_ownerId, ownerId, StringComparison.Ordinal))
            {
                _isPoisoned = true;
            }
        }
    }

    internal void ConfirmInputReleased(GameControlOwnerKind ownerKind, string ownerId)
    {
        lock (_syncRoot)
        {
            if (_ownerKind == ownerKind &&
                string.Equals(_ownerId, ownerId, StringComparison.Ordinal))
            {
                _isPoisoned = false;
            }
        }
    }

    private static TaskCompletionSource CreateCompletedIdleSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

internal sealed class GameControlLease : IDisposable
{
    internal static GameControlLease None { get; } = new();

    private readonly GameControlLeaseCoordinator? _coordinator;
    private readonly GameControlOwnerKind _ownerKind;
    private bool _disposed;

    private GameControlLease()
    {
        OwnerId = string.Empty;
    }

    internal GameControlLease(
        GameControlLeaseCoordinator coordinator,
        GameControlOwnerKind ownerKind,
        string ownerId)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _ownerKind = ownerKind;
        OwnerId = ownerId;
    }

    public string OwnerId { get; }

    public void MarkPoisoned()
    {
        if (!_disposed)
        {
            _coordinator?.MarkPoisoned(_ownerKind, OwnerId);
        }
    }

    public void ConfirmInputReleased()
    {
        if (!_disposed)
        {
            _coordinator?.ConfirmInputReleased(_ownerKind, OwnerId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator?.Release(_ownerKind, OwnerId);
    }
}

internal sealed class GameControlExecutionScope : IDisposable
{
    private readonly GameControlLease? _lease;
    private readonly IDisposable? _contextScope;
    private bool _disposed;

    internal GameControlExecutionScope(GameControlLease? lease, IDisposable? contextScope)
    {
        _lease = lease;
        _contextScope = contextScope;
    }

    public void MarkPoisoned()
    {
        _lease?.MarkPoisoned();
    }

    public void ConfirmInputReleased()
    {
        _lease?.ConfirmInputReleased();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _contextScope?.Dispose();
        _lease?.Dispose();
    }
}

internal static class GameControlLeaseContext
{
    private static readonly AsyncLocal<string?> CurrentOwnerHolder = new();

    public static string? CurrentOwnerId => CurrentOwnerHolder.Value;

    public static IDisposable Push(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var previousOwnerId = CurrentOwnerHolder.Value;
        CurrentOwnerHolder.Value = ownerId;
        return new RestoreContextScope(previousOwnerId);
    }

    private sealed class RestoreContextScope : IDisposable
    {
        private readonly string? _previousOwnerId;
        private bool _disposed;

        public RestoreContextScope(string? previousOwnerId)
        {
            _previousOwnerId = previousOwnerId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentOwnerHolder.Value = _previousOwnerId;
        }
    }
}
