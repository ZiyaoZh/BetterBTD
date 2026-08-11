using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Models.AutoTasks;

public enum LoopStageScriptRunState
{
    NotStarted,
    RunningBeforeBoundary,
    WaitingForFreeplayPrompt,
    RunningAfterBoundary,
    WaitingForBlockingUi,
    WaitingForTargetRound,
    WaitingForExit,
    FinishedCurrentStage
}

public sealed class LoopStageAutoTaskScriptContext
{
    public required BlackBorderMapCategory Category { get; init; }

    public required StageEntryTarget Target { get; init; }

    public required HeroType Hero { get; init; }

    public required string FilePath { get; init; }

    public required int FreeplayBoundaryIndex { get; init; }
}

public static class LoopStageAutoTaskStateKeys
{
    public const string ResolvedScriptContext = "LoopStage.ResolvedScriptContext";
    public const string HeroSelected = "LoopStage.HeroSelected";
    public const string MapLocateAttempts = "LoopStage.MapLocateAttempts";
    public const string ScriptRunState = "LoopStage.ScriptRunState";
    public const string ResumeStepIndex = "LoopStage.ResumeStepIndex";
    public const string InterruptedUiState = "LoopStage.InterruptedUiState";
    public const string RoundProgressTracker = "LoopStage.RoundProgressTracker";
    public const string TargetRoundReached = "LoopStage.TargetRoundReached";
    public const string FreeplayPromptConfirmed = "LoopStage.FreeplayPromptConfirmed";
}

public sealed class LoopStageRoundProgressTracker
{
    private const int RequiredConsecutiveObservations = 2;
    private const int AllowedRoundOvershoot = 10;

    private readonly int _targetRound;
    private int _highestObservedRound;
    private int _targetObservationCount;

    public LoopStageRoundProgressTracker(int targetRound)
    {
        if (targetRound < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetRound), targetRound, "The target round must be positive.");
        }

        _targetRound = targetRound;
    }

    public int HighestObservedRound => _highestObservedRound;

    public bool Observe(int? round)
    {
        if (!round.HasValue || round.Value < 1 || round.Value < _highestObservedRound)
        {
            _targetObservationCount = 0;
            return false;
        }

        if (round.Value > _targetRound + AllowedRoundOvershoot)
        {
            _targetObservationCount = 0;
            return false;
        }

        _highestObservedRound = Math.Max(_highestObservedRound, round.Value);
        if (_highestObservedRound < _targetRound)
        {
            _targetObservationCount = 0;
            return false;
        }

        _targetObservationCount++;
        return _targetObservationCount >= RequiredConsecutiveObservations;
    }
}
