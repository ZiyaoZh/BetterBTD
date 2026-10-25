using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class PlaceHeroInventoryInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.PlaceHeroInventory;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (!Enum.TryParse<InventoryType>(instruction.SelectedInventoryItem, true, out var inventoryType))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "PlaceHeroInventoryType",
                $"Unsupported hero inventory '{instruction.SelectedInventoryItem}'.",
                messageKey: "Editor.Runtime.Error.UnsupportedHeroInventory",
                messageArguments: [instruction.SelectedInventoryItem]);
        }

        var heroHotkey = ScriptExecutionKeyBindingResolver.ResolveHeroHotkey();
        var inventoryHotkey = ScriptExecutionKeyBindingResolver.ResolveHeroInventoryHotkey(inventoryType);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceHeroInventoryHero",
            $"Opening hero panel with hotkey '{heroHotkey.DisplayName}'.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.PressHotkey(context, heroHotkey, cancellationToken);

        await ScriptExecutionOperations.DelayAsync(
            context,
            60,
            "PlaceHeroInventoryHeroDelay",
            cancellationToken).ConfigureAwait(false);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceHeroInventorySelect",
            $"Selecting hero inventory '{inventoryType}' with hotkey '{inventoryHotkey.DisplayName}'.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.PressHotkey(context, inventoryHotkey, cancellationToken);

        if (instruction.RequiresAbilityCoordinate)
        {
            var targetCoordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);

            await ScriptExecutionOperations.DelayAsync(
                context,
                60,
                "PlaceHeroInventoryTargetingDelay",
                cancellationToken).ConfigureAwait(false);

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "PlaceHeroInventoryClick",
                $"Clicking inventory target coordinate {ScriptInstructionHandlerSupport.FormatPoint(targetCoordinate)}.",
                cancellationToken).ConfigureAwait(false);

            ScriptExecutionOperations.ClickMouseAtScriptCoordinate(context, targetCoordinate, cancellationToken, clickCount: 1);
        }

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceHeroInventorySucceeded",
            instruction.RequiresAbilityCoordinate
                ? $"Used hero inventory '{inventoryType}' with target coordinate."
                : $"Used hero inventory '{inventoryType}'.",
            cancellationToken).ConfigureAwait(false);
    }
}
