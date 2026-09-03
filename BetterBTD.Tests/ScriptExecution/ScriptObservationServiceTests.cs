using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Tests.ScriptExecution;

public sealed class ScriptObservationServiceTests
{
    [Fact]
    public async Task CaptureSnapshotAsync_RetriesTransientFailureAndRecovers()
    {
        var source = new FlakyStageStateService();
        var service = new ScriptObservationService(source, TimeProvider.System);

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(2, source.CaptureCallCount);
        Assert.Equal("StageSnapshot", service.GetDiagnostics().Checkpoint);
        Assert.Equal(2, service.GetDiagnostics().Attempt);
    }

    [Fact]
    public async Task CaptureSnapshotAsync_UsesFallbackAfterRepeatedFailure()
    {
        var source = new FlakyStageStateService { AlwaysThrow = true };
        var service = new ScriptObservationService(source, TimeProvider.System);

        var exception = await Record.ExceptionAsync(() => service.CaptureSnapshotAsync());

        Assert.Null(exception);
        Assert.Null(await service.CaptureSnapshotAsync());
        Assert.Equal(6, source.CaptureCallCount);
        Assert.Contains("fallback", service.GetDiagnostics().Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FlakyStageStateService : IGameStageStateService
    {
        public bool AlwaysThrow { get; init; }

        public int CaptureCallCount { get; private set; }

        public bool IsAvailable => true;

        public Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            CaptureCallCount++;
            if (AlwaysThrow || CaptureCallCount == 1)
            {
                throw new InvalidOperationException("transient capture failure");
            }

            return Task.FromResult<GameStageStateSnapshot?>(new GameStageStateSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Gold = 650
            });
        }

        public Task<bool?> GetIsInLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(true);

        public Task<int?> GetGoldAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(650);

        public Task<int?> GetRoundAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(1);

        public Task<bool?> GetRightUpgradeVisibleAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

        public Task<int?> GetRightTopUpgradeLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);

        public Task<int?> GetRightMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);

        public Task<int?> GetRightBottomUpgradeLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);

        public Task<bool?> GetLeftUpgradeVisibleAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

        public Task<int?> GetLeftTopUpgradeLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);

        public Task<int?> GetLeftMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);

        public Task<int?> GetLeftBottomUpgradeLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);

        public Task<bool?> GetIsPlacingMonkeyAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

        public Task<bool?> GetCanPlaceHeroAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

        public Task<bool> IsCoordinateColorMatchAsync(
            WpfPoint scriptCoordinate,
            int expectedR,
            int expectedG,
            int expectedB,
            int tolerance,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<string> GetStageTargetAsync(CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }
}
