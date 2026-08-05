using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models;
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
    private static readonly OpenCvRect[] BlackBorderVisibleMapSlotReferenceRegions =
    [
        new(150, 220, 540, 310),
        new(690, 220, 540, 310),
        new(1230, 220, 540, 310),
        new(150, 530, 540, 310),
        new(690, 530, 540, 310),
        new(1230, 530, 540, 310)
    ];

    private static readonly WpfPoint MapSelectionScrollPoint = new(960, 540);

    private const int MapCategoryClickCaptureDelayMs = 500;
    private const int MapSelectionNextPageScrollDelta = -5;
    private const double MapSelectionClickYOffset1080p = -60d;

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
                return ExecuteMapSelection(step, state);
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
                return await ExecuteHomeButtonClickAsync(
                        step,
                        snapshot,
                        "black border victory screen",
                        "Returned to the main menu from the black border victory screen.",
                        cancellationToken)
                    .ConfigureAwait(false);
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

    private GameUiActionExecutionResult ExecuteMapSelection(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Black border script metadata is unavailable. Returning from map selection.");
        }

        BlackBorderMapSearchStateMachine.EnsureCurrentContext(state, context);

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
            if (TryBuildMapSelectionResult(step, state, context, frame, out var locatedResult))
            {
                return locatedResult;
            }

            if (!BlackBorderMapSearchStateMachine.IsCategorySelected(state))
            {
                var categoryPoint = GetCategorySelectionPoint(context.Category);
                InputSimulationService.PrepareTargetWindowForInput();
                InputSimulationService.ClickMouseAtScriptCoordinate(categoryPoint);
                BlackBorderMapSearchStateMachine.MarkCategorySelected(state);
                return Success(
                    step,
                    $"Selected black border map category '{context.Category}' before searching for '{context.Target.Map}'.",
                    MapCategoryClickCaptureDelayMs);
            }

            var missDecision = BlackBorderMapSearchStateMachine.CreateMissDecision(state);

            if (missDecision.IsTerminal)
            {
                BlackBorderMapSearchStateMachine.MarkSearchExhausted(state);
                return Success(
                    step,
                    $"Black border map '{context.Target.Map}' was not found after scanning {BlackBorderMapSearchStateMachine.MaxPages} page(s). Skipping the current queued stage.",
                    600);
            }

            InputSimulationService.PrepareTargetWindowForInput();
            InputSimulationService.MoveMouseToScriptCoordinate(MapSelectionScrollPoint);
            InputSimulationService.ScrollMouseWheelVertical(MapSelectionNextPageScrollDelta);
            BlackBorderMapSearchStateMachine.MarkPageAdvanced(state, missDecision);
            return Success(
                step,
                $"Black border map '{context.Target.Map}' was not found on page {missDecision.PageIndex}. Advanced to page {missDecision.NextPageIndex} ({missDecision.Attempts}/{BlackBorderMapSearchStateMachine.MaxPages}).",
                700);
        }
    }

    private bool TryBuildMapSelectionResult(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        BlackBorderAutoTaskScriptContext context,
        OpenCvSharp.Mat frame,
        out GameUiActionExecutionResult result)
    {
        result = null!;

        if (!TryLocateMapInVisibleSlots(frame, context.Target.Map, out var mapPoint, out var matchInfo, out var slotIndex))
        {
            return false;
        }

        if (state.Request.Kind == AutoTaskKind.BlackBorder &&
            BlackBorderBadgeDetection.TryIsStageBadgeAcquired(frame, context.Target, mapPoint, out var isAcquired) &&
            isAcquired)
        {
            state.SetProperty(BlackBorderAutoTaskStateKeys.SkipCurrentTaskRequested, true);
            var currentPageIndex = BlackBorderMapSearchStateMachine.MarkMapFound(state);
            result = Success(
                step,
                $"Black border badge for '{context.Target.Map}/{context.Target.Difficulty}/{context.Target.Mode}' is already acquired. Page {currentPageIndex}, slot {slotIndex + 1}, score {FormatScore(matchInfo.Score)}. Skipping the current queued stage.",
                600);
            return true;
        }

        var selectedPageIndex = BlackBorderMapSearchStateMachine.GetCurrentPageIndex(state);
        var clickPoint = ApplyMapSelectionClickOffset(mapPoint);
        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.ClickMouseAtScriptCoordinate(clickPoint);
        BlackBorderMapSearchStateMachine.MarkMapFound(state);
        result = Success(
            step,
            $"Selected black border map '{context.Target.Map}' on page {selectedPageIndex}, slot {slotIndex + 1}, score {FormatScore(matchInfo.Score)}, click ({Math.Round(clickPoint.X)}, {Math.Round(clickPoint.Y)}).",
            800);
        return true;
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

    private bool TryLocateMapInVisibleSlots(
        OpenCvSharp.Mat frame,
        GameMapType map,
        out WpfPoint point,
        out TemplateMatchInfo matchInfo,
        out int slotIndex)
    {
        point = default;
        matchInfo = default;
        slotIndex = -1;

        var bestPoint = default(WpfPoint);
        var bestMatch = default(TemplateMatchInfo);
        var bestSlotIndex = -1;
        var foundAny = false;

        for (var index = 0; index < BlackBorderVisibleMapSlotReferenceRegions.Length; index++)
        {
            var slotRegion = ScaleReferenceRect(BlackBorderVisibleMapSlotReferenceRegions[index], frame.Width, frame.Height);
            using var captureRegion = new OpenCvSharp.Mat(frame, slotRegion);
            if (!NavigationOcrService.TryLocateMap(
                    captureRegion,
                    map,
                    frame.Width,
                    frame.Height,
                    slotRegion.X,
                    slotRegion.Y,
                    out var candidatePoint,
                    out var candidateMatch))
            {
                continue;
            }

            if (!foundAny || candidateMatch.Score > bestMatch.Score)
            {
                bestPoint = candidatePoint;
                bestMatch = candidateMatch;
                bestSlotIndex = index;
                foundAny = true;
            }
        }

        if (!foundAny)
        {
            return false;
        }

        point = bestPoint;
        matchInfo = bestMatch;
        slotIndex = bestSlotIndex;
        return true;
    }

    private static WpfPoint ApplyMapSelectionClickOffset(WpfPoint point)
    {
        return new WpfPoint(point.X, Math.Max(0d, point.Y + MapSelectionClickYOffset1080p));
    }

    private static string FormatScore(double score)
    {
        return score.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
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
