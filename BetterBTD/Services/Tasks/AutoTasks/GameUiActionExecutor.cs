using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.Config;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class GameUiActionExecutor : IGameUiActionExecutor
{
    private static readonly Lazy<GameUiActionExecutor> InstanceHolder = new(() => new GameUiActionExecutor());

    private readonly ScriptInputSimulationService _inputSimulationService;
    private readonly IGameUiElementLocator _elementLocator;
    private readonly GameCaptureService _gameCaptureService;
    private readonly GameUiNavigationOcrService _navigationOcrService;

    private GameUiActionExecutor()
        : this(
            ScriptInputSimulationService.Instance,
            UnimplementedGameUiElementLocator.Instance,
            GameCaptureService.Instance,
            GameUiNavigationOcrService.Instance)
    {
    }

    internal GameUiActionExecutor(
        ScriptInputSimulationService inputSimulationService,
        IGameUiElementLocator elementLocator,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
    {
        _inputSimulationService = inputSimulationService ?? throw new ArgumentNullException(nameof(inputSimulationService));
        _elementLocator = elementLocator ?? throw new ArgumentNullException(nameof(elementLocator));
        _gameCaptureService = gameCaptureService ?? throw new ArgumentNullException(nameof(gameCaptureService));
        _navigationOcrService = navigationOcrService ?? throw new ArgumentNullException(nameof(navigationOcrService));
    }

    public static GameUiActionExecutor Instance => InstanceHolder.Value;

    public async Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        cancellationToken.ThrowIfCancellationRequested();

        if (step.ActionKind is GameUiActionKind.None or GameUiActionKind.Wait)
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = true,
                Message = step.Description,
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        if (state.Request.Kind == AutoTaskKind.Collection)
        {
            return await ExecuteCollectionAsync(step, state, snapshot, cancellationToken).ConfigureAwait(false);
        }

        if (!_elementLocator.TryLocateScriptPoint(
            step.ActionKind,
            state.Request.StageTarget,
            snapshot,
            out var scriptPoint,
            out var failureMessage))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = string.IsNullOrWhiteSpace(failureMessage)
                    ? $"No locator is available for action '{step.ActionKind}'."
                    : failureMessage,
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        _inputSimulationService.PrepareTargetWindowForInput();
        _inputSimulationService.ClickMouseAtScriptCoordinate(scriptPoint);

        return new GameUiActionExecutionResult
        {
            Succeeded = true,
            Message = step.Description,
            RecommendedDelayMs = step.PostActionDelayMs
        };
    }

    private async Task<GameUiActionExecutionResult> ExecuteCollectionAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (snapshot.State)
        {
            case GameUiStateId.MainMenu:
                return Click(step, new WpfPoint(960, 940), "Opened collection flow from the main menu.");
            case GameUiStateId.RaceResult:
                return Click(step, new WpfPoint(960, 800), "Closed the race result overlay.");
            case GameUiStateId.BossResult:
                return Click(step, new WpfPoint(960, 880), "Closed the boss result overlay.");
            case GameUiStateId.MapGrid:
            case GameUiStateId.CollectionEvent:
                return Click(step, new WpfPoint(80, 55), "Returned from the current collection screen.");
            case GameUiStateId.CollectionEventClaimable:
                return Click(step, new WpfPoint(960, 680), "Opened the claimable collection chest.");
            case GameUiStateId.MapSearch:
                return ExecuteCollectionMapSearch(step, state);
            case GameUiStateId.MapSearchResults:
                return ExecuteCollectionMapSearchResults(step, state, snapshot);
            case GameUiStateId.DifficultySelect:
                return ExecuteCollectionDifficultySelect(step, state);
            case GameUiStateId.EasyModeSelect:
                return ExecuteCollectionModeSelect(step, state, StageDifficulty.Easy);
            case GameUiStateId.MediumModeSelect:
                return ExecuteCollectionModeSelect(step, state, StageDifficulty.Medium);
            case GameUiStateId.HardModeSelect:
                return ExecuteCollectionModeSelect(step, state, StageDifficulty.Hard);
            case GameUiStateId.HeroSelect:
                return await ExecuteCollectionHeroSelectAsync(step, state, cancellationToken).ConfigureAwait(false);
            case GameUiStateId.StageHint:
                return Click(step, new WpfPoint(1140, 730), "Dismissed the stage hint.");
            case GameUiStateId.StageChallengeWithHint:
                return Click(step, new WpfPoint(960, 760), "Dismissed the in-level hint overlay.");
            case GameUiStateId.StageSettings:
                return Click(step, new WpfPoint(850, 850), "Surrendered from the stage settings menu.");
            case GameUiStateId.Victory:
                return Click(step, new WpfPoint(720, 850), "Confirmed the stage victory result.");
            case GameUiStateId.StageSettlement:
                return Click(step, new WpfPoint(960, 910), "Advanced past the stage settlement screen.");
            case GameUiStateId.LevelUp:
                return Click(step, new WpfPoint(960, 980), "Confirmed the level-up prompt.");
            case GameUiStateId.Defeat:
                return await ExecuteCollectionDefeatAsync(step, snapshot, cancellationToken).ConfigureAwait(false);
            case GameUiStateId.Returnable:
                return Click(step, new WpfPoint(80, 55), "Returned from the current collection screen.");
            case GameUiStateId.ThreeChests:
                await OpenCollectionChestsAsync(
                    [new WpfPoint(660, 540), new WpfPoint(960, 540), new WpfPoint(1260, 540)],
                    2000,
                    1000,
                    cancellationToken).ConfigureAwait(false);
                return Success(step, "Opened all three collection chests.", 1000);
            case GameUiStateId.TwoChests:
                await OpenCollectionChestsAsync(
                    [new WpfPoint(810, 540), new WpfPoint(1110, 540)],
                    1000,
                    1000,
                    cancellationToken).ConfigureAwait(false);
                return Success(step, "Opened both collection chests.", 1000);
            case GameUiStateId.InstaMonkeyReward:
                return Click(step, new WpfPoint(960, 540), "Confirmed the Insta Monkey reward.");
            case GameUiStateId.ChestOpened:
                return Click(step, new WpfPoint(960, 1000), "Closed the opened-chest result overlay.");
            default:
                return new GameUiActionExecutionResult
                {
                    Succeeded = false,
                    Message = $"Collection action executor does not handle UI state '{snapshot.State}' yet.",
                    RecommendedDelayMs = step.PostActionDelayMs
                };
        }
    }

    private GameUiActionExecutionResult ExecuteCollectionMapSearch(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        var attempts = state.TryGetProperty<int>(CollectionAutoTaskStateKeys.MapSearchAttempts, out var currentAttempts)
            ? currentAttempts
            : 0;
        var searchButtonPoint = attempts >= 3
            ? new WpfPoint(1275, 45)
            : new WpfPoint(1350, 45);

        state.SetProperty(CollectionAutoTaskStateKeys.MapSearchAttempts, attempts + 1);
        return Click(step, searchButtonPoint, "Triggered collection map search.");
    }

    private GameUiActionExecutionResult ExecuteCollectionMapSearchResults(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (snapshot.Facts.TryGetValue("collectionMap", out var rawMap) && rawMap is GameMapType recognizedMap)
        {
            state.SetProperty(CollectionAutoTaskStateKeys.RecognizedMap, recognizedMap);
        }

        state.SetProperty(CollectionAutoTaskStateKeys.MapSearchAttempts, 0);
        state.SetProperty(CollectionAutoTaskStateKeys.HeroSelected, false);
        return Click(step, new WpfPoint(540, 650), "Entered the recognized collection map.");
    }

    private GameUiActionExecutionResult ExecuteCollectionDifficultySelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (!TryGetCollectionScriptContext(state, out var context))
        {
            return PressEscape(step, "Collection script metadata is unavailable. Returning from difficulty select.");
        }

        var point = context.Difficulty switch
        {
            StageDifficulty.Easy => new WpfPoint(630, 400),
            StageDifficulty.Medium => new WpfPoint(970, 400),
            StageDifficulty.Hard => new WpfPoint(1300, 400),
            _ => new WpfPoint(970, 400)
        };

        return Click(step, point, $"Selected collection difficulty '{context.Difficulty}'.");
    }

    private GameUiActionExecutionResult ExecuteCollectionModeSelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        StageDifficulty expectedDifficulty)
    {
        if (!TryGetCollectionScriptContext(state, out var context))
        {
            return PressEscape(step, "Collection script metadata is unavailable. Returning from mode select.");
        }

        if (context.Difficulty != expectedDifficulty)
        {
            return PressEscape(
                step,
                $"Resolved script difficulty '{context.Difficulty}' does not match the current mode screen '{expectedDifficulty}'.");
        }

        var heroSelected = state.TryGetProperty<bool>(CollectionAutoTaskStateKeys.HeroSelected, out var selected) && selected;
        if (!heroSelected)
        {
            return Click(step, new WpfPoint(100, 1000), "Opening hero selection before choosing the collection mode.");
        }

        return TryGetModeSelectionPoint(context.Mode, out var point)
            ? Click(step, point, $"Selected collection mode '{context.Mode}'.")
            : new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Collection mode '{context.Mode}' does not have a configured coordinate.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
    }

    private async Task<GameUiActionExecutionResult> ExecuteCollectionHeroSelectAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken)
    {
        if (!TryGetCollectionScriptContext(state, out var context))
        {
            return Click(step, new WpfPoint(80, 55), "Collection script metadata is unavailable. Returning from hero selection.");
        }

        var heroSelected = state.TryGetProperty<bool>(CollectionAutoTaskStateKeys.HeroSelected, out var selected) && selected;
        if (heroSelected)
        {
            return Click(step, new WpfPoint(80, 55), "Hero already selected. Returning from hero selection.");
        }

        if (!_gameCaptureService.TryCaptureFrame(out _, out var frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = "Failed to capture the hero selection screen.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (_navigationOcrService.TryLocateHero(frame, context.Hero, out var heroPoint))
            {
                _inputSimulationService.PrepareTargetWindowForInput();
                _inputSimulationService.ClickMouseAtScriptCoordinate(heroPoint);
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                _inputSimulationService.ClickMouseAtScriptCoordinate(new WpfPoint(1120, 620));
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                _inputSimulationService.ClickMouseAtScriptCoordinate(new WpfPoint(80, 55));
                state.SetProperty(CollectionAutoTaskStateKeys.HeroSelected, true);
                return Success(step, $"Selected hero '{context.Hero}' for the collection script.", 800);
            }
        }

        _inputSimulationService.PrepareTargetWindowForInput();
        _inputSimulationService.MoveMouseToScriptCoordinate(new WpfPoint(960, 540));
        _inputSimulationService.ScrollMouseWheelVertical(-50);
        return Success(step, $"Hero '{context.Hero}' not found yet. Scrolled to continue searching.", 600);
    }

    private async Task<GameUiActionExecutionResult> ExecuteCollectionDefeatAsync(
        GameUiNavigationStep step,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (TryGetHomeButtonPoint(snapshot, out var homeButtonPoint))
        {
            return Click(step, homeButtonPoint, "Returned to the main menu after defeat.");
        }

        if (!_gameCaptureService.TryCaptureFrame(out _, out var frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = "Failed to capture the defeat screen for home button recognition.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (_navigationOcrService.TryLocateHomeButton(frame, out var recognizedHomeButtonPoint))
            {
                return Click(step, recognizedHomeButtonPoint, "Returned to the main menu after defeat.");
            }
        }

        await Task.Yield();
        return new GameUiActionExecutionResult
        {
            Succeeded = false,
            Message = "Failed to locate the defeat home button.",
            RecommendedDelayMs = step.PostActionDelayMs
        };
    }

    private async Task OpenCollectionChestsAsync(
        IReadOnlyList<WpfPoint> chestPoints,
        int reopenDelayMs,
        int betweenChestDelayMs,
        CancellationToken cancellationToken)
    {
        _inputSimulationService.PrepareTargetWindowForInput();

        foreach (var chestPoint in chestPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inputSimulationService.ClickMouseAtScriptCoordinate(chestPoint);
            await Task.Delay(reopenDelayMs, cancellationToken).ConfigureAwait(false);
            _inputSimulationService.ClickMouseAtScriptCoordinate(chestPoint);
            await Task.Delay(betweenChestDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryGetCollectionScriptContext(
        AutoTaskRuntimeState state,
        out CollectionAutoTaskScriptContext context)
    {
        return state.TryGetProperty(CollectionAutoTaskStateKeys.ResolvedScriptContext, out context!);
    }

    private static bool TryGetHomeButtonPoint(GameUiSnapshot snapshot, out WpfPoint point)
    {
        if (snapshot.Facts.TryGetValue("homeButtonPoint1080p", out var rawPoint) && rawPoint is WpfPoint typedPoint)
        {
            point = typedPoint;
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryGetModeSelectionPoint(StageMode mode, out WpfPoint point)
    {
        switch (mode)
        {
            case StageMode.Standard:
                point = new WpfPoint(630, 590);
                return true;
            case StageMode.PrimaryOnly:
                point = new WpfPoint(960, 450);
                return true;
            case StageMode.Deflation:
                point = new WpfPoint(1300, 450);
                return true;
            case StageMode.MilitaryOnly:
                point = new WpfPoint(960, 450);
                return true;
            case StageMode.Apopalypse:
                point = new WpfPoint(1300, 450);
                return true;
            case StageMode.Reverse:
                point = new WpfPoint(960, 750);
                return true;
            case StageMode.MagicOnly:
                point = new WpfPoint(960, 450);
                return true;
            case StageMode.DoubleHpMoabs:
                point = new WpfPoint(1300, 450);
                return true;
            case StageMode.HalfCash:
                point = new WpfPoint(1600, 450);
                return true;
            case StageMode.AlternateBloonsRounds:
                point = new WpfPoint(960, 750);
                return true;
            case StageMode.Impoppable:
                point = new WpfPoint(1300, 750);
                return true;
            case StageMode.CHIMPS:
                point = new WpfPoint(1600, 750);
                return true;
            default:
                point = default;
                return false;
        }
    }

    private GameUiActionExecutionResult Click(
        GameUiNavigationStep step,
        WpfPoint scriptPoint,
        string message)
    {
        _inputSimulationService.PrepareTargetWindowForInput();
        _inputSimulationService.ClickMouseAtScriptCoordinate(scriptPoint);
        return Success(step, message, step.PostActionDelayMs);
    }

    private GameUiActionExecutionResult PressEscape(
        GameUiNavigationStep step,
        string message)
    {
        _inputSimulationService.PrepareTargetWindowForInput();
        _inputSimulationService.PressKey(KeyId.Escape);
        return Success(step, message, step.PostActionDelayMs);
    }

    private static GameUiActionExecutionResult Success(
        GameUiNavigationStep step,
        string message,
        int delayMs)
    {
        return new GameUiActionExecutionResult
        {
            Succeeded = true,
            Message = string.IsNullOrWhiteSpace(message) ? step.Description : message,
            RecommendedDelayMs = delayMs > 0 ? delayMs : step.PostActionDelayMs
        };
    }
}

internal sealed class UnimplementedGameUiElementLocator : IGameUiElementLocator
{
    private static readonly Lazy<UnimplementedGameUiElementLocator> InstanceHolder =
        new(() => new UnimplementedGameUiElementLocator());

    private UnimplementedGameUiElementLocator()
    {
    }

    public static UnimplementedGameUiElementLocator Instance => InstanceHolder.Value;

    public bool TryLocateScriptPoint(
        GameUiActionKind actionKind,
        StageEntryTarget target,
        GameUiSnapshot snapshot,
        out WpfPoint scriptPoint,
        out string failureMessage)
    {
        scriptPoint = default;
        failureMessage = $"Locator for UI action '{actionKind}' is not implemented yet.";
        return false;
    }
}
