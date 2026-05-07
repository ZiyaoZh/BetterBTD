using BetterBTD.Models.ScriptEditor;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.Config;
using BetterBTD.Services;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.ScriptExecution.Handlers;

public interface IScriptInstructionHandler
{
    ScriptCommandType CommandType { get; }

    Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken);
}

public abstract class ScriptInstructionHandlerBase : IScriptInstructionHandler
{
    public abstract ScriptCommandType CommandType { get; }

    public abstract Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken);
}

public sealed class ScriptInstructionHandlerRegistry
{
    private static readonly Lazy<ScriptInstructionHandlerRegistry> InstanceHolder = new(() => new ScriptInstructionHandlerRegistry());

    private readonly Dictionary<ScriptCommandType, IScriptInstructionHandler> _handlers = [];

    private ScriptInstructionHandlerRegistry()
    {
        Register(new PlaceMonkeyInstructionHandler());
        Register(new UpgradeMonkeyInstructionHandler());
        Register(new SwitchMonkeyTargetInstructionHandler());
        Register(new SetMonkeyAbilityInstructionHandler());
        Register(new SellMonkeyInstructionHandler());
        Register(new PlaceHeroInventoryInstructionHandler());
        Register(new ActivateAbilityInstructionHandler());
        Register(new MouseClickInstructionHandler());
        Register(new NextRoundInstructionHandler());
        Register(new WaitInstructionHandler());
        Register(new ModifyMonkeyCoordinateInstructionHandler());
        Register(new CommentInstructionHandler());
    }

    public static ScriptInstructionHandlerRegistry Instance => InstanceHolder.Value;

    public void Register(IScriptInstructionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[handler.CommandType] = handler;
    }

    public IScriptInstructionHandler GetRequiredHandler(ScriptCommandType commandType)
    {
        if (_handlers.TryGetValue(commandType, out var handler))
        {
            return handler;
        }

        throw new InvalidOperationException($"No instruction handler was registered for '{commandType}'.");
    }
}

