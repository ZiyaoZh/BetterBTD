using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class SellMonkeyInstructionHandler : ScriptInstructionHandlerBase
{
    internal const int DefaultOperationIntervalMilliseconds = 200;
    internal const int SellDetectionTimeoutMilliseconds = 10 * 60 * 1000;

    public override ScriptCommandType CommandType => ScriptCommandType.SellMonkey;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SellMonkeyTarget",
                "Sell monkey instruction is missing the target monkey binding ID.");
        }

        if (!context.State.TryGetMonkeyState(instruction.TargetMonkeyBindingId, out var monkeyState))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SellMonkeyTarget",
                $"Target monkey binding '{instruction.TargetMonkeyBindingId}' does not exist in runtime state.");
        }

        if (monkeyState.LastKnownCoordinate is null)
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "SellMonkeyCoordinate",
                $"Target monkey '{monkeyState.ObjectId}' does not have a known runtime coordinate.");
        }

        var targetCoordinate = monkeyState.LastKnownCoordinate.Value;
        var sellHotkey = ScriptExecutionKeyBindingResolver.ResolveSellHotkey();
        var panelDetectionEnabled = ScriptInstructionHandlerSupport.ResolveMonkeyPanelDetectionEnabled(
            monkeyState.ObjectId,
            instruction.MonkeyPanelDetectionEnabled ?? true);
        var operationIntervalMilliseconds = ScriptInstructionHandlerSupport.ResolveOperationIntervalMilliseconds(
            context.Options,
            instruction.MonkeyPanelOperationIntervalMilliseconds,
            DefaultOperationIntervalMilliseconds);
        var sellDetectionEnabled = ScriptInstructionHandlerSupport.ResolveSellDetectionEnabled(
            monkeyState.ObjectId,
            instruction.SellDetectionEnabled ?? true);
        var shouldSelectMonkey = ScriptInstructionHandlerSupport.ShouldSelectMonkeyForPanelInteraction(context);

        await ScriptInstructionHandlerSupport
            .PrepareMonkeyPanelInteractionAsync(
                context,
                targetCoordinate,
                shouldSelectMonkey,
                panelDetectionEnabled,
                operationIntervalMilliseconds,
                cancellationToken)
            .ConfigureAwait(false);

        await ScriptInstructionHandlerSupport.ExecuteSellMonkeyAsync(
            context,
            monkeyState.ObjectId,
            sellHotkey,
            sellDetectionEnabled,
            SellDetectionTimeoutMilliseconds,
            operationIntervalMilliseconds,
            cancellationToken).ConfigureAwait(false);

        _ = context.State.RemoveMonkeyState(instruction.TargetMonkeyBindingId);
    }
}
