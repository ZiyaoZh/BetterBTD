using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using OpenCvRect = OpenCvSharp.Rect;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal sealed class BlackBorderGameUiActionHandler : AutoTaskGameUiActionHandlerBase
{
    private static readonly OpenCvRect BlackBorderMapGridReferenceRegion = new(150, 220, 1620, 620);

    public BlackBorderGameUiActionHandler(
        ScriptInputSimulationService inputSimulationService,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
        : base(inputSimulationService, gameCaptureService, navigationOcrService)
    {
    }

    public override AutoTaskKind Kind => AutoTaskKind.BlackBorder;

    public override async Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (snapshot.State)
        {
            case GameUiStateId.Unknown:
                return Success(step, "Black border UI state is unknown. No action taken.", step.PostActionDelayMs);
            case GameUiStateId.MainMenu:
                return Click(step, new WpfPoint(960, 940), "Opened black border map selection from the main menu.");
            case GameUiStateId.MapCategorySelect:
            case GameUiStateId.MapGrid:
                return await ExecuteMapSelectionAsync(step, state, cancellationToken).ConfigureAwait(false);
            case GameUiStateId.DifficultySelect:
                return ExecuteDifficultySelect(step, state);
            case GameUiStateId.EasyModeSelect:
                return ExecuteModeSelect(step, state, StageDifficulty.Easy);
            case GameUiStateId.MediumModeSelect:
                return ExecuteModeSelect(step, state, StageDifficulty.Medium);
            case GameUiStateId.HardModeSelect:
                return ExecuteModeSelect(step, state, StageDifficulty.Hard);
            case GameUiStateId.HeroSelect:
                return await ExecuteHeroSelectAsync(step, state, cancellationToken).ConfigureAwait(false);
            case GameUiStateId.StageHint:
                return Click(step, new WpfPoint(1140, 730), "Dismissed the stage hint.");
            case GameUiStateId.StageChallengeWithHint:
                return Click(step, new WpfPoint(960, 760), "Dismissed the in-level hint overlay.");
            case GameUiStateId.InLevel:
                return ExecuteInLevel(step, state);
            case GameUiStateId.StageSettings:
                return ExecuteStageSettings(step, state);
            case GameUiStateId.Victory:
                return Click(step, new WpfPoint(720, 850), "Confirmed the black border stage victory result.");
            case GameUiStateId.StageSettlement:
                return Click(step, new WpfPoint(960, 910), "Advanced past the stage settlement screen.");
            case GameUiStateId.LevelUp:
                return Click(step, new WpfPoint(960, 980), "Confirmed the level-up prompt.");
            case GameUiStateId.Defeat:
                return await ExecuteDefeatReturnAsync(step, snapshot, cancellationToken).ConfigureAwait(false);
            case GameUiStateId.Returnable:
                return Click(step, new WpfPoint(80, 55), "Returned from the current screen.");
            case GameUiStateId.RaceResult:
                return Click(step, new WpfPoint(960, 800), "Closed the race result overlay.");
            case GameUiStateId.BossResult:
                return Click(step, new WpfPoint(960, 880), "Closed the boss result overlay.");
            case GameUiStateId.CollectionEventClaimable:
                return Click(step, new WpfPoint(960, 680), "Opened the claimable reward chest.");
            case GameUiStateId.ThreeChests:
                await OpenChestsAsync(
                    [new WpfPoint(660, 540), new WpfPoint(960, 540), new WpfPoint(1260, 540)],
                    2000,
                    1000,
                    cancellationToken).ConfigureAwait(false);
                return Success(step, "Opened all three reward chests.", 1000);
            case GameUiStateId.TwoChests:
                await OpenChestsAsync(
                    [new WpfPoint(810, 540), new WpfPoint(1110, 540)],
                    1000,
                    1000,
                    cancellationToken).ConfigureAwait(false);
                return Success(step, "Opened both reward chests.", 1000);
            case GameUiStateId.InstaMonkeyReward:
                return Click(step, new WpfPoint(960, 540), "Confirmed the Insta Monkey reward.");
            case GameUiStateId.ChestOpened:
                return Click(step, new WpfPoint(960, 1000), "Closed the opened-chest result overlay.");
            case GameUiStateId.Loading:
            case GameUiStateId.ModeSelect:
                return Success(step, step.Description, step.PostActionDelayMs);
            default:
                return new GameUiActionExecutionResult
                {
                    Succeeded = false,
                    Message = $"Black border action executor does not handle UI state '{snapshot.State}' yet.",
                    RecommendedDelayMs = step.PostActionDelayMs
                };
        }
    }

    private async Task<GameUiActionExecutionResult> ExecuteMapSelectionAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Black border script metadata is unavailable. Returning from map selection.");
        }

        var categoryPoint = GetCategorySelectionPoint(context.Category);
        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.ClickMouseAtScriptCoordinate(categoryPoint);
        await Task.Delay(400, cancellationToken).ConfigureAwait(false);

        if (!GameCaptureService.TryCaptureFrame(out _, out var frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = "Failed to capture the black border map selection screen.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (TryLocateMap(frame, context.Target.Map, out var mapPoint))
            {
                if (BlackBorderBadgeDetection.TryIsStageBadgeAcquired(frame, context.Target, mapPoint, out var isAcquired) &&
                    isAcquired)
                {
                    state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, 0);
                    state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, false);
                    state.SetProperty(BlackBorderAutoTaskStateKeys.SkipCurrentTaskRequested, true);
                    return Success(
                        step,
                        $"Black border badge for '{context.Target.Map}/{context.Target.Difficulty}/{context.Target.Mode}' is already acquired. Skipping the current queued stage.",
                        600);
                }

                InputSimulationService.PrepareTargetWindowForInput();
                InputSimulationService.ClickMouseAtScriptCoordinate(mapPoint);
                state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, 0);
                state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, false);
                return Success(step, $"Selected black border map '{context.Target.Map}'.", 800);
            }
        }

        var attempts = state.TryGetProperty<int>(BlackBorderAutoTaskStateKeys.MapLocateAttempts, out var currentAttempts)
            ? currentAttempts + 1
            : 1;

        if (attempts > 10)
        {
            state.RemoveProperty(BlackBorderAutoTaskStateKeys.ResolvedScriptContext);
            state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, 0);
            state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, false);
            state.SetProperty(BlackBorderAutoTaskStateKeys.SkipCurrentTaskRequested, true);
            return Success(
                step,
                $"Black border map '{context.Target.Map}' was not found after 10 attempts. Skipping the current queued stage.",
                600);
        }

        state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, attempts);
        return Success(
            step,
            $"Black border map '{context.Target.Map}' was not found yet. Retrying map selection ({attempts}/10).",
            600);
    }

    private GameUiActionExecutionResult ExecuteDifficultySelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Black border script metadata is unavailable. Returning from difficulty select.");
        }

        var point = context.Target.Difficulty switch
        {
            StageDifficulty.Easy => new WpfPoint(630, 400),
            StageDifficulty.Medium => new WpfPoint(970, 400),
            StageDifficulty.Hard => new WpfPoint(1300, 400),
            _ => new WpfPoint(970, 400)
        };

        return Click(step, point, $"Selected black border difficulty '{context.Target.Difficulty}'.");
    }

    private GameUiActionExecutionResult ExecuteModeSelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        StageDifficulty expectedDifficulty)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Black border script metadata is unavailable. Returning from mode select.");
        }

        if (context.Target.Difficulty != expectedDifficulty)
        {
            return PressEscape(
                step,
                $"Queued black border difficulty '{context.Target.Difficulty}' does not match the current mode screen '{expectedDifficulty}'.");
        }

        var heroSelected = state.TryGetProperty<bool>(BlackBorderAutoTaskStateKeys.HeroSelected, out var selected) && selected;
        if (!heroSelected)
        {
            return Click(step, new WpfPoint(100, 1000), "Opening hero selection before choosing the black border mode.");
        }

        return TryGetModeSelectionPoint(context.Target.Mode, out var point)
            ? Click(step, point, $"Selected black border mode '{context.Target.Mode}'.")
            : new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Black border mode '{context.Target.Mode}' does not have a configured coordinate.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
    }

    private async Task<GameUiActionExecutionResult> ExecuteHeroSelectAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return Click(step, new WpfPoint(80, 55), "Black border script metadata is unavailable. Returning from hero selection.");
        }

        var heroSelected = state.TryGetProperty<bool>(BlackBorderAutoTaskStateKeys.HeroSelected, out var selected) && selected;
        if (heroSelected)
        {
            return Click(step, new WpfPoint(80, 55), "Hero already selected. Returning from hero selection.");
        }

        return await ExecuteHeroSelectionAsync(
                step,
                context.Hero,
                () => state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, true),
                $"Selected hero '{context.Hero}' for the black border script.",
                $"Hero '{context.Hero}' not found yet. Scrolled to continue searching.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private GameUiActionExecutionResult ExecuteInLevel(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        var hasContext = TryGetScriptContext(state, out _);
        var runState = GetScriptRunState(state);
        if (hasContext && runState == BlackBorderAutoTaskScriptRunState.Running)
        {
            return Success(step, "Black border script is active inside the stage.", step.PostActionDelayMs);
        }

        return Click(
            step,
            new WpfPoint(1600, 40),
            "No black border script is active inside the stage. Opened the stage menu to surrender.");
    }

    private GameUiActionExecutionResult ExecuteStageSettings(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        var hasContext = TryGetScriptContext(state, out _);
        var runState = GetScriptRunState(state);
        if (hasContext && runState == BlackBorderAutoTaskScriptRunState.Running)
        {
            return Success(step, "Stage settings screen detected while the black border script is active. Holding position.", step.PostActionDelayMs);
        }

        return Click(step, new WpfPoint(850, 850), "Surrendered from the current black border stage.");
    }

    private bool TryLocateMap(
        OpenCvSharp.Mat frame,
        GameMapType map,
        out WpfPoint point)
    {
        var mapGridRegion = ScaleReferenceRect(BlackBorderMapGridReferenceRegion, frame.Width, frame.Height);
        using var captureRegion = new OpenCvSharp.Mat(frame, mapGridRegion);
        return NavigationOcrService.TryLocateMap(
            captureRegion,
            map,
            frame.Width,
            frame.Height,
            mapGridRegion.X,
            mapGridRegion.Y,
            out point,
            out _);
    }

    private static WpfPoint GetCategorySelectionPoint(BlackBorderMapCategory category)
    {
        return category switch
        {
            BlackBorderMapCategory.Beginner => new WpfPoint(590, 980),
            BlackBorderMapCategory.Intermediate => new WpfPoint(840, 980),
            BlackBorderMapCategory.Advanced => new WpfPoint(1090, 980),
            BlackBorderMapCategory.Expert => new WpfPoint(1340, 980),
            _ => new WpfPoint(590, 980)
        };
    }

    private static bool TryGetScriptContext(
        AutoTaskRuntimeState state,
        out BlackBorderAutoTaskScriptContext context)
    {
        return state.TryGetProperty(BlackBorderAutoTaskStateKeys.ResolvedScriptContext, out context!);
    }

    private static BlackBorderAutoTaskScriptRunState GetScriptRunState(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<BlackBorderAutoTaskScriptRunState>(
            BlackBorderAutoTaskStateKeys.ScriptRunState,
            out var runState)
            ? runState
            : BlackBorderAutoTaskScriptRunState.NotStarted;
    }
}
