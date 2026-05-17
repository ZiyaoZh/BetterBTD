using BetterBTD.Core.Config;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class UpgradeMonkeyInstructionHandler : ScriptInstructionHandlerBase
{
    internal const int UpgradePanelDetectionTimeoutMilliseconds = 10 * 60 * 1000;
    internal const int DefaultUpgradeOperationIntervalMilliseconds = 200;

    public override ScriptCommandType CommandType => ScriptCommandType.UpgradeMonkey;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        if (string.IsNullOrWhiteSpace(instruction.TargetMonkeyBindingId))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyTarget",
                "Upgrade instruction is missing the target monkey binding ID.");
        }

        if (!context.State.TryGetMonkeyState(instruction.TargetMonkeyBindingId, out var monkeyState))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyTarget",
                $"Target monkey binding '{instruction.TargetMonkeyBindingId}' does not exist in runtime state.");
        }

        var isHeroTarget = ScriptInstructionHandlerSupport.IsHeroObjectKey(monkeyState.ObjectId) ||
                           ScriptInstructionHandlerSupport.IsHeroObjectKey(instruction.TargetMonkeyObjectId);
        var upgradePath = ResolveUpgradePath(context, instruction, isHeroTarget);
        var upgradeHotkey = ScriptExecutionKeyBindingResolver.ResolveUpgradeHotkey(upgradePath);
        var upgradeCount = Math.Max(1, instruction.UpgradeCount);
        var targetLevel = monkeyState.GetExpectedUpgradeLevel(upgradePath) + upgradeCount;
        var upgradeDetectionEnabled = instruction.UpgradeDetectionEnabled ?? true;
        var upgradeOperationIntervalMilliseconds = ScriptInstructionHandlerSupport.ResolveOperationIntervalMilliseconds(
            context.Options,
            instruction.UpgradeOperationIntervalMilliseconds,
            DefaultUpgradeOperationIntervalMilliseconds);
        var shouldSelectMonkey = ScriptInstructionHandlerSupport.ShouldSelectMonkeyForPanelInteraction(context);
        var shouldCloseMonkeyPanel = ScriptInstructionHandlerSupport.ShouldCloseMonkeyPanelAfterInstruction(context);

        if (isHeroTarget)
        {
            if (shouldSelectMonkey)
            {
                var heroHotkey = ScriptExecutionKeyBindingResolver.ResolveHeroHotkey();

                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "UpgradeMonkeyHeroSelect",
                    $"Selecting hero '{monkeyState.ObjectId}' with hotkey '{heroHotkey.DisplayName}'.",
                    cancellationToken).ConfigureAwait(false);

                context.RuntimeServices.Input.PressHotkey(heroHotkey);
            }

            await ScriptInstructionHandlerSupport.PressHotkeyRepeatedAsync(
                context,
                upgradeHotkey,
                upgradeCount,
                "UpgradeMonkeyHeroPress",
                $"Upgrading hero '{monkeyState.ObjectId}' without runtime verification.",
                upgradeOperationIntervalMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (targetLevel > 5)
            {
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "UpgradeMonkeyLevelCap",
                    $"Cannot upgrade '{instruction.UpgradePath}' to target level {targetLevel} because level 5 is the maximum.");
            }

            if (monkeyState.LastKnownCoordinate is null)
            {
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "UpgradeMonkeyCoordinate",
                    $"Target monkey '{monkeyState.ObjectId}' does not have a known runtime coordinate.");
            }

            await UpgradeTowerAsync(
                context,
                instruction,
                monkeyState,
                monkeyState.LastKnownCoordinate.Value,
                upgradePath,
                upgradeHotkey,
                upgradeCount,
                targetLevel,
                shouldSelectMonkey,
                upgradeDetectionEnabled,
                upgradeOperationIntervalMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }

        if (shouldCloseMonkeyPanel)
        {
            await ScriptInstructionHandlerSupport
                .CloseUpgradePanelAsync(context, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static UpgradePathType ResolveUpgradePath(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        bool isHeroTarget)
    {
        if (Enum.TryParse<UpgradePathType>(instruction.UpgradePath, true, out var upgradePath))
        {
            return upgradePath;
        }

        if (isHeroTarget && string.IsNullOrWhiteSpace(instruction.UpgradePath))
        {
            return UpgradePathType.Top;
        }

        throw ScriptInstructionHandlerSupport.CreateExecutionException(
            context,
            "UpgradeMonkeyPath",
            $"Unsupported upgrade path '{instruction.UpgradePath}'.");
    }

    private static async Task UpgradeTowerAsync(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        ScriptMonkeyRuntimeState monkeyState,
        WpfPoint targetCoordinate,
        UpgradePathType upgradePath,
        HotkeyBinding upgradeHotkey,
        int upgradeCount,
        int targetLevel,
        bool shouldSelectMonkey,
        bool upgradeDetectionEnabled,
        int upgradeOperationIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        if (!upgradeDetectionEnabled)
        {
            if (shouldSelectMonkey)
            {
                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "UpgradeMonkeySelect",
                    $"Selecting '{monkeyState.ObjectId}' at {ScriptInstructionHandlerSupport.FormatPoint(targetCoordinate)} without upgrade detection.",
                    cancellationToken).ConfigureAwait(false);

                context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(targetCoordinate, clickCount: 1);

                await ScriptExecutionOperations.DelayAsync(
                    context,
                    upgradeOperationIntervalMilliseconds,
                    "UpgradeMonkeySelectDelay",
                    cancellationToken).ConfigureAwait(false);
            }

            await ScriptInstructionHandlerSupport.PressHotkeyRepeatedAsync(
                context,
                upgradeHotkey,
                upgradeCount,
                "UpgradeMonkeyPress",
                $"Upgrading '{monkeyState.ObjectId}' without runtime verification.",
                upgradeOperationIntervalMilliseconds,
                cancellationToken).ConfigureAwait(false);

            monkeyState.SetExpectedUpgradeLevel(upgradePath, targetLevel);
            return;
        }

        var panelSnapshot = await ScriptInstructionHandlerSupport.PrepareMonkeyPanelInteractionAsync(
            context,
            targetCoordinate,
            shouldSelectMonkey,
            true,
            upgradeOperationIntervalMilliseconds,
            cancellationToken).ConfigureAwait(false)
            ?? throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyPanel",
                "Failed to detect the upgrade panel for the selected monkey.");

        var currentLevel = GetRequiredUpgradeLevel(context, instruction, panelSnapshot, upgradePath);
        if (currentLevel >= targetLevel)
        {
            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "UpgradeMonkeySatisfied",
                currentLevel == targetLevel
                    ? $"'{instruction.UpgradePath}' is already at target level {targetLevel} for '{monkeyState.ObjectId}'."
                    : $"'{instruction.UpgradePath}' is already above target level {targetLevel} for '{monkeyState.ObjectId}' (current level {currentLevel}).",
                cancellationToken).ConfigureAwait(false);
            monkeyState.SetExpectedUpgradeLevel(upgradePath, targetLevel);
            return;
        }

        while (currentLevel < targetLevel)
        {
            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "UpgradeMonkeyPress",
                $"Upgrading '{monkeyState.ObjectId}' {instruction.UpgradePath}: current level {currentLevel}, target level {targetLevel}. Sending '{upgradeHotkey.DisplayName}'.",
                cancellationToken).ConfigureAwait(false);

            context.RuntimeServices.Input.PressHotkey(upgradeHotkey);

            await ScriptExecutionOperations.DelayAsync(
                context,
                upgradeOperationIntervalMilliseconds,
                "UpgradeMonkeyPressInterval",
                cancellationToken).ConfigureAwait(false);

            panelSnapshot = await context.RuntimeServices.GameStageState
                .CaptureSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(panelSnapshot) is null)
            {
                panelSnapshot = await ScriptInstructionHandlerSupport.WaitForUpgradePanelVisibleAsync(
                    context,
                    targetCoordinate,
                    UpgradePanelDetectionTimeoutMilliseconds,
                    upgradeOperationIntervalMilliseconds,
                    cancellationToken).ConfigureAwait(false);
            }

            currentLevel = GetRequiredUpgradeLevel(context, instruction, panelSnapshot, upgradePath);
        }

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "UpgradeMonkeySucceeded",
            $"'{instruction.UpgradePath}' reached level {targetLevel} for '{monkeyState.ObjectId}'.",
            cancellationToken).ConfigureAwait(false);
        monkeyState.SetExpectedUpgradeLevel(upgradePath, targetLevel);
    }

    private static int GetRequiredUpgradeLevel(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        GameStageStateSnapshot? panelSnapshot,
        UpgradePathType upgradePath)
    {
        var panelSide = ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(panelSnapshot);
        if (!panelSide.HasValue || panelSnapshot is null)
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyPanel",
                "Failed to detect the upgrade panel for the selected monkey.");
        }

        var level = ScriptInstructionHandlerSupport.GetUpgradeLevel(
            panelSnapshot,
            panelSide.Value,
            upgradePath);
        if (!level.HasValue)
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyPanel",
                $"Failed to read the current '{instruction.UpgradePath}' path level.");
        }

        return level.Value;
    }
}
