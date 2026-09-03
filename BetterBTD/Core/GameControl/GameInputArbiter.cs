namespace BetterBTD.Core.GameControl;

public enum GameInputPriority
{
    Script = 100,
    Navigation = 200,
    TemporaryPopup = 300,
    ResultHandling = 400
}

/// <summary>Serializes input within an auto-task and provides explicit, cooperative preemption.</summary>
public sealed class GameInputArbiter
{
    private readonly object _syncRoot = new();
    private InputArbiterLease? _active;
    private bool _poisoned;

    public bool IsPoisoned { get { lock (_syncRoot) return _poisoned; } }
    public bool HasActiveLease { get { lock (_syncRoot) return _active is not null; } }

    public bool TryAcquire(
        string ownerId,
        GameInputPriority priority,
        out InputArbiterLease lease,
        string? parentOwnerId = null,
        Func<CancellationToken, Task<bool>>? preemptAsync = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        lock (_syncRoot)
        {
            if (_poisoned)
            {
                lease = InputArbiterLease.None;
                return false;
            }

            if (_active is null)
            {
                lease = _active = new InputArbiterLease(this, ownerId, priority, preemptAsync);
                return true;
            }

            if (string.Equals(_active.OwnerId, parentOwnerId, StringComparison.Ordinal))
            {
                lease = _active.CreateChild(ownerId, priority);
                return true;
            }

            lease = InputArbiterLease.None;
            return false;
        }
    }

    public async Task<InputArbiterLease> AcquireTemporaryAsync(
        string ownerId,
        GameInputPriority priority,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        InputArbiterLease active;
        lock (_syncRoot)
        {
            if (_poisoned) throw new InvalidOperationException("Input arbiter is poisoned.");
            if (_active is null)
                return _active = new InputArbiterLease(this, ownerId, priority, null);
            active = _active;
            if (priority <= active.Priority)
                throw new InvalidOperationException("Input lease priority is insufficient for preemption.");
        }

        if (active.PreemptAsync is null || !await active.PreemptAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            throw new TimeoutException("The active input lease did not acknowledge preemption.");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                if (_active is null)
                    return _active = new InputArbiterLease(this, ownerId, priority, null);
            }
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The active input lease was not released after preemption.");
    }

    internal void Release(InputArbiterLease lease)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_active, lease)) _active = null;
            else _active?.ReleaseChild(lease);
        }
    }

    internal void MarkPoisoned(InputArbiterLease lease) { lock (_syncRoot) if (Owns(lease)) _poisoned = true; }
    internal void ConfirmInputReleased(InputArbiterLease lease) { lock (_syncRoot) if (Owns(lease)) _poisoned = false; }
    private bool Owns(InputArbiterLease lease) => ReferenceEquals(_active, lease) || _active?.HasChild(lease) == true;
}

/// <summary>Ambient arbiter scope used to attach script input to its owning navigation session.</summary>
internal static class GameInputArbiterContext
{
    private static readonly AsyncLocal<InputArbiterContextState?> CurrentHolder = new();

    public static InputArbiterContextState? Current => CurrentHolder.Value;

    public static IDisposable Push(InputArbiterContextState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = CurrentHolder.Value;
        CurrentHolder.Value = state;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(InputArbiterContextState? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentHolder.Value = previous;
        }
    }
}

internal sealed record InputArbiterContextState(
    GameInputArbiter Arbiter,
    InputArbiterLease NavigationLease);

public sealed class InputArbiterLease : IDisposable
{
    internal static InputArbiterLease None { get; } = new();
    private readonly GameInputArbiter? _arbiter;
    private readonly List<InputArbiterLease> _children = [];
    private bool _disposed;
    private InputArbiterLease() { OwnerId = string.Empty; }
    internal InputArbiterLease(GameInputArbiter arbiter, string ownerId, GameInputPriority priority, Func<CancellationToken, Task<bool>>? preemptAsync)
    { _arbiter = arbiter; OwnerId = ownerId; Priority = priority; PreemptAsync = preemptAsync; }
    public string OwnerId { get; }
    public GameInputPriority Priority { get; }
    internal Func<CancellationToken, Task<bool>>? PreemptAsync { get; }
    internal InputArbiterLease CreateChild(string ownerId, GameInputPriority priority) { var child = new InputArbiterLease(_arbiter!, ownerId, priority, null); _children.Add(child); return child; }
    internal bool HasChild(InputArbiterLease lease) => _children.Contains(lease);
    internal void ReleaseChild(InputArbiterLease lease) => _children.Remove(lease);
    public void MarkPoisoned() { if (!_disposed) _arbiter?.MarkPoisoned(this); }
    public void ConfirmInputReleased() { if (!_disposed) _arbiter?.ConfirmInputReleased(this); }
    public void Dispose() { if (_disposed) return; _disposed = true; _arbiter?.Release(this); }
}
