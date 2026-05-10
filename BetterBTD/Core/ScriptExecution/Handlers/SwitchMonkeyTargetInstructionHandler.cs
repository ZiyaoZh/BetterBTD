using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class SwitchMonkeyTargetInstructionHandler : ScriptInstructionHandlerBase
{
    internal const int DefaultOperationIntervalMilliseconds = 200;

    public override ScriptCommandType CommandType => ScriptCommandType.SwitchMonkeyTarget;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SwitchMonkeyTargetTarget",
                "Switch target instruction is missing the target monkey binding ID.");
        }

        if (!context.State.TryGetMonkeyState(instruction.TargetMonkeyBindingId, out var monkeyState))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SwitchMonkeyTargetTarget",
                $"Target monkey binding '{instruction.TargetMonkeyBindingId}' does not exist in runtime state.");
        }

        if (monkeyState.LastKnownCoordinate is null)
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SwitchMonkeyTargetCoordinate",
                $"Target monkey '{monkeyState.ObjectId}' does not have a known runtime coordinate.");
        }

        if (!Enum.TryParse<SwitchDirectionType>(instruction.SwitchDirection, true, out var switchDirection))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SwitchMonkeyTargetDirection",
                $"Unsupported switch direction '{instruction.SwitchDirection}'.");
        }

        var targetCoordinate = monkeyState.LastKnownCoordinate.Value;
        var switchHotkey = ScriptExecutionKeyBindingResolver.ResolveSwitchTargetHotkey(switchDirection);
        var switchCount = Math.Max(1, instruction.SwitchCount);
        var panelDetectionEnabled = ScriptInstructionHandlerSupport.ResolveMonkeyPanelDetectionEnabled(
            monkeyState.ObjectId,
            instruction.MonkeyPanelDetectionEnabled ?? true);
        var operationIntervalMilliseconds = instruction.MonkeyPanelOperationIntervalMilliseconds ?? DefaultOperationIntervalMilliseconds;

        await ScriptInstructionHandlerSupport
            .PrepareMonkeyPanelInteractionAsync(
                context,
                targetCoordinate,
                panelDetectionEnabled,
                operationIntervalMilliseconds,
                cancellationToken)
            .ConfigureAwait(false);

        await ScriptInstructionHandlerSupport
            .PressHotkeyRepeatedAsync(
                context,
                switchHotkey,
                switchCount,
                "SwitchMonkeyTargetPress",
                $"Switching '{monkeyState.ObjectId}' targeting {switchDirection} {switchCount} time(s).",
                operationIntervalMilliseconds,
                cancellationToken)
            .ConfigureAwait(false);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "SwitchMonkeyTargetSucceeded",
            $"Switched '{monkeyState.ObjectId}' targeting {switchDirection} {switchCount} time(s).",
            cancellationToken).ConfigureAwait(false);

        await ScriptInstructionHandlerSupport
            .CloseUpgradePanelAsync(context, cancellationToken)
            .ConfigureAwait(false);
    }
}
