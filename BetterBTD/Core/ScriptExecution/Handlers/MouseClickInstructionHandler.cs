using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class MouseClickInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.MouseClick;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        var coordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);
        var clickCount = Math.Max(1, instruction.ClickCount);
        var clickIntervalMilliseconds = ScriptInstructionHandlerSupport.ResolveOperationIntervalMilliseconds(
            context.Options,
            instruction.ClickIntervalMilliseconds);

        for (var index = 0; index < clickCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "MouseClick",
                $"Executing click {index + 1}/{clickCount}.",
                cancellationToken).ConfigureAwait(false);
            context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(coordinate, clickCount: 1);

            if (index < clickCount - 1 && clickIntervalMilliseconds > 0)
            {
                await ScriptExecutionOperations.DelayAsync(
                    context,
                    clickIntervalMilliseconds,
                    "MouseClickInterval",
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
