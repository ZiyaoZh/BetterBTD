using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public sealed class PlaceMonkeyInstructionHandler : ScriptInstructionHandlerBase
{
    internal const int PlacementModeActivationTimeoutMilliseconds = 10 * 60 * 1000;
    internal const int PlacementModeActivationPollIntervalMilliseconds = 100;

    public override ScriptCommandType CommandType => ScriptCommandType.PlaceMonkey;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        var selectionCode = ScriptEditorInstructionService.NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower);
        var requestedCoordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);
        var placementHotkey = ScriptExecutionKeyBindingResolver.ResolvePlacementHotkey(selectionCode);
        var placementDetectionEnabled = instruction.PlacementDetectionEnabled ?? true;
        var placementFailureAdjustmentEnabled = instruction.PlacementFailureAdjustmentEnabled ?? true;
        var placementAttemptIntervalMilliseconds = instruction.PlacementAttemptIntervalMilliseconds ?? 200;
        var placementAdjustmentAttemptIntervalMilliseconds = instruction.PlacementAdjustmentAttemptIntervalMilliseconds ?? 200;

        await ScriptInstructionHandlerSupport.CancelPlacementModeIfActiveAsync(context, cancellationToken).ConfigureAwait(false);

        await ScriptExecutionOperations.RetryAsync(
            context,
            new ScriptRetryOptions
            {
                MaxAttempts = 3,
                DelayBetweenAttemptsMilliseconds = 150,
                Description = $"Place '{selectionCode}'"
            },
            async (attempt, token) =>
            {
                await ScriptInstructionHandlerSupport.CancelPlacementModeIfActiveAsync(context, token).ConfigureAwait(false);
                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "PlaceMonkeyPrepare",
                    $"Placement attempt {attempt}: moving mouse to requested coordinate.",
                    token).ConfigureAwait(false);

                context.RuntimeServices.Input.MoveMouseToScriptCoordinate(requestedCoordinate);

                if (!placementDetectionEnabled)
                {
                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "PlaceMonkeySelect",
                        $"Placement attempt {attempt}: sending hotkey '{placementHotkey.DisplayName}' for '{selectionCode}' without placement detection.",
                        token).ConfigureAwait(false);

                    context.RuntimeServices.Input.PressHotkey(placementHotkey);

                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "PlaceMonkeyClick",
                        $"Trying placement click at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} without placement detection.",
                        token).ConfigureAwait(false);

                    context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(requestedCoordinate, clickCount: 1);
                    MarkPlaced(context, instruction, selectionCode, requestedCoordinate);

                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "PlaceMonkeyPlaced",
                        $"Placed '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} without placement detection.",
                        token).ConfigureAwait(false);

                    return true;
                }

                await ScriptInstructionHandlerSupport.WaitForPlacementModeActiveAsync(
                    context,
                    placementHotkey,
                    selectionCode,
                    attempt,
                    PlacementModeActivationTimeoutMilliseconds,
                    placementAttemptIntervalMilliseconds,
                    token).ConfigureAwait(false);

                var placementCoordinates = placementFailureAdjustmentEnabled
                    ? ScriptInstructionHandlerSupport.BuildPlacementSearchCoordinates(requestedCoordinate)
                    : [requestedCoordinate];

                foreach (var placementCoordinate in placementCoordinates)
                {
                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "PlaceMonkeyClick",
                        $"Trying placement click at {ScriptInstructionHandlerSupport.FormatPoint(placementCoordinate)}.",
                        token).ConfigureAwait(false);

                    context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(placementCoordinate, clickCount: 1);

                    GameStageStateSnapshot? postClickSnapshot = null;
                    try
                    {
                        await ScriptExecutionOperations.WaitUntilAsync(
                            context,
                            new ScriptWaitOptions
                            {
                                TimeoutMilliseconds = 1000,
                                PollIntervalMilliseconds = Math.Min(75, Math.Max(10, placementAdjustmentAttemptIntervalMilliseconds)),
                                Description = "placement mode exit"
                            },
                            async innerToken =>
                            {
                                postClickSnapshot = await context.RuntimeServices.GameStageState
                                    .CaptureSnapshotAsync(innerToken)
                                    .ConfigureAwait(false);
                                return postClickSnapshot?.IsPlacingMonkey == false;
                            },
                            token).ConfigureAwait(false);
                    }
                    catch (ScriptExecutionException ex) when (ScriptInstructionHandlerSupport.IsWaitTimeout(ex))
                    {
                    }

                    if (postClickSnapshot?.IsPlacingMonkey == false)
                    {
                        MarkPlaced(context, instruction, selectionCode, placementCoordinate);

                        await ScriptExecutionOperations.CheckpointAsync(
                            context,
                            "PlaceMonkeyPlaced",
                            $"Placed '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(placementCoordinate)}.",
                            token).ConfigureAwait(false);

                        return true;
                    }
                }

                await ScriptInstructionHandlerSupport.CancelPlacementModeIfActiveAsync(context, token).ConfigureAwait(false);
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "PlaceMonkeyClick",
                    placementFailureAdjustmentEnabled
                        ? $"Failed to place '{selectionCode}' near {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} after offset search."
                        : $"Failed to place '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} without failure adjustment.",
                    attempt);
            },
            static success => success,
            cancellationToken).ConfigureAwait(false);
    }

    private static void MarkPlaced(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        string selectionCode,
        WpfPoint placementCoordinate)
    {
        var monkeyDocument = context.TaskFlow.MonkeyObjectsByBindingId.GetValueOrDefault(instruction.MonkeyBindingId);
        var runtimeState = context.State.UpsertMonkeyState(
            instruction.MonkeyBindingId,
            string.IsNullOrWhiteSpace(instruction.MonkeyObjectId)
                ? monkeyDocument?.ObjectId ?? string.Empty
                : instruction.MonkeyObjectId,
            selectionCode,
            monkeyDocument?.PlacementOrder ?? 0);
        runtimeState.ResetExpectedUpgradeLevels();
        runtimeState.LastKnownCoordinate = placementCoordinate;
    }
}
