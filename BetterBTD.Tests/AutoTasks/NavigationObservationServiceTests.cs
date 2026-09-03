using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class NavigationObservationServiceTests
{
    [Fact]
    public async Task Subscribers_ReceiveTheSameStrictlyIncreasingObservationSequence()
    {
        var source = new IncrementingGameUiStateService();
        var service = CreateService(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        await using var second = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var firstMoves = ReadAsync(first, 3);
        var secondMoves = ReadAsync(second, 3);
        service.Start(timeout.Token);

        var firstObservations = await firstMoves;
        var secondObservations = await secondMoves;
        await service.StopAsync();

        Assert.Equal(firstObservations.Select(static x => x.Sequence), secondObservations.Select(static x => x.Sequence));
        Assert.Equal([1L, 2L, 3L], firstObservations.Select(static x => x.Sequence));
        for (var index = 0; index < firstObservations.Count; index++)
        {
            Assert.Same(firstObservations[index], secondObservations[index]);
        }
    }

    [Fact]
    public async Task CaptureFailure_PublishesDiagnosticSnapshotAndContinues()
    {
        var source = new ThrowOnceGameUiStateService();
        var service = CreateService(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var subscriber = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var observationsTask = ReadAsync(subscriber, 2);
        service.Start(timeout.Token);

        var observations = await observationsTask;
        await service.StopAsync();

        Assert.Equal(GameUiStateId.Unknown, observations[0].Snapshot.State);
        Assert.Equal(true, observations[0].Snapshot.Facts["navigationObservationFailure"]);
        Assert.Equal(GameUiStateId.InLevel, observations[1].Snapshot.State);
        Assert.Equal(1, service.GetDiagnostics().FailureCount);
        Assert.Equal(0, service.GetDiagnostics().ConsecutiveFailureCount);
    }

    [Fact]
    public async Task PublishedSnapshot_DoesNotShareMutableFactsOrStageState()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var facts = new Dictionary<string, object?> { ["value"] = 1 };
        var stageState = new GameStageStateSnapshot { CapturedAt = capturedAt, Gold = 100 };
        var source = new StaticGameUiStateService(new GameUiSnapshot
        {
            CapturedAt = capturedAt,
            State = GameUiStateId.InLevel,
            Facts = facts,
            StageState = stageState
        });
        var service = CreateService(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var subscriber = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var observationTask = ReadAsync(subscriber, 1);
        service.Start(timeout.Token);
        var observation = Assert.Single(await observationTask);
        facts["value"] = 2;
        await service.StopAsync();

        Assert.Equal(1, observation.Snapshot.Facts["value"]);
        Assert.NotSame(facts, observation.Snapshot.Facts);
        Assert.NotSame(stageState, observation.Snapshot.StageState);
        Assert.Equal(100, observation.Snapshot.StageState?.Gold);
    }

    [Fact]
    public async Task Start_IsIdempotentAndStopCompletesSubscribers()
    {
        var source = new IncrementingGameUiStateService();
        var service = CreateService(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var subscriber = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        service.Start(timeout.Token);
        service.Start(timeout.Token);
        Assert.True(await subscriber.MoveNextAsync());
        await service.StopAsync();

        while (await subscriber.MoveNextAsync())
        {
        }

        Assert.False(service.GetDiagnostics().IsRunning);
        Assert.Equal(service.GetDiagnostics().PublishedCount, source.CaptureCount);
    }

    [Fact]
    public async Task LateSubscriber_ImmediatelyReceivesTheLatestPublishedObservation()
    {
        var service = CreateService(new IncrementingGameUiStateService());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        service.Start(timeout.Token);
        Assert.True(await first.MoveNextAsync());
        await service.StopAsync();
        var latest = service.LatestObservation;
        await using var late = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await late.MoveNextAsync());

        Assert.NotNull(latest);
        Assert.Same(latest, late.Current);
        Assert.False(await late.MoveNextAsync());
    }

    [Fact]
    public async Task MissingCaptureTime_IsNormalizedWithoutStoppingTheLoop()
    {
        var service = CreateService(new StaticGameUiStateService(new GameUiSnapshot
        {
            CapturedAt = default,
            State = GameUiStateId.InLevel
        }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var subscriber = service.SubscribeAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        service.Start(timeout.Token);
        Assert.True(await subscriber.MoveNextAsync());
        var observation = subscriber.Current;
        await service.StopAsync();

        Assert.NotEqual(default, observation.CapturedAt);
        Assert.Equal(observation.CapturedAt, observation.Snapshot.CapturedAt);
    }

    private static NavigationObservationService CreateService(IGameUiStateService source)
    {
        return new NavigationObservationService(source, TimeProvider.System, TimeSpan.FromMilliseconds(10));
    }

    private static async Task<IReadOnlyList<NavigationObservation>> ReadAsync(
        IAsyncEnumerator<NavigationObservation> subscriber,
        int count)
    {
        var observations = new List<NavigationObservation>(count);
        while (observations.Count < count && await subscriber.MoveNextAsync())
        {
            observations.Add(subscriber.Current);
        }

        return observations;
    }

    private sealed class IncrementingGameUiStateService : IGameUiStateService
    {
        private int _captureCount;

        public int CaptureCount => Volatile.Read(ref _captureCount);

        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captureCount = Interlocked.Increment(ref _captureCount);
            return Task.FromResult(new GameUiSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                State = captureCount % 2 == 0 ? GameUiStateId.MainMenu : GameUiStateId.InLevel
            });
        }

        public void ResetStabilizationState()
        {
        }
    }

    private sealed class ThrowOnceGameUiStateService : IGameUiStateService
    {
        private int _captureCount;

        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _captureCount) == 1)
            {
                throw new InvalidOperationException("transient recognition failure");
            }

            return Task.FromResult(new GameUiSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                State = GameUiStateId.InLevel
            });
        }

        public void ResetStabilizationState()
        {
        }
    }

    private sealed class StaticGameUiStateService(GameUiSnapshot snapshot) : IGameUiStateService
    {
        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }

        public void ResetStabilizationState()
        {
        }
    }
}