public sealed class PlaceMonkeyInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.PlaceMonkey;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        var selectionCode = ScriptEditorInstructionService.NormalizePlaceSelectionCode(instruction.SelectedMonkeyTower);
        var requestedCoordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);
        var placementHotkey = ScriptExecutionKeyBindingResolver.ResolvePlacementHotkey(selectionCode);

        await ScriptInstructionHandlerSupport.CancelPlacementModeIfActiveAsync(context, cancellationToken).ConfigureAwait(false);

        if (ScriptEditorInstructionService.TryParseHeroSelection(selectionCode, out _))
        {
            var precheckSnapshot = await ScriptExecutionOperations
                .CaptureRequiredSnapshotAsync(context, "PlaceMonkeyHeroPrecheck", cancellationToken)
                .ConfigureAwait(false);

            if (precheckSnapshot.CanPlaceHero == false)
            {
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "PlaceMonkeyHeroPrecheck",
                    "The configured hero is not currently available for placement.");
            }
        }

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

                await ScriptExecutionOperations.CheckpointAsync(
                    context,
                    "PlaceMonkeySelect",
                    $"Placement attempt {attempt}: sending hotkey for '{selectionCode}'.",
                    token).ConfigureAwait(false);

                context.RuntimeServices.Input.PressHotkey(placementHotkey);

                await ScriptExecutionOperations.WaitUntilAsync(
                    context,
                    new ScriptWaitOptions
                    {
                        TimeoutMilliseconds = 1000,
                        PollIntervalMilliseconds = 100,
                        Description = "placement mode active"
                    },
                    async innerToken =>
                    {
                        var snapshot = await context.RuntimeServices.GameStageState
                            .CaptureSnapshotAsync(innerToken)
                            .ConfigureAwait(false);
                        return ScriptInstructionHandlerSupport.IsPlacementModeActive(snapshot);
                    },
                    token).ConfigureAwait(false);

                foreach (var placementCoordinate in ScriptInstructionHandlerSupport.BuildPlacementSearchCoordinates(requestedCoordinate))
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
                                TimeoutMilliseconds = 400,
                                PollIntervalMilliseconds = 75,
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
                        var monkeyDocument = context.TaskFlow.MonkeyObjectsByBindingId.GetValueOrDefault(instruction.MonkeyBindingId);
                        var runtimeState = context.State.UpsertMonkeyState(
                            instruction.MonkeyBindingId,
                            string.IsNullOrWhiteSpace(instruction.MonkeyObjectId)
                                ? monkeyDocument?.ObjectId ?? string.Empty
                                : instruction.MonkeyObjectId,
                            selectionCode,
                            monkeyDocument?.PlacementOrder ?? 0);
                        runtimeState.LastKnownCoordinate = placementCoordinate;

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
                    $"Failed to place '{selectionCode}' near {ScriptInstructionHandlerSupport.FormatPoint(requestedCoordinate)} after offset search.",
                    attempt);
            },
            static success => success,
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class UpgradeMonkeyInstructionHandler : ScriptInstructionHandlerBase
{
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

        if (ScriptInstructionHandlerSupport.IsHeroObjectKey(monkeyState.ObjectId) ||
            ScriptInstructionHandlerSupport.IsHeroObjectKey(instruction.TargetMonkeyObjectId))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyHeroUnsupported",
                "Hero upgrades are not yet supported because the runtime cannot verify hero upgrade success.");
        }

        if (monkeyState.LastKnownCoordinate is null)
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyCoordinate",
                $"Target monkey '{monkeyState.ObjectId}' does not have a known runtime coordinate.");
        }

        if (!Enum.TryParse<UpgradePathType>(instruction.UpgradePath, true, out var upgradePath))
        {
            throw ScriptInstructionHandlerSupport.CreateExecutionException(
                context,
                "UpgradeMonkeyPath",
                $"Unsupported upgrade path '{instruction.UpgradePath}'.");
        }

        var targetCoordinate = monkeyState.LastKnownCoordinate.Value;
        var upgradeHotkey = ScriptExecutionKeyBindingResolver.ResolveUpgradeHotkey(upgradePath);
        var upgradeCount = Math.Max(1, instruction.UpgradeCount);

        for (var upgradeIndex = 1; upgradeIndex <= upgradeCount; upgradeIndex++)
        {
            var panelSnapshot = await ScriptInstructionHandlerSupport
                .EnsureUpgradePanelVisibleAsync(context, targetCoordinate, cancellationToken)
                .ConfigureAwait(false);
            var panelSide = ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(panelSnapshot);
            if (!panelSide.HasValue)
            {
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "UpgradeMonkeyPanel",
                    "Failed to detect the upgrade panel for the selected monkey.");
            }

            var currentLevel = ScriptInstructionHandlerSupport.GetUpgradeLevel(
                panelSnapshot,
                panelSide.Value,
                upgradePath);
            if (!currentLevel.HasValue)
            {
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "UpgradeMonkeyPanel",
                    $"Failed to read the current '{instruction.UpgradePath}' path level.");
            }

            if (currentLevel.Value >= 5)
            {
                throw ScriptInstructionHandlerSupport.CreateExecutionException(
                    context,
                    "UpgradeMonkeyLevelCap",
                    $"The '{instruction.UpgradePath}' path is already at level 5.");
            }

            var expectedLevel = currentLevel.Value + 1;

            await ScriptExecutionOperations.RetryAsync(
                context,
                new ScriptRetryOptions
                {
                    MaxAttempts = 3,
                    DelayBetweenAttemptsMilliseconds = 150,
                    Description = $"Upgrade '{monkeyState.ObjectId}' {instruction.UpgradePath} to level {expectedLevel}"
                },
                async (attempt, token) =>
                {
                    var beforePressSnapshot = await ScriptInstructionHandlerSupport
                        .EnsureUpgradePanelVisibleAsync(context, targetCoordinate, token)
                        .ConfigureAwait(false);
                    var visiblePanelSide = ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(beforePressSnapshot);
                    if (!visiblePanelSide.HasValue)
                    {
                        throw ScriptInstructionHandlerSupport.CreateExecutionException(
                            context,
                            "UpgradeMonkeyPanel",
                            "Failed to restore the upgrade panel before sending the upgrade hotkey.",
                            attempt);
                    }

                    var beforePressLevel = ScriptInstructionHandlerSupport.GetUpgradeLevel(
                        beforePressSnapshot,
                        visiblePanelSide.Value,
                        upgradePath);
                    if (!beforePressLevel.HasValue)
                    {
                        throw ScriptInstructionHandlerSupport.CreateExecutionException(
                            context,
                            "UpgradeMonkeyPanel",
                            $"Failed to read the '{instruction.UpgradePath}' path level before upgrading.",
                            attempt);
                    }

                    if (beforePressLevel.Value >= expectedLevel)
                    {
                        return true;
                    }

                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "UpgradeMonkeyPress",
                        $"Upgrade {upgradeIndex}/{upgradeCount}, attempt {attempt}: sending '{instruction.UpgradePath}' upgrade hotkey.",
                        token).ConfigureAwait(false);

                    context.RuntimeServices.Input.PressHotkey(upgradeHotkey);

                    await ScriptExecutionOperations.WaitUntilAsync(
                        context,
                        new ScriptWaitOptions
                        {
                            TimeoutMilliseconds = 900,
                            PollIntervalMilliseconds = 100,
                            Description = $"upgrade path reach level {expectedLevel}"
                        },
                        async innerToken =>
                        {
                            var afterPressSnapshot = await context.RuntimeServices.GameStageState
                                .CaptureSnapshotAsync(innerToken)
                                .ConfigureAwait(false);
                            if (afterPressSnapshot is null)
                            {
                                return false;
                            }

                            var afterPressPanelSide = ScriptInstructionHandlerSupport.ResolveVisibleUpgradePanelSide(afterPressSnapshot);
                            if (!afterPressPanelSide.HasValue)
                            {
                                return false;
                            }

                            var afterPressLevel = ScriptInstructionHandlerSupport.GetUpgradeLevel(
                                afterPressSnapshot,
                                afterPressPanelSide.Value,
                                upgradePath);
                            return afterPressLevel.HasValue && afterPressLevel.Value >= expectedLevel;
                        },
                        token).ConfigureAwait(false);

                    await ScriptExecutionOperations.CheckpointAsync(
                        context,
                        "UpgradeMonkeySucceeded",
                        $"Upgrade {upgradeIndex}/{upgradeCount}: '{instruction.UpgradePath}' reached level {expectedLevel}.",
                        token).ConfigureAwait(false);

                    return true;
                },
                static success => success,
                cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class SwitchMonkeyTargetInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.SwitchMonkeyTarget;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for rotating targeting priorities on an existing monkey.
        return Task.CompletedTask;
    }
}

