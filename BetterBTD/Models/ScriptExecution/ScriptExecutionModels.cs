using System.Windows;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Models.ScriptEditor;

namespace BetterBTD.Models.ScriptExecution;

public enum ScriptExecutionStatus
{
    Completed,
    Cancelled,
    Failed
}

public sealed class ScriptExecutionOptions
{
    public int? OverrideInstructionIntervalMs { get; init; }

    public bool RequireCaptureService { get; init; } = true;

    public bool RequireTargetWindow { get; init; } = true;

    public ScriptExecutionRuntimeServices? RuntimeServices { get; init; }
}

public sealed class ScriptTaskFlow
{
    public string SourceFilePath { get; init; } = string.Empty;

    public required ScriptDocument Document { get; init; }

    public required IReadOnlyList<ScriptTaskFlowStep> Steps { get; init; }

    public required IReadOnlyDictionary<string, ScriptMonkeyObjectDocument> MonkeyObjectsByBindingId { get; init; }
}

public sealed class ScriptTaskFlowStep
{
    public required int Index { get; init; }

    public required ScriptCommandType CommandType { get; init; }

    public required ScriptInstructionDocument Instruction { get; init; }
}

public sealed class ScriptMonkeyRuntimeState
{
    public required string BindingId { get; init; }

    public string ObjectId { get; set; } = string.Empty;

    public string SelectionCode { get; set; } = string.Empty;

    public int PlacementOrder { get; set; }

    public Point? LastKnownCoordinate { get; set; }
}

public sealed class ScriptExecutionState
{
    private readonly Dictionary<string, ScriptMonkeyRuntimeState> _monkeyStates = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, ScriptMonkeyRuntimeState> MonkeyStates => _monkeyStates;

    public void SeedMonkeyStates(IEnumerable<ScriptMonkeyObjectDocument> monkeyObjects)
    {
        ArgumentNullException.ThrowIfNull(monkeyObjects);

        _monkeyStates.Clear();
        foreach (var monkeyObject in monkeyObjects)
        {
            if (string.IsNullOrWhiteSpace(monkeyObject.BindingId))
            {
                continue;
            }

            _monkeyStates[monkeyObject.BindingId] = new ScriptMonkeyRuntimeState
            {
                BindingId = monkeyObject.BindingId,
                ObjectId = monkeyObject.ObjectId,
                SelectionCode = monkeyObject.SelectionCode,
                PlacementOrder = monkeyObject.PlacementOrder
            };
        }
    }

    public bool TryGetMonkeyState(string bindingId, out ScriptMonkeyRuntimeState monkeyState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        return _monkeyStates.TryGetValue(bindingId, out monkeyState!);
    }
}

public sealed class ScriptInstructionExecutionContext
{
    public required ScriptTaskFlow TaskFlow { get; init; }

    public required ScriptTaskFlowStep Step { get; init; }

    public required ScriptExecutionState State { get; init; }

    public required ScriptExecutionOptions Options { get; init; }

    public required ScriptExecutionRuntimeServices RuntimeServices { get; init; }
}

public sealed class GameStageUpgradePanelState
{
    public static GameStageUpgradePanelState Empty { get; } = new();

    public bool? IsVisible { get; init; }

    public int? TopPathLevel { get; init; }

    public int? MiddlePathLevel { get; init; }

    public int? BottomPathLevel { get; init; }
}

public sealed class GameStageStateSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool? IsInLevel { get; init; }

    public int? Gold { get; init; }

    public int? Round { get; init; }

    public GameStageUpgradePanelState RightUpgradePanel { get; init; } = GameStageUpgradePanelState.Empty;

    public GameStageUpgradePanelState LeftUpgradePanel { get; init; } = GameStageUpgradePanelState.Empty;

    public bool? IsPlacingMonkey { get; init; }

    public bool? CanPlaceHero { get; init; }

    public string StageTarget { get; init; } = string.Empty;
}

public sealed class ScriptExecutionResult
{
    public required ScriptExecutionStatus Status { get; init; }

    public required int ExecutedStepCount { get; init; }

    public required int LastCompletedStepIndex { get; init; }

    public Exception? Exception { get; init; }
}
