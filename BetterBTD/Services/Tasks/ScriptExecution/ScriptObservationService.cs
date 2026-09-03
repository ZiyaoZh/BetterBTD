using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.ScriptExecution;

public sealed class ScriptObservationService : IScriptObservationService
{
    private static readonly Lazy<ScriptObservationService> InstanceHolder = new(
        () => new ScriptObservationService(GameStageStateService.Instance, TimeProvider.System));
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);
    private const int MaxAttempts = 3;

    private readonly object _syncRoot = new();
    private readonly IGameStageStateService _inner;
    private readonly TimeProvider _timeProvider;
    private ScriptObservationDiagnostics _diagnostics = new(
        null,
        "Idle",
        0,
        "Script observation has not started.");

    private ScriptObservationService()
        : this(GameStageStateService.Instance, TimeProvider.System)
    {
    }

    internal ScriptObservationService(IGameStageStateService inner, TimeProvider timeProvider)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public static ScriptObservationService Instance => InstanceHolder.Value;

    public bool IsAvailable
    {
        get
        {
            try
            {
                return _inner.IsAvailable;
            }
            catch (Exception ex)
            {
                RecordFailure("Availability", 1, ex);
                return false;
            }
        }
    }

    public ScriptObservationDiagnostics GetDiagnostics()
    {
        lock (_syncRoot)
        {
            return _diagnostics;
        }
    }

    public void ResetDiagnostics()
    {
        lock (_syncRoot)
        {
            _diagnostics = new ScriptObservationDiagnostics(
                null,
                "Idle",
                0,
                "Script observation diagnostics reset.");
        }
    }

    public Task<bool?> GetIsInLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("IsInLevel", _inner.GetIsInLevelAsync, cancellationToken);

    public Task<int?> GetGoldAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("Gold", _inner.GetGoldAsync, cancellationToken);

    public Task<int?> GetRoundAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("Round", _inner.GetRoundAsync, cancellationToken);

    public Task<bool?> GetRightUpgradeVisibleAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("RightUpgradeVisible", _inner.GetRightUpgradeVisibleAsync, cancellationToken);

    public Task<int?> GetRightTopUpgradeLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("RightTopUpgradeLevel", _inner.GetRightTopUpgradeLevelAsync, cancellationToken);

    public Task<int?> GetRightMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("RightMiddleUpgradeLevel", _inner.GetRightMiddleUpgradeLevelAsync, cancellationToken);

    public Task<int?> GetRightBottomUpgradeLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("RightBottomUpgradeLevel", _inner.GetRightBottomUpgradeLevelAsync, cancellationToken);

    public Task<bool?> GetLeftUpgradeVisibleAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("LeftUpgradeVisible", _inner.GetLeftUpgradeVisibleAsync, cancellationToken);

    public Task<int?> GetLeftTopUpgradeLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("LeftTopUpgradeLevel", _inner.GetLeftTopUpgradeLevelAsync, cancellationToken);

    public Task<int?> GetLeftMiddleUpgradeLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("LeftMiddleUpgradeLevel", _inner.GetLeftMiddleUpgradeLevelAsync, cancellationToken);

    public Task<int?> GetLeftBottomUpgradeLevelAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("LeftBottomUpgradeLevel", _inner.GetLeftBottomUpgradeLevelAsync, cancellationToken);

    public Task<bool?> GetIsPlacingMonkeyAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("IsPlacingMonkey", _inner.GetIsPlacingMonkeyAsync, cancellationToken);

    public Task<bool?> GetCanPlaceHeroAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("CanPlaceHero", _inner.GetCanPlaceHeroAsync, cancellationToken);

    public Task<bool> IsCoordinateColorMatchAsync(
        WpfPoint scriptCoordinate,
        int expectedR,
        int expectedG,
        int expectedB,
        int tolerance,
        CancellationToken cancellationToken = default) =>
        ObserveAsync(
            "CoordinateColor",
            token => _inner.IsCoordinateColorMatchAsync(
                scriptCoordinate,
                expectedR,
                expectedG,
                expectedB,
                tolerance,
                token),
            static _ => true,
            false,
            cancellationToken);

    public Task<string> GetStageTargetAsync(CancellationToken cancellationToken = default) =>
        ObserveAsync(
            "StageTarget",
            _inner.GetStageTargetAsync,
            static value => !string.IsNullOrWhiteSpace(value),
            string.Empty,
            cancellationToken);

    public Task<GameStageStateSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
        ObserveNullableAsync("StageSnapshot", _inner.CaptureSnapshotAsync, cancellationToken);

    private Task<T?> ObserveNullableAsync<T>(
        string checkpoint,
        Func<CancellationToken, Task<T?>> operation,
        CancellationToken cancellationToken)
        where T : struct
    {
        return ObserveAsync(
            checkpoint,
            operation,
            static value => value.HasValue,
            default(T?),
            cancellationToken);
    }

    private Task<GameStageStateSnapshot?> ObserveNullableAsync(
        string checkpoint,
        Func<CancellationToken, Task<GameStageStateSnapshot?>> operation,
        CancellationToken cancellationToken)
    {
        return ObserveAsync(
            checkpoint,
            operation,
            static value => value is not null,
            null,
            cancellationToken);
    }

    private async Task<T> ObserveAsync<T>(
        string checkpoint,
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> isUsable,
        T fallback,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var value = await operation(cancellationToken).ConfigureAwait(false);
                if (isUsable(value))
                {
                    RecordSuccess(checkpoint, attempt, value);
                    return value;
                }

                RecordUnavailable(checkpoint, attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                RecordFailure(checkpoint, attempt, ex);
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(RetryInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        var detail = lastException?.GetBaseException().Message ?? "no usable value was recognized";
        ScriptExecutionRuntimeDiagnostics.Warning(
            ScriptExecutionRuntimeLogCategory.Capture,
            $"Script observation '{checkpoint}' is unavailable after {MaxAttempts} attempts; using a safe fallback ({detail}).",
            aggregationKey: $"script-observation:{checkpoint}",
            replaceExisting: true);
        RecordFallback(checkpoint, detail);
        return fallback;
    }

    private void RecordSuccess<T>(string checkpoint, int attempt, T value)
    {
        var capturedAt = value is GameStageStateSnapshot snapshot
            ? snapshot.CapturedAt
            : _timeProvider.GetUtcNow();
        lock (_syncRoot)
        {
            _diagnostics = new ScriptObservationDiagnostics(
                capturedAt,
                checkpoint,
                attempt,
                "Script observation succeeded.");
        }
    }

    private void RecordUnavailable(string checkpoint, int attempt)
    {
        lock (_syncRoot)
        {
            _diagnostics = new ScriptObservationDiagnostics(
                _diagnostics.LastCapturedAt,
                checkpoint,
                attempt,
                "Script observation returned no usable value; retrying.");
        }
    }

    private void RecordFailure(string checkpoint, int attempt, Exception exception)
    {
        lock (_syncRoot)
        {
            _diagnostics = new ScriptObservationDiagnostics(
                _diagnostics.LastCapturedAt,
                checkpoint,
                attempt,
                $"Script observation failed and will retry: {exception.GetBaseException().Message}");
        }
    }

    private void RecordFallback(string checkpoint, string detail)
    {
        lock (_syncRoot)
        {
            _diagnostics = new ScriptObservationDiagnostics(
                _diagnostics.LastCapturedAt,
                checkpoint,
                MaxAttempts,
                $"Script observation is unavailable; using a safe fallback: {detail}");
        }
    }
}
