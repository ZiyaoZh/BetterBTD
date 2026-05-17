using BetterBTD.Core.Config;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using System.Windows.Input;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

internal static class ScriptInstructionHandlerSupport
{
    private const int CommonOperationIntervalMinimumMilliseconds = 50;
    private const int CommonOperationIntervalMaximumMilliseconds = 1000;
    private const int MonkeyPanelSelectionAttemptTimeoutMilliseconds = 5000;
    private const int RepeatedHotkeyIntervalMilliseconds = 60;
    private const int PlacementSearchRingCount = 20;
    private const double PlacementSearchRingStepPixels = 1d;

    private static readonly (double OffsetX, double OffsetY)[] SelectionOffsets =
    [
        (0d, 0d),
        (3d, 0d),
        (-3d, 0d),
        (0d, 3d),
        (0d, -3d),
        (6d, 0d),
        (-6d, 0d),
        (0d, 6d),
        (0d, -6d)
    ];

    public static IEnumerable<WpfPoint> BuildPlacementSearchCoordinates(WpfPoint requestedCoordinate)
    {
        yield return requestedCoordinate;

        for (var ring = 1; ring <= PlacementSearchRingCount; ring++)
        {
            var offset = ring * PlacementSearchRingStepPixels;
            yield return new WpfPoint(requestedCoordinate.X + offset, requestedCoordinate.Y);
            yield return new WpfPoint(requestedCoordinate.X + offset, requestedCoordinate.Y + offset);
            yield return new WpfPoint(requestedCoordinate.X, requestedCoordinate.Y + offset);
            yield return new WpfPoint(requestedCoordinate.X - offset, requestedCoordinate.Y + offset);
            yield return new WpfPoint(requestedCoordinate.X - offset, requestedCoordinate.Y);
            yield return new WpfPoint(requestedCoordinate.X - offset, requestedCoordinate.Y - offset);
            yield return new WpfPoint(requestedCoordinate.X, requestedCoordinate.Y - offset);
            yield return new WpfPoint(requestedCoordinate.X + offset, requestedCoordinate.Y - offset);
        }
    }

    public static bool IsPlacementModeActive(GameStageStateSnapshot? snapshot)
    {
        return snapshot?.IsPlacingMonkey == true;
    }

    public static UpgradePanelSide? ResolveVisibleUpgradePanelSide(GameStageStateSnapshot? snapshot)
    {
        if (snapshot?.RightUpgradePanel.IsVisible == true)
        {
            return UpgradePanelSide.Right;
        }

        if (snapshot?.LeftUpgradePanel.IsVisible == true)
        {
            return UpgradePanelSide.Left;
        }

        return null;
    }

    public static int? GetUpgradeLevel(
        GameStageStateSnapshot snapshot,
        UpgradePanelSide panelSide,
        UpgradePathType upgradePath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var panelState = panelSide == UpgradePanelSide.Right
            ? snapshot.RightUpgradePanel
            : snapshot.LeftUpgradePanel;

        return upgradePath switch
        {
            UpgradePathType.Top => panelState.TopPathLevel,
            UpgradePathType.Middle => panelState.MiddlePathLevel,
            UpgradePathType.Bottom => panelState.BottomPathLevel,
            _ => null
        };
    }

    public static bool IsHeroObjectKey(string? objectKey)
    {
        return !string.IsNullOrWhiteSpace(objectKey) &&
               objectKey.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ResolveMonkeyPanelDetectionEnabled(string? objectKey, bool detectionEnabled)
    {
        return !IsHeroObjectKey(objectKey) && detectionEnabled;
    }

    public static bool ResolveSellDetectionEnabled(string? objectKey, bool detectionEnabled)
    {
        return !IsHeroObjectKey(objectKey) && detectionEnabled;
    }

    public static bool ShouldSelectMonkeyForPanelInteraction(ScriptInstructionExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !HasAdjacentMonkeyPanelInstructionWithSameTarget(context, stepOffset: -1);
    }

    public static bool ShouldCloseMonkeyPanelAfterInstruction(ScriptInstructionExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !HasAdjacentMonkeyPanelInstructionWithSameTarget(context, stepOffset: 1);
    }

    public static bool IsWaitTimeout(ScriptExecutionException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return string.Equals(exception.Checkpoint, "WaitTimedOut", StringComparison.Ordinal);
    }

    public static int ResolveInstructionIntervalMilliseconds(
        ScriptTaskFlowStep step,
        ScriptExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(options);

        if (TryResolveCommonOperationIntervalMilliseconds(options, out var commonIntervalMilliseconds))
        {
            return commonIntervalMilliseconds;
        }

        if (options.OverrideInstructionIntervalMs.HasValue)
        {
            return Math.Max(0, options.OverrideInstructionIntervalMs.Value);
        }

        return Math.Max(0, step.Instruction.IntervalToNextInstructionMs);
    }

    public static int ResolveOperationIntervalMilliseconds(
        ScriptExecutionOptions options,
        int configuredIntervalMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TryResolveCommonOperationIntervalMilliseconds(options, out var commonIntervalMilliseconds))
        {
            return commonIntervalMilliseconds;
        }

        return Math.Max(0, configuredIntervalMilliseconds);
    }

