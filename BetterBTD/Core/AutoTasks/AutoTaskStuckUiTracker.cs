using System.Numerics;
using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Core.AutoTasks;

internal sealed class AutoTaskStuckUiTracker
{
    private readonly TimeSpan _timeout;
    private readonly int _visualFingerprintDistanceTolerance;
    private Observation? _baseline;

    public AutoTaskStuckUiTracker(TimeSpan timeout, int visualFingerprintDistanceTolerance = 6)
    {
        _timeout = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        _visualFingerprintDistanceTolerance = Math.Clamp(visualFingerprintDistanceTolerance, 0, 64);
    }

    public bool Observe(
        GameUiSnapshot snapshot,
        AutoTaskPhase phase,
        int completedStageCount)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.State == GameUiStateId.InLevel)
        {
            Reset();
            return false;
        }

        var current = new Observation(
            snapshot.State,
            phase,
            completedStageCount,
            snapshot.VisualFingerprint,
            snapshot.CapturedAt);

        if (_baseline is null || !IsSameInterface(_baseline.Value, current) ||
            current.CapturedAt < _baseline.Value.CapturedAt)
        {
            _baseline = current;
            return false;
        }

        return current.CapturedAt - _baseline.Value.CapturedAt >= _timeout;
    }

    public void Reset()
    {
        _baseline = null;
    }

    private bool IsSameInterface(Observation baseline, Observation current)
    {
        return baseline.State == current.State &&
               baseline.Phase == current.Phase &&
               baseline.CompletedStageCount == current.CompletedStageCount &&
               AreVisualFingerprintsEquivalent(
                   baseline.VisualFingerprint,
                   current.VisualFingerprint,
                   _visualFingerprintDistanceTolerance);
    }

    private static bool AreVisualFingerprintsEquivalent(ulong? baseline, ulong? current, int tolerance)
    {
        return !baseline.HasValue || !current.HasValue ||
               BitOperations.PopCount(baseline.Value ^ current.Value) <= tolerance;
    }

    private readonly record struct Observation(
        GameUiStateId State,
        AutoTaskPhase Phase,
        int CompletedStageCount,
        ulong? VisualFingerprint,
        DateTimeOffset CapturedAt);
}
