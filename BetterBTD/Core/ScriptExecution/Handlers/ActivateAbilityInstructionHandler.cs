using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class ActivateAbilityInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.ActivateAbility;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (!Enum.TryParse<ActivatedAbilityType>(instruction.SelectedActivatedAbility, true, out var abilityType))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "ActivateAbilityType",
                $"Unsupported activated ability '{instruction.SelectedActivatedAbility}'.",
                messageKey: "Editor.Runtime.Error.UnsupportedActivatedAbility",
                messageArguments: [instruction.SelectedActivatedAbility]);
        }

        var abilityHotkey = ScriptExecutionKeyBindingResolver.ResolveActivatedAbilityHotkey(abilityType);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "ActivateAbilityPress",
            $"Activating global ability '{abilityType}' with hotkey '{abilityHotkey.DisplayName}'.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.PressHotkey(context, abilityHotkey, cancellationToken);

        if (instruction.RequiresAbilityCoordinate)
        {
            var targetCoordinate = new WpfPoint(instruction.AbilityCoordinateX, instruction.AbilityCoordinateY);

            await ScriptExecutionOperations.DelayAsync(
                context,
                60,
                "ActivateAbilityTargetingDelay",
                cancellationToken).ConfigureAwait(false);

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "ActivateAbilityClick",
                $"Clicking activated ability target coordinate {ScriptInstructionHandlerSupport.FormatPoint(targetCoordinate)}.",
                cancellationToken).ConfigureAwait(false);

            ScriptExecutionOperations.ClickMouseAtScriptCoordinate(context, targetCoordinate, cancellationToken, clickCount: 1);
        }

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "ActivateAbilitySucceeded",
            instruction.RequiresAbilityCoordinate
                ? $"Activated global ability '{abilityType}' with target coordinate."
                : $"Activated global ability '{abilityType}'.",
            cancellationToken).ConfigureAwait(false);
    }
}
