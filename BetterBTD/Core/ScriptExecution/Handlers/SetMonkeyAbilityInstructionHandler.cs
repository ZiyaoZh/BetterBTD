using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class SetMonkeyAbilityInstructionHandler : ScriptInstructionHandlerBase
{
    internal const int DefaultOperationIntervalMilliseconds = 200;

    public override ScriptCommandType CommandType => ScriptCommandType.SetMonkeyAbility;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SetMonkeyAbilityTarget",
                "Set monkey ability instruction is missing the target monkey binding ID.");
        }

        if (!context.State.TryGetMonkeyState(instruction.TargetMonkeyBindingId, out var monkeyState))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SetMonkeyAbilityTarget",
                $"Target monkey binding '{instruction.TargetMonkeyBindingId}' does not exist in runtime state.");
        }

        if (monkeyState.LastKnownCoordinate is null)
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SetMonkeyAbilityCoordinate",
                $"Target monkey '{monkeyState.ObjectId}' does not have a known runtime coordinate.");
        }

        if (!Enum.TryParse<MonkeyAbilityType>(instruction.SelectedAbility, true, out var abilityType))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SetMonkeyAbilityType",
                $"Unsupported monkey ability '{instruction.SelectedAbility}'.");
        }

        var targetCoordinate = monkeyState.LastKnownCoordinate.Value;
        var abilityHotkey = ScriptExecutionKeyBindingResolver.ResolveMonkeyAbilityHotkey(abilityType);
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

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "SetMonkeyAbilityPress",
            $"Sending monkey ability hotkey '{abilityHotkey.DisplayName}' for '{abilityType}' on '{monkeyState.ObjectId}'.",
            cancellationToken).ConfigureAwait(false);

        context.RuntimeServices.Input.PressHotkey(abilityHotkey);

        if (instruction.RequiresAbilityCoordinate)
        {
            var abilityCoordinate = new WpfPoint(instruction.AbilityCoordinateX, instruction.AbilityCoordinateY);

            await ScriptExecutionOperations.DelayAsync(
                context,
                operationIntervalMilliseconds,
                "SetMonkeyAbilityTargetingDelay",
                cancellationToken).ConfigureAwait(false);

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "SetMonkeyAbilityClick",
                $"Clicking ability coordinate {ScriptInstructionHandlerSupport.FormatPoint(abilityCoordinate)}.",
                cancellationToken).ConfigureAwait(false);

            context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(abilityCoordinate, clickCount: 1);
        }

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "SetMonkeyAbilitySucceeded",
            instruction.RequiresAbilityCoordinate
                ? $"Applied monkey ability '{abilityType}' for '{monkeyState.ObjectId}' with target coordinate."
                : $"Applied monkey ability '{abilityType}' for '{monkeyState.ObjectId}'.",
            cancellationToken).ConfigureAwait(false);

        await ScriptInstructionHandlerSupport
            .CloseUpgradePanelAsync(context, cancellationToken)
            .ConfigureAwait(false);
    }
}
