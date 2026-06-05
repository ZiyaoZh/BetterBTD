using BetterBTD.Core.Config;
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
        var isHeroPlacement = ScriptEditorInstructionService.IsHeroSelectionCode(selectionCode);
        var requestedCoordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);
        var placementHotkey = ScriptExecutionKeyBindingResolver.ResolvePlacementHotkey(selectionCode);
        var placementDetectionEnabled = instruction.PlacementDetectionEnabled ?? true;
        var placementFailureAdjustmentEnabled = instruction.PlacementFailureAdjustmentEnabled ?? true;
        var placementAttemptIntervalMilliseconds = ScriptInstructionHandlerSupport.ResolveOperationIntervalMilliseconds(
            context.Options,
            instruction.PlacementAttemptIntervalMilliseconds,
            defaultIntervalMilliseconds: 200);
        var placementAdjustmentAttemptIntervalMilliseconds = ScriptInstructionHandlerSupport.ResolveOperationIntervalMilliseconds(
            context.Options,
            instruction.PlacementAdjustmentAttemptIntervalMilliseconds,
            defaultIntervalMilliseconds: 200);

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

                ScriptExecutionOperations.MoveMouseToScriptCoordinate(context, requestedCoordinate, token);

                if (isHeroPlacement)
                {
                    return await PlaceHeroAsync(
                        context,
                        instruction,
                        selectionCode,
                        requestedCoordinate,
                        placementHotkey,
                        attempt,
                        placementAttemptIntervalMilliseconds,
                        placementAdjustmentAttemptIntervalMilliseconds,
                        token).ConfigureAwait(false);
                }

                if (!placementDetectionEnabled)
                {
                    return await PlaceMonkeyWithoutDetectionAsync(
                        context,
                        instruction,
                        selectionCode,
                        requestedCoordinate,
                        placementHotkey,
                        attempt,
                        token).ConfigureAwait(false);
                }

                return await PlaceMonkeyWithDetectionAsync(
                    context,
                    instruction,
                    selectionCode,
                    requestedCoordinate,
                    placementHotkey,
                    placementFailureAdjustmentEnabled,
                    attempt,
                    placementAttemptIntervalMilliseconds,
                    placementAdjustmentAttemptIntervalMilliseconds,
                    token).ConfigureAwait(false);
            },
            static success => success,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> PlaceHeroAsync(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        string selectionCode,
        WpfPoint requestedCoordinate,
        HotkeyBinding placementHotkey,
        int attempt,
        int placementAttemptIntervalMilliseconds,
        int placementAdjustmentAttemptIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeyHeroAim",
            $"Placement attempt {attempt}: aligning hero placement cursor to {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)}.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.MoveMouseToScriptCoordinate(context, requestedCoordinate, cancellationToken);

        await ScriptExecutionOperations.WaitUntilAsync(
            context,
            new ScriptWaitOptions
            {
                TimeoutMilliseconds = PlacementModeActivationTimeoutMilliseconds,
                PollIntervalMilliseconds = placementAttemptIntervalMilliseconds,
                Description = "hero placement available"
            },
            async innerToken =>
            {
                var canPlaceHero = await context.RuntimeServices.GameStageState
                    .GetCanPlaceHeroAsync(innerToken)
                    .ConfigureAwait(false);
                return canPlaceHero == true;
            },
            cancellationToken).ConfigureAwait(false);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeySelect",
            $"Hero placement is available. Sending hotkey '{placementHotkey.DisplayName}' for '{selectionCode}'.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.PressHotkey(context, placementHotkey, cancellationToken);

        await ScriptExecutionOperations.WaitUntilAsync(
            context,
            new ScriptWaitOptions
            {
                TimeoutMilliseconds = PlacementModeActivationTimeoutMilliseconds,
                PollIntervalMilliseconds = placementAttemptIntervalMilliseconds,
                Description = "hero placement mode active"
            },
            async innerToken =>
            {
                var snapshot = await context.RuntimeServices.GameStageState
                    .CaptureSnapshotAsync(innerToken)
                    .ConfigureAwait(false);
                return snapshot?.IsPlacingMonkey == true;
            },
            cancellationToken).ConfigureAwait(false);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeyClick",
            $"Hero placement is available. Clicking {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} after sending '{placementHotkey.DisplayName}'.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.ClickMouseAtScriptCoordinate(context, requestedCoordinate, cancellationToken, clickCount: 1);

        if (await WaitForPlacementModeExitAfterClickAsync(context, placementAdjustmentAttemptIntervalMilliseconds, cancellationToken).ConfigureAwait(false))
        {
            MarkPlaced(context, instruction, selectionCode, requestedCoordinate);

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "PlaceMonkeyPlaced",
                $"Placed hero '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)}.",
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        await ScriptInstructionHandlerSupport.CancelPlacementModeIfActiveAsync(context, cancellationToken).ConfigureAwait(false);
        throw ScriptInstructionHandlerSupport.CreateExecutionException(
            context,
            "PlaceMonkeyClick",
            $"Hero placement at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} did not exit placement mode after the click.",
            attempt);
    }

    private static async Task<bool> PlaceMonkeyWithoutDetectionAsync(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        string selectionCode,
        WpfPoint requestedCoordinate,
        HotkeyBinding placementHotkey,
        int attempt,
        CancellationToken cancellationToken)
    {
        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeySelect",
            $"Placement attempt {attempt}: sending hotkey '{placementHotkey.DisplayName}' for '{selectionCode}' without placement detection.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.PressHotkey(context, placementHotkey, cancellationToken);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeyClick",
            $"Trying placement click at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} without placement detection.",
            cancellationToken).ConfigureAwait(false);

        ScriptExecutionOperations.ClickMouseAtScriptCoordinate(context, requestedCoordinate, cancellationToken, clickCount: 1);
        MarkPlaced(context, instruction, selectionCode, requestedCoordinate);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeyPlaced",
            $"Placed '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} without placement detection.",
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static async Task<bool> PlaceMonkeyWithDetectionAsync(
        ScriptInstructionExecutionContext context,
        ScriptInstructionDocument instruction,
        string selectionCode,
        WpfPoint requestedCoordinate,
        HotkeyBinding placementHotkey,
        bool placementFailureAdjustmentEnabled,
        int attempt,
        int placementAttemptIntervalMilliseconds,
        int placementAdjustmentAttemptIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        await ScriptInstructionHandlerSupport.WaitForPlacementModeActiveAsync(
            context,
            placementHotkey,
            selectionCode,
            attempt,
            PlacementModeActivationTimeoutMilliseconds,
            placementAttemptIntervalMilliseconds,
            cancellationToken).ConfigureAwait(false);

        var placementCoordinates = placementFailureAdjustmentEnabled
            ? ScriptInstructionHandlerSupport.BuildPlacementSearchCoordinates(requestedCoordinate)
            : [requestedCoordinate];

        foreach (var placementCoordinate in placementCoordinates)
        {
            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "PlaceMonkeyClick",
                $"Trying placement click at {ScriptInstructionHandlerSupport.FormatPoint(placementCoordinate)}.",
                cancellationToken).ConfigureAwait(false);

            ScriptExecutionOperations.ClickMouseAtScriptCoordinate(context, placementCoordinate, cancellationToken, clickCount: 1);

            if (await WaitForPlacementModeExitAfterClickAsync(context, placementAdjustmentAttemptIntervalMilliseconds, cancellationToken).ConfigureAwait(false))
            {
                MarkPlaced(context, instruction, selectionCode, placementCoordinate);

                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "PlaceMonkeyPlaced",
                    $"Placed '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(placementCoordinate)}.",
                    cancellationToken).ConfigureAwait(false);

                return true;
            }
        }

        await ScriptInstructionHandlerSupport.CancelPlacementModeIfActiveAsync(context, cancellationToken).ConfigureAwait(false);
        throw ScriptInstructionHandlerSupport.CreateExecutionException(
            context,
            "PlaceMonkeyClick",
            placementFailureAdjustmentEnabled
                ? $"Failed to place '{selectionCode}' near {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} after offset search."
                : $"Failed to place '{selectionCode}' at {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} without failure adjustment.",
            attempt);
    }

    private static async Task<bool> WaitForPlacementModeExitAfterClickAsync(
        ScriptInstructionExecutionContext context,
        int placementAdjustmentAttemptIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
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
                cancellationToken).ConfigureAwait(false);
        }
        catch (ScriptExecutionException ex) when (ScriptInstructionHandlerSupport.IsWaitTimeout(ex))
        {
        }

        return postClickSnapshot?.IsPlacingMonkey == false;
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