    public static int ResolveOperationIntervalMilliseconds(
        ScriptExecutionOptions options,
        int? configuredIntervalMilliseconds,
        int defaultIntervalMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ResolveOperationIntervalMilliseconds(
            options,
            configuredIntervalMilliseconds ?? defaultIntervalMilliseconds);
    }

    public static string FormatPoint(WpfPoint point)
    {
        return $"({point.X:0.##}, {point.Y:0.##})";
    }

    public static ScriptExecutionException CreateExecutionException(
        ScriptInstructionExecutionContext context,
        string checkpoint,
        string message,
        int attempt = 0,
        Exception? innerException = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);

        return new ScriptExecutionException(
            message,
            context.Step.Index,
            context.Step.CommandType.ToString(),
            checkpoint,
            attempt,
            innerException);
    }

    public static async Task CancelPlacementModeIfActiveAsync(
        ScriptInstructionExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var snapshot = await context.RuntimeServices.GameStageState
            .CaptureSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!IsPlacementModeActive(snapshot))
        {
            return;
        }

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "PlaceMonkeyCancel",
            "Placement mode is already active. Sending Escape to reset it.",
            cancellationToken).ConfigureAwait(false);

        context.RuntimeServices.Input.PressKey(KeyId.Escape);

        try
        {
            await ScriptExecutionOperations.WaitUntilAsync(
                context,
                new ScriptWaitOptions
                {
                    TimeoutMilliseconds = 700,
                    PollIntervalMilliseconds = 100,
                    Description = "placement mode reset"
                },
                async innerToken =>
                {
                    var currentSnapshot = await context.RuntimeServices.GameStageState
                        .CaptureSnapshotAsync(innerToken)
                        .ConfigureAwait(false);
                    return currentSnapshot?.IsPlacingMonkey == false;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ScriptExecutionException ex) when (IsWaitTimeout(ex))
        {
        }
    }

    public static async Task<GameStageStateSnapshot> EnsureUpgradePanelVisibleAsync(
        ScriptInstructionExecutionContext context,
        WpfPoint targetCoordinate,
        CancellationToken cancellationToken)
    {
        return await ScriptExecutionOperations.RetryAsync(
            context,
            new ScriptRetryOptions
            {
                MaxAttempts = 3,
                DelayBetweenAttemptsMilliseconds = 150,
                Description = $"Open upgrade panel at {FormatPoint(targetCoordinate)}"
            },
            async (attempt, token) =>
            {
                foreach (var selectionCoordinate in BuildOffsetCoordinates(targetCoordinate, SelectionOffsets))
                {
                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "UpgradeMonkeySelect",
                        $"Selection attempt {attempt}: clicking {FormatPoint(selectionCoordinate)}.",
                        token).ConfigureAwait(false);

                    context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(selectionCoordinate, clickCount: 1);

                    GameStageStateSnapshot? visibleSnapshot = null;
                    try
                    {
                        await ScriptExecutionOperations.WaitUntilAsync(
                            context,
                            new ScriptWaitOptions
                            {
                                TimeoutMilliseconds = 700,
                                PollIntervalMilliseconds = 100,
                                Description = "upgrade panel visible"
                            },
                            async innerToken =>
                            {
                                visibleSnapshot = await context.RuntimeServices.GameStageState
                                    .CaptureSnapshotAsync(innerToken)
                                    .ConfigureAwait(false);
                                return ResolveVisibleUpgradePanelSide(visibleSnapshot).HasValue;
                            },
                            token).ConfigureAwait(false);
                    }
                    catch (ScriptExecutionException ex) when (IsWaitTimeout(ex))
                    {
                    }

                    if (ResolveVisibleUpgradePanelSide(visibleSnapshot).HasValue)
                    {
                        return visibleSnapshot!;
                    }
                }

                throw CreateExecutionException(
                    context,
                    "UpgradeMonkeySelect",
                    $"Failed to open the upgrade panel near {FormatPoint(targetCoordinate)}.",
                    attempt);
            },
            snapshot => ResolveVisibleUpgradePanelSide(snapshot).HasValue,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task CloseUpgradePanelAsync(
        ScriptInstructionExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "UpgradePanelClose",
            "Sending Escape to close the upgrade panel.",
            cancellationToken).ConfigureAwait(false);

        context.RuntimeServices.Input.PressKey(KeyId.Escape);
    }

    public static async Task<GameStageStateSnapshot?> PrepareMonkeyPanelInteractionAsync(
        ScriptInstructionExecutionContext context,
        WpfPoint targetCoordinate,
        bool shouldSelectMonkey,
        bool panelDetectionEnabled,
        int operationIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var effectiveOperationIntervalMilliseconds = Math.Max(0, operationIntervalMilliseconds);

        if (!shouldSelectMonkey)
        {
            if (!panelDetectionEnabled)
            {
                return null;
            }

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "MonkeyPanelReuse",
                "Reusing the currently open monkey upgrade panel from the previous instruction.",
                cancellationToken).ConfigureAwait(false);

            var visibleSnapshot = await context.RuntimeServices.GameStageState
                .CaptureSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            if (ResolveVisibleUpgradePanelSide(visibleSnapshot).HasValue)
            {
                return visibleSnapshot;
            }

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "MonkeyPanelReuseFallback",
                "The chained monkey panel was not visible. Re-selecting the monkey and reopening the panel.",
                cancellationToken).ConfigureAwait(false);

            return await WaitForUpgradePanelVisibleAsync(
                context,
                targetCoordinate,
                10 * 60 * 1000,
                effectiveOperationIntervalMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }

        if (panelDetectionEnabled)
        {
            return await WaitForUpgradePanelVisibleAsync(
                context,
                targetCoordinate,
                10 * 60 * 1000,
                effectiveOperationIntervalMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "UpgradeMonkeySelect",
            $"Selecting monkey at {FormatPoint(targetCoordinate)} without panel detection.",
            cancellationToken).ConfigureAwait(false);

        context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(targetCoordinate, clickCount: 1);

        await ScriptExecutionOperations.DelayAsync(
            context,
            effectiveOperationIntervalMilliseconds,
            "UpgradeMonkeySelectDelay",
            cancellationToken).ConfigureAwait(false);

        return null;
    }

    public static async Task<GameStageStateSnapshot> WaitForUpgradePanelVisibleAsync(
        ScriptInstructionExecutionContext context,
        WpfPoint targetCoordinate,
        int timeoutMilliseconds,
        int panelPollIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        GameStageStateSnapshot? visibleSnapshot = null;
        var effectiveTimeoutMilliseconds = Math.Max(0, timeoutMilliseconds);
        var effectivePanelPollIntervalMilliseconds = Math.Max(0, panelPollIntervalMilliseconds);
        var startedAt = DateTimeOffset.UtcNow;
        var selectionAttempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsedOverallMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            if (elapsedOverallMilliseconds >= effectiveTimeoutMilliseconds)
            {
                throw CreateExecutionException(
                    context,
                    "UpgradeMonkeyPanel",
                    $"Failed to detect the upgrade panel near {FormatPoint(targetCoordinate)}.");
            }

            selectionAttempt++;

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "UpgradeMonkeySelect",
                $"Selection attempt {selectionAttempt}: clicking {FormatPoint(targetCoordinate)}.",
                cancellationToken).ConfigureAwait(false);

            context.RuntimeServices.Input.ClickMouseAtScriptCoordinate(targetCoordinate, clickCount: 1);

            var selectionAttemptStartedAt = DateTimeOffset.UtcNow;
            var detectionPoll = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var selectionAttemptElapsedMilliseconds = (DateTimeOffset.UtcNow - selectionAttemptStartedAt).TotalMilliseconds;
                var remainingSelectionAttemptMilliseconds = MonkeyPanelSelectionAttemptTimeoutMilliseconds - selectionAttemptElapsedMilliseconds;
                var remainingOverallMilliseconds = effectiveTimeoutMilliseconds - (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                if (remainingSelectionAttemptMilliseconds <= 0 || remainingOverallMilliseconds <= 0)
                {
                    break;
                }

                var delayMilliseconds = effectivePanelPollIntervalMilliseconds > 0
                    ? (int)Math.Min(
                        Math.Min(remainingSelectionAttemptMilliseconds, remainingOverallMilliseconds),
                        effectivePanelPollIntervalMilliseconds)
                    : 0;

                if (delayMilliseconds > 0)
                {
                    await ScriptExecutionOperations.DelayAsync(
                        context,
                        delayMilliseconds,
                        "UpgradeMonkeySelectDelay",
                        cancellationToken).ConfigureAwait(false);
                }

                detectionPoll++;
                visibleSnapshot = await context.RuntimeServices.GameStageState
                    .CaptureSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (ResolveVisibleUpgradePanelSide(visibleSnapshot).HasValue)
                {
                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "UpgradeMonkeyPanelVisible",
                        $"Upgrade panel became visible after selection attempt {selectionAttempt}, poll {detectionPoll}.",
                        cancellationToken).ConfigureAwait(false);

                    return visibleSnapshot!;
                }
            }

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "UpgradeMonkeySelectRetry",
                $"Selection attempt {selectionAttempt} timed out after {MonkeyPanelSelectionAttemptTimeoutMilliseconds} ms without detecting the panel. Re-selecting monkey.",
                cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ExecuteSellMonkeyAsync(
        ScriptInstructionExecutionContext context,
        string monkeyObjectId,
        HotkeyBinding sellHotkey,
        bool sellDetectionEnabled,
        int timeoutMilliseconds,
        int detectionIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(monkeyObjectId);
        ArgumentNullException.ThrowIfNull(sellHotkey);

        var effectiveDetectionIntervalMilliseconds = Math.Max(0, detectionIntervalMilliseconds);

        if (!sellDetectionEnabled)
        {
            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "SellMonkeyPress",
                $"Selling '{monkeyObjectId}' with hotkey '{sellHotkey.DisplayName}' without sell detection.",
                cancellationToken).ConfigureAwait(false);

            context.RuntimeServices.Input.PressHotkey(sellHotkey);

            await ScriptExecutionOperations.CheckpointAsync(
                context,
                "SellMonkeySucceeded",
                $"Sent sell hotkey for '{monkeyObjectId}' without sell detection.",
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var pressCount = 0;

        await ScriptExecutionOperations.WaitUntilAsync(
            context,
            new ScriptWaitOptions
            {
                TimeoutMilliseconds = timeoutMilliseconds,
                PollIntervalMilliseconds = 10,
                Description = "sell panel close"
            },
            async innerToken =>
            {
                pressCount++;

                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "SellMonkeyPress",
                    $"Sell attempt {pressCount}: sending hotkey '{sellHotkey.DisplayName}' for '{monkeyObjectId}'.",
                    innerToken).ConfigureAwait(false);

                context.RuntimeServices.Input.PressHotkey(sellHotkey);

                await ScriptExecutionOperations.DelayAsync(
                    context,
                    effectiveDetectionIntervalMilliseconds,
                    "SellMonkeyPressInterval",
                    innerToken).ConfigureAwait(false);

                var snapshot = await context.RuntimeServices.GameStageState
                    .CaptureSnapshotAsync(innerToken)
                    .ConfigureAwait(false);
                return ResolveVisibleUpgradePanelSide(snapshot) is null;
            },
            cancellationToken).ConfigureAwait(false);

        await ScriptExecutionOperations.CheckpointAsync(
            context,
            "SellMonkeySucceeded",
            $"Sold '{monkeyObjectId}' after {pressCount} attempt(s).",
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task WaitForPlacementModeActiveAsync(
        ScriptInstructionExecutionContext context,
        HotkeyBinding hotkey,
        string selectionCode,
        int attempt,
        int timeoutMilliseconds,
        int pollIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionCode);

        var pressCount = 0;

        await ScriptExecutionOperations.WaitUntilAsync(
            context,
            new ScriptWaitOptions
            {
                TimeoutMilliseconds = timeoutMilliseconds,
                PollIntervalMilliseconds = pollIntervalMilliseconds,
                Description = "placement mode active"
            },
            async innerToken =>
            {
                pressCount++;

                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "PlaceMonkeySelect",
                    $"Placement attempt {attempt}: sending hotkey '{hotkey.DisplayName}' for '{selectionCode}' (press {pressCount}).",
                    innerToken).ConfigureAwait(false);

                context.RuntimeServices.Input.PressHotkey(hotkey);

                var snapshot = await context.RuntimeServices.GameStageState
                    .CaptureSnapshotAsync(innerToken)
                    .ConfigureAwait(false);
                return IsPlacementModeActive(snapshot);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static Task PressHotkeyRepeatedAsync(
        ScriptInstructionExecutionContext context,
        HotkeyBinding hotkey,
        int repeatCount,
        string checkpoint,
        string description,
        int intervalMilliseconds,
        CancellationToken cancellationToken)
    {
        return PressHotkeyRepeatedAsync(
            context,
            hotkey,
            repeatCount,
            checkpoint,
            description,
            intervalMilliseconds,
            modifierTransitionIntervalMilliseconds: 50,
            cancellationToken);
    }

    public static async Task PressHotkeyRepeatedAsync(
        ScriptInstructionExecutionContext context,
        HotkeyBinding hotkey,
        int repeatCount,
        string checkpoint,
        string description,
        int intervalMilliseconds,
        int modifierTransitionIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint);

        var effectiveRepeatCount = Math.Max(1, repeatCount);
        var effectiveIntervalMilliseconds = Math.Max(50, intervalMilliseconds);
        var effectiveModifierTransitionIntervalMilliseconds = Math.Max(50, modifierTransitionIntervalMilliseconds);
        var effectiveDescription = string.IsNullOrWhiteSpace(description)
            ? $"Sending hotkey '{hotkey.DisplayName}'."
            : description;
        var modifierKeys = ExpandModifierKeys(hotkey.Modifiers);

        try
        {
            if (modifierKeys.Count > 0)
            {
                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    $"{checkpoint}ModifiersDown",
                    $"{effectiveDescription} Holding modifiers '{hotkey.DisplayName}'.",
                    cancellationToken).ConfigureAwait(false);

                foreach (var modifierKey in modifierKeys)
                {
                    context.RuntimeServices.Input.KeyDown(modifierKey);
                }

                if (effectiveModifierTransitionIntervalMilliseconds > 0)
                {
                    await ScriptExecutionOperations.DelayAsync(
                        context,
                        effectiveModifierTransitionIntervalMilliseconds,
                        $"{checkpoint}ModifiersDownInterval",
                        cancellationToken).ConfigureAwait(false);
                }
            }

            for (var index = 0; index < effectiveRepeatCount; index++)
            {
                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    checkpoint,
                    $"{effectiveDescription} Press {index + 1}/{effectiveRepeatCount}.",
                    cancellationToken).ConfigureAwait(false);

                context.RuntimeServices.Input.PressKey(hotkey.Key);

                if (index < effectiveRepeatCount - 1)
                {
                    await ScriptExecutionOperations.DelayAsync(
                        context,
                        effectiveIntervalMilliseconds,
                        $"{checkpoint}Interval",
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (modifierKeys.Count > 0 && effectiveModifierTransitionIntervalMilliseconds > 0)
            {
                await ScriptExecutionOperations.DelayAsync(
                    context,
                    effectiveModifierTransitionIntervalMilliseconds,
                    $"{checkpoint}ModifiersUpInterval",
                    cancellationToken).ConfigureAwait(false);
            }

            for (var index = modifierKeys.Count - 1; index >= 0; index--)
            {
                context.RuntimeServices.Input.KeyUp(modifierKeys[index]);
            }
        }
    }

    public static Task PressHotkeyRepeatedAsync(
        ScriptInstructionExecutionContext context,
        HotkeyBinding hotkey,
        int repeatCount,
        string checkpoint,
        string description,
        CancellationToken cancellationToken)
    {
        return PressHotkeyRepeatedAsync(
            context,
            hotkey,
            repeatCount,
            checkpoint,
            description,
            RepeatedHotkeyIntervalMilliseconds,
            cancellationToken);
    }

    private static IEnumerable<WpfPoint> BuildOffsetCoordinates(
        WpfPoint baseCoordinate,
        IReadOnlyList<(double OffsetX, double OffsetY)> offsets)
    {
        foreach (var (offsetX, offsetY) in offsets)
        {
            yield return new WpfPoint(baseCoordinate.X + offsetX, baseCoordinate.Y + offsetY);
        }
    }

    private static List<KeyId> ExpandModifierKeys(ModifierKeys modifiers)
    {
        var keys = new List<KeyId>(4);

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            keys.Add(KeyId.LeftCtrl);
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            keys.Add(KeyId.LeftShift);
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            keys.Add(KeyId.LeftAlt);
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            keys.Add(KeyId.LeftWin);
        }

        return keys;
    }

    private static bool TryResolveCommonOperationIntervalMilliseconds(
        ScriptExecutionOptions options,
        out int commonIntervalMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.IntervalStrategy == ScriptExecutionOperationIntervalStrategy.CommonOperationInterval)
        {
            commonIntervalMilliseconds = Math.Clamp(
                options.CommonOperationIntervalMs,
                CommonOperationIntervalMinimumMilliseconds,
                CommonOperationIntervalMaximumMilliseconds);
            return true;
        }

        commonIntervalMilliseconds = 0;
        return false;
    }

    private static bool HasAdjacentMonkeyPanelInstructionWithSameTarget(
        ScriptInstructionExecutionContext context,
        int stepOffset)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryGetStepPosition(context, out var currentStepPosition))
        {
            return false;
        }

        var adjacentStepPosition = currentStepPosition + stepOffset;
        if (adjacentStepPosition < 0 || adjacentStepPosition >= context.TaskFlow.Steps.Count)
        {
            return false;
        }

        var currentStep = context.TaskFlow.Steps[currentStepPosition];
        var adjacentStep = context.TaskFlow.Steps[adjacentStepPosition];
        if (!IsMonkeyPanelInstruction(currentStep.CommandType) || !IsMonkeyPanelInstruction(adjacentStep.CommandType))
        {
            return false;
        }

        return AreSameMonkeyPanelTarget(currentStep.Instruction, adjacentStep.Instruction);
    }

    private static bool TryGetStepPosition(
        ScriptInstructionExecutionContext context,
        out int stepPosition)
    {
        for (var index = 0; index < context.TaskFlow.Steps.Count; index++)
        {
            if (ReferenceEquals(context.TaskFlow.Steps[index], context.Step))
            {
                stepPosition = index;
                return true;
            }
        }

        for (var index = 0; index < context.TaskFlow.Steps.Count; index++)
        {
            if (context.TaskFlow.Steps[index].Index == context.Step.Index)
            {
                stepPosition = index;
                return true;
            }
        }

        stepPosition = -1;
        return false;
    }

    private static bool IsMonkeyPanelInstruction(ScriptCommandType commandType)
    {
        return commandType is ScriptCommandType.UpgradeMonkey
            or ScriptCommandType.SwitchMonkeyTarget
            or ScriptCommandType.SetMonkeyAbility
            or ScriptCommandType.SellMonkey;
    }

    private static bool AreSameMonkeyPanelTarget(
        ScriptInstructionDocument currentInstruction,
        ScriptInstructionDocument adjacentInstruction)
    {
        ArgumentNullException.ThrowIfNull(currentInstruction);
        ArgumentNullException.ThrowIfNull(adjacentInstruction);

        if (!string.IsNullOrWhiteSpace(currentInstruction.TargetMonkeyBindingId) &&
            !string.IsNullOrWhiteSpace(adjacentInstruction.TargetMonkeyBindingId))
        {
            return string.Equals(
                currentInstruction.TargetMonkeyBindingId,
                adjacentInstruction.TargetMonkeyBindingId,
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(currentInstruction.TargetMonkeyObjectId) &&
            !string.IsNullOrWhiteSpace(adjacentInstruction.TargetMonkeyObjectId))
        {
            return string.Equals(
                currentInstruction.TargetMonkeyObjectId,
                adjacentInstruction.TargetMonkeyObjectId,
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