public sealed class SetMonkeyAbilityInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.SetMonkeyAbility;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for selecting a monkey ability and optionally targeting a coordinate.
        return Task.CompletedTask;
    }
}

public sealed class SellMonkeyInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.SellMonkey;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for selecting a monkey and sending the sell action.
        return Task.CompletedTask;
    }
}

public sealed class PlaceHeroInventoryInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.PlaceHeroInventory;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for selecting hero inventory and placing the item at a target coordinate.
        return Task.CompletedTask;
    }
}

public sealed class ActivateAbilityInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.ActivateAbility;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for triggering a global activated ability and optional target click.
        return Task.CompletedTask;
    }
}

public sealed class MouseClickInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.MouseClick;

    public override async Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        var instruction = context.Step.Instruction;
        var coordinate = new WpfPoint(instruction.PositionX, instruction.PositionY);
        var clickCount = Math.Max(1, instruction.ClickCount);
        var clickIntervalMilliseconds = Math.Max(0, instruction.ClickIntervalMilliseconds);

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

public sealed class NextRoundInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.NextRound;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for fast-forward / next-round actions.
        return Task.CompletedTask;
    }
}

public sealed class WaitInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.Wait;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for wait conditions backed by OCR and capture services.
        return Task.CompletedTask;
    }
}

public sealed class ModifyMonkeyCoordinateInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.ModifyMonkeyCoordinate;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for updating runtime coordinates of an existing monkey binding.
        return Task.CompletedTask;
    }
}

public sealed class CommentInstructionHandler : ScriptInstructionHandlerBase
{
    public override ScriptCommandType CommandType => ScriptCommandType.Comment;

    public override Task HandleAsync(ScriptInstructionExecutionContext context, CancellationToken cancellationToken)
    {
        // Comments do not change runtime state and are intentionally ignored.
        return Task.CompletedTask;
    }
}

internal enum UpgradePanelSide
{
    Left,
    Right
}

internal static class ScriptInstructionHandlerSupport
{
    private static readonly (double OffsetX, double OffsetY)[] PlacementOffsets =
    [
        (0d, 0d),
        (4d, 0d),
        (-4d, 0d),
        (0d, 4d),
        (0d, -4d),
        (8d, 0d),
        (-8d, 0d),
        (0d, 8d),
        (0d, -8d),
        (6d, 6d),
        (-6d, 6d),
        (6d, -6d),
        (-6d, -6d)
    ];

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
        return BuildOffsetCoordinates(requestedCoordinate, PlacementOffsets);
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

    public static bool IsWaitTimeout(ScriptExecutionException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return string.Equals(exception.Checkpoint, "WaitTimedOut", StringComparison.Ordinal);
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

    private static IEnumerable<WpfPoint> BuildOffsetCoordinates(
        WpfPoint baseCoordinate,
        IReadOnlyList<(double OffsetX, double OffsetY)> offsets)
    {
        foreach (var (offsetX, offsetY) in offsets)
        {
            yield return new WpfPoint(baseCoordinate.X + offsetX, baseCoordinate.Y + offsetY);
        }
    }
}
