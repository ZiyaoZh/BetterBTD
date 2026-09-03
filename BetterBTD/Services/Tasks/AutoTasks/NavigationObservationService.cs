using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class NavigationObservationService : INavigationObservationService
{
    private static readonly Lazy<NavigationObservationService> InstanceHolder = new(
        () => new NavigationObservationService());
    private static readonly TimeSpan DefaultObservationInterval = TimeSpan.FromMilliseconds(100);

    private readonly object _syncRoot = new();
    private readonly IGameUiStateService _gameUiStateService;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _observationInterval;
    private readonly Dictionary<long, Channel<NavigationObservation>> _subscribers = [];

    private CancellationTokenSource? _loopCancellationSource;
    private Task? _loopTask;
    private NavigationObservation? _latestObservation;
    private NavigationObservationDiagnostics _diagnostics = new();
    private long _nextObservationSequence;
    private long _nextSubscriberId;

    private NavigationObservationService()
        : this(GameUiStateService.Instance, TimeProvider.System, DefaultObservationInterval)
    {
    }

    internal NavigationObservationService(
        IGameUiStateService gameUiStateService,
        TimeProvider timeProvider,
        TimeSpan observationInterval)
    {
        _gameUiStateService = gameUiStateService ?? throw new ArgumentNullException(nameof(gameUiStateService));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (observationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observationInterval),
                observationInterval,
                "The navigation observation interval must be positive.");
        }

        _observationInterval = observationInterval;
    }

    public static NavigationObservationService Instance => InstanceHolder.Value;

    public NavigationObservation? LatestObservation
    {
        get
        {
            lock (_syncRoot)
            {
                return _latestObservation;
            }
        }
    }

    public NavigationObservationDiagnostics GetDiagnostics()
    {
        lock (_syncRoot)
        {
            return _diagnostics;
        }
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _loopCancellationSource?.Dispose();
            _loopCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loopCancellationToken = _loopCancellationSource.Token;
            _diagnostics = _diagnostics with
            {
                IsRunning = true,
                ConsecutiveFailureCount = 0,
                LastMessage = "Navigation observation loop started."
            };
            _loopTask = Task.Run(
                () => RunObservationLoopAsync(loopCancellationToken),
                CancellationToken.None);
        }
    }

    public async IAsyncEnumerable<NavigationObservation> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<NavigationObservation>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        long subscriberId;

        lock (_syncRoot)
        {
            subscriberId = ++_nextSubscriberId;
            _subscribers.Add(subscriberId, channel);
            if (_latestObservation is not null)
            {
                channel.Writer.TryWrite(_latestObservation);
            }

        }

        try
        {
            await foreach (var observation in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return observation;
            }
        }
        finally
        {
            lock (_syncRoot)
            {
                _subscribers.Remove(subscriberId);
            }

            channel.Writer.TryComplete();
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellationSource;
        Task? loopTask;
        lock (_syncRoot)
        {
            cancellationSource = _loopCancellationSource;
            loopTask = _loopTask;
        }

        cancellationSource?.Cancel();
        if (loopTask is not null)
        {
            await loopTask.ConfigureAwait(false);
        }
    }

    private async Task RunObservationLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            TryResetStabilizationState();
            while (!cancellationToken.IsCancellationRequested)
            {
                await CaptureAndPublishAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_observationInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            CompleteLoop();
        }
    }

    private void TryResetStabilizationState()
    {
        try
        {
            _gameUiStateService.ResetStabilizationState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Navigation observation stabilization reset failed: {ex}");
            RecordFailure($"Stabilization reset failed: {ex.GetBaseException().Message}");
        }
    }

    private async Task CaptureAndPublishAsync(CancellationToken cancellationToken)
    {
        GameUiSnapshot snapshot;
        try
        {
            snapshot = await _gameUiStateService
                .CaptureSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                throw new InvalidOperationException("The UI state service returned no snapshot.");
            }

            snapshot = FreezeSnapshot(snapshot, _timeProvider.GetUtcNow());
            RecordSuccess(snapshot.Summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Navigation observation capture failed and will be retried: {ex}");
            var message = $"Navigation observation failed and will retry: {ex.GetBaseException().Message}";
            RecordFailure(message);
            snapshot = FreezeSnapshot(new GameUiSnapshot
            {
                CapturedAt = _timeProvider.GetUtcNow(),
                State = GameUiStateId.Unknown,
                Confidence = 0d,
                Facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["navigationObservationFailure"] = true,
                    ["exceptionType"] = ex.GetType().Name
                },
                Summary = message
            }, _timeProvider.GetUtcNow());
        }

        Publish(snapshot);
    }

    private void Publish(GameUiSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            var sequence = ++_nextObservationSequence;
            var observation = new NavigationObservation(sequence, snapshot.CapturedAt, snapshot);
            _latestObservation = observation;
            _diagnostics = _diagnostics with
            {
                PublishedCount = _diagnostics.PublishedCount + 1,
                LastPublishedAt = snapshot.CapturedAt
            };

            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryWrite(observation);
            }
        }
    }

    private void RecordSuccess(string message)
    {
        lock (_syncRoot)
        {
            _diagnostics = _diagnostics with
            {
                ConsecutiveFailureCount = 0,
                LastMessage = string.IsNullOrWhiteSpace(message)
                    ? "Navigation observation captured."
                    : message
            };
        }
    }

    private void RecordFailure(string message)
    {
        lock (_syncRoot)
        {
            _diagnostics = _diagnostics with
            {
                FailureCount = _diagnostics.FailureCount + 1,
                ConsecutiveFailureCount = _diagnostics.ConsecutiveFailureCount + 1,
                LastMessage = message
            };
        }
    }

    private void CompleteLoop()
    {
        lock (_syncRoot)
        {
            _diagnostics = _diagnostics with
            {
                IsRunning = false,
                LastMessage = "Navigation observation loop stopped."
            };
        }
    }

    private static GameUiSnapshot FreezeSnapshot(
        GameUiSnapshot snapshot,
        DateTimeOffset fallbackCapturedAt)
    {
        return new GameUiSnapshot
        {
            CapturedAt = snapshot.CapturedAt == default ? fallbackCapturedAt : snapshot.CapturedAt,
            State = snapshot.State,
            Confidence = snapshot.Confidence,
            VisualFingerprint = snapshot.VisualFingerprint,
            StageState = CloneStageState(snapshot.StageState),
            Facts = snapshot.Facts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            Summary = snapshot.Summary
        };
    }

    private static GameStageStateSnapshot? CloneStageState(GameStageStateSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new GameStageStateSnapshot
        {
            CapturedAt = snapshot.CapturedAt,
            IsInLevel = snapshot.IsInLevel,
            Gold = snapshot.Gold,
            Round = snapshot.Round,
            RightUpgradePanel = CloneUpgradePanel(snapshot.RightUpgradePanel),
            LeftUpgradePanel = CloneUpgradePanel(snapshot.LeftUpgradePanel),
            IsPlacingMonkey = snapshot.IsPlacingMonkey,
            CanPlaceHero = snapshot.CanPlaceHero,
            StageTarget = snapshot.StageTarget
        };
    }

    private static GameStageUpgradePanelState CloneUpgradePanel(GameStageUpgradePanelState panel)
    {
        return new GameStageUpgradePanelState
        {
            IsVisible = panel.IsVisible,
            TopPathLevel = panel.TopPathLevel,
            MiddlePathLevel = panel.MiddlePathLevel,
            BottomPathLevel = panel.BottomPathLevel
        };
    }
}
