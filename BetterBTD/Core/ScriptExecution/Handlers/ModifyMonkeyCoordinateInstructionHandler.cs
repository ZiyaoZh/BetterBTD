using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class ModifyMonkeyCoordinateInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.ModifyMonkeyCoordinate;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "ModifyMonkeyCoordinateTarget",
                "Modify coordinate instruction is missing the target monkey binding ID.",
                messageKey: "Editor.Runtime.Error.MissingCoordinateTarget");
        }

        if (!context.State.TryGetMonkeyState(instruction.TargetMonkeyBindingId, out var monkeyState))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "ModifyMonkeyCoordinateTarget",
                $"Target monkey binding '{instruction.TargetMonkeyBindingId}' does not exist in runtime state.",
                messageKey: "Editor.Runtime.Error.TargetNotFound",
                messageArguments: [instruction.TargetMonkeyBindingId]);
        }

        var updatedCoordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);
        monkeyState.LastKnownCoordinate = updatedCoordinate;

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "ModifyMonkeyCoordinateSucceeded",
            $"Updated runtime coordinate for '{monkeyState.ObjectId}' to {ScriptInstructionHandlerSupport.FormatPoint(updatedCoordinate)}.",
            cancellationToken).ConfigureAwait(false);
    }
}
