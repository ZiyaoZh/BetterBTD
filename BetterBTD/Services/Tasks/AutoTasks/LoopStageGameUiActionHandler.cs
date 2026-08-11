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

internal sealed class LoopStageGameUiActionHandler : AutoTaskGameUiActionHandlerBase
{
    private static readonly OpenCvRect MapGridReferenceRegion = new(150, 220, 1620, 620);
    private const int MapCategoryClickCaptureDelayMs = 500;

    public LoopStageGameUiActionHandler(
        ScriptInputSimulationService inputSimulationService,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
        : base(inputSimulationService, gameCaptureService, navigationOcrService)
    {
    }

    public override AutoTaskKind Kind => AutoTaskKind.LoopStage;

    public override async Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return snapshot.State switch
        {
            GameUiStateId.Unknown => Success(step, "Loop-stage UI state is unknown. No action taken.", step.PostActionDelayMs),
            GameUiStateId.MainMenu => Click(step, new WpfPoint(960, 940), "Opened loop-stage map selection from the main menu."),
            GameUiStateId.MapCategorySelect or GameUiStateId.MapGrid =>
                await ExecuteMapSelectionAsync(step, state, cancellationToken).ConfigureAwait(false),
            GameUiStateId.DifficultySelect => ExecuteDifficultySelect(step, state),
            GameUiStateId.EasyModeSelect => ExecuteModeSelect(step, state, StageDifficulty.Easy),
            GameUiStateId.MediumModeSelect => ExecuteModeSelect(step, state, StageDifficulty.Medium),
            GameUiStateId.HardModeSelect => ExecuteModeSelect(step, state, StageDifficulty.Hard),
            GameUiStateId.HeroSelect => await ExecuteHeroSelectAsync(step, state, cancellationToken).ConfigureAwait(false),
            GameUiStateId.StageHint =>
                Click(step, new WpfPoint(1140, 730), "Dismissed the stage hint."),
            GameUiStateId.StageChallengeWithHint =>
                DismissStageChallengeWithHint(step, state),
            GameUiStateId.InLevel => ExecuteInLevel(step, state),
            GameUiStateId.StageSettings => ExecuteStageSettings(step, state),
            GameUiStateId.StageSettlement => Click(step, new WpfPoint(964, 910), "Advanced past the stage settlement screen."),
            GameUiStateId.Victory => await ExecuteVictoryAsync(step, state, snapshot, cancellationToken).ConfigureAwait(false),
            GameUiStateId.FreeplayPrompt => ConfirmFreeplayPrompt(step, state),
            GameUiStateId.ConfirmDialog => ConfirmFreeplayPrompt(step, state),
            GameUiStateId.LevelUp => Click(step, new WpfPoint(960, 980), "Confirmed the level-up prompt."),
            GameUiStateId.InstaMonkeyReward => Click(step, new WpfPoint(960, 540), "Confirmed the Insta Monkey reward."),
            GameUiStateId.Defeat => await ExecuteDefeatReturnAsync(step, snapshot, cancellationToken).ConfigureAwait(false),
            GameUiStateId.Reward or GameUiStateId.ChestOpened =>
                Click(step, new WpfPoint(960, 1000), "Dismissed the reward overlay."),
            GameUiStateId.TwoChests => await OpenChestsAndReturnAsync(
                step,
                [new WpfPoint(810, 540), new WpfPoint(1110, 540)],
                cancellationToken).ConfigureAwait(false),
            GameUiStateId.ThreeChests => await OpenChestsAndReturnAsync(
                step,
                [new WpfPoint(660, 540), new WpfPoint(960, 540), new WpfPoint(1260, 540)],
                cancellationToken).ConfigureAwait(false),
            GameUiStateId.Loading or GameUiStateId.ModeSelect =>
                Success(step, step.Description, step.PostActionDelayMs),
            _ => new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Loop-stage action executor does not handle UI state '{snapshot.State}' yet.",
                RecommendedDelayMs = step.PostActionDelayMs
            }
        };
    }

    private async Task<GameUiActionExecutionResult> ExecuteMapSelectionAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Loop-stage script metadata is unavailable. Returning from map selection.");
        }

        if (!GameCaptureService.TryCaptureFrame(out _, out var frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = "Failed to capture the loop-stage map selection screen.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (TrySelectMap(step, context.Target.Map, frame, out var result))
            {
                return result;
            }
        }

        var categoryPoint = GetCategorySelectionPoint(context.Category);
        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.ClickMouseAtScriptCoordinate(categoryPoint);
        await Task.Delay(MapCategoryClickCaptureDelayMs, cancellationToken).ConfigureAwait(false);

        if (!GameCaptureService.TryCaptureFrame(out _, out frame))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = "Failed to capture the loop-stage map selection screen after selecting the map category.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        using (frame)
        {
            if (TrySelectMap(step, context.Target.Map, frame, out var result))
            {
                return result;
            }
        }

        var attempts = state.TryGetProperty<int>(
            LoopStageAutoTaskStateKeys.MapLocateAttempts,
            out var currentAttempts)
            ? currentAttempts + 1
            : 1;
        state.SetProperty(LoopStageAutoTaskStateKeys.MapLocateAttempts, attempts);
        if (attempts >= 10)
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Loop-stage map '{context.Target.Map}' was not found after 10 attempts.",
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        return Success(
            step,
            $"Loop-stage map '{context.Target.Map}' was not found yet. Retrying map selection ({attempts}/10).",
            600);
    }

    private bool TrySelectMap(
        GameUiNavigationStep step,
        GameMapType map,
        OpenCvSharp.Mat frame,
        out GameUiActionExecutionResult result)
    {
        result = null!;
        var mapGridRegion = ScaleReferenceRect(MapGridReferenceRegion, frame.Width, frame.Height);
        using var captureRegion = new OpenCvSharp.Mat(frame, mapGridRegion);
        if (!NavigationOcrService.TryLocateMap(
                captureRegion,
                map,
                frame.Width,
                frame.Height,
                mapGridRegion.X,
                mapGridRegion.Y,
                out var mapPoint,
                out _))
        {
            return false;
        }

        InputSimulationService.PrepareTargetWindowForInput();
        InputSimulationService.ClickMouseAtScriptCoordinate(mapPoint);
        result = Success(step, $"Selected loop-stage map '{map}'.", 800);
        return true;
    }

    private GameUiActionExecutionResult ExecuteDifficultySelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Loop-stage script metadata is unavailable. Returning from difficulty select.");
        }

        var point = context.Target.Difficulty switch
        {
            StageDifficulty.Easy => new WpfPoint(630, 400),
            StageDifficulty.Medium => new WpfPoint(970, 400),
            StageDifficulty.Hard => new WpfPoint(1300, 400),
            _ => new WpfPoint(970, 400)
        };

        return Click(step, point, $"Selected loop-stage difficulty '{context.Target.Difficulty}'.");
    }

    private GameUiActionExecutionResult ExecuteModeSelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        StageDifficulty expectedDifficulty)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Loop-stage script metadata is unavailable. Returning from mode select.");
        }

        if (context.Target.Difficulty != expectedDifficulty)
        {
            return PressEscape(step, "The configured difficulty does not match the current mode screen.");
        }

        var heroSelected = state.TryGetProperty<bool>(
            LoopStageAutoTaskStateKeys.HeroSelected,
            out var selected) && selected;
        if (!heroSelected)
        {
            return Click(step, new WpfPoint(100, 1000), "Opening hero selection before choosing the loop-stage mode.");
        }

        return TryGetModeSelectionPoint(context.Target.Mode, out var point)
            ? Click(step, point, $"Selected loop-stage mode '{context.Target.Mode}'.")
            : new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Loop-stage mode '{context.Target.Mode}' does not have a configured coordinate.",
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
            return Click(step, new WpfPoint(80, 55), "Loop-stage script metadata is unavailable. Returning from hero selection.");
        }

        var heroSelected = state.TryGetProperty<bool>(
            LoopStageAutoTaskStateKeys.HeroSelected,
            out var selected) && selected;
        if (heroSelected)
        {
            return Click(step, new WpfPoint(80, 55), "Hero already selected. Returning from hero selection.");
        }

        return await ExecuteHeroSelectionAsync(
            step,
            context.Hero,
            () => state.SetProperty(LoopStageAutoTaskStateKeys.HeroSelected, true),
            $"Selected hero '{context.Hero}' for the loop-stage script.",
            $"Hero '{context.Hero}' not found yet. Scrolled to continue searching.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GameUiActionExecutionResult> ExecuteVictoryAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound)
        {
            return Click(step, new WpfPoint(1202, 850), "Selected freeplay from the victory screen.");
        }

        return await ExecuteHomeButtonClickAsync(
            step,
            snapshot,
            "loop-stage victory screen",
            "Returned to the main menu from the loop-stage victory screen.",
            cancellationToken).ConfigureAwait(false);
    }

    private GameUiActionExecutionResult ExecuteInLevel(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        var runState = GetScriptRunState(state);
        if (runState is LoopStageScriptRunState.WaitingForExit or LoopStageScriptRunState.FinishedCurrentStage)
        {
            return Click(step, new WpfPoint(1600, 46), "Opened the in-level settings menu to exit the stage.");
        }

        return Success(step, "Loop-stage script is active inside the stage.", step.PostActionDelayMs);
    }

    private GameUiActionExecutionResult ExecuteStageSettings(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        var runState = GetScriptRunState(state);
        if (runState is LoopStageScriptRunState.WaitingForExit or LoopStageScriptRunState.FinishedCurrentStage)
        {
            return Click(step, new WpfPoint(850, 850), "Exited the current loop-stage game.");
        }

        return Success(step, "Stage settings detected while the loop-stage script is active. Holding position.", step.PostActionDelayMs);
    }

    private GameUiActionExecutionResult ConfirmFreeplayPrompt(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound &&
            state.TryGetProperty<LoopStageScriptRunState>(
                LoopStageAutoTaskStateKeys.ScriptRunState,
                out var runState) &&
            runState == LoopStageScriptRunState.WaitingForFreeplayPrompt)
        {
            state.SetProperty(LoopStageAutoTaskStateKeys.FreeplayPromptConfirmed, true);
        }

        return Click(step, new WpfPoint(959, 757), "Confirmed the freeplay prompt.");
    }

    private GameUiActionExecutionResult DismissStageChallengeWithHint(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound &&
            state.TryGetProperty<LoopStageScriptRunState>(
                LoopStageAutoTaskStateKeys.ScriptRunState,
                out var runState) &&
            runState == LoopStageScriptRunState.WaitingForFreeplayPrompt)
        {
            state.SetProperty(LoopStageAutoTaskStateKeys.FreeplayPromptConfirmed, true);
        }

        return Click(step, new WpfPoint(960, 760), "Dismissed the in-level hint overlay.");
    }

    private async Task<GameUiActionExecutionResult> OpenChestsAndReturnAsync(
        GameUiNavigationStep step,
        IReadOnlyList<WpfPoint> chestPoints,
        CancellationToken cancellationToken)
    {
        await OpenChestsAsync(chestPoints, 1000, 1000, cancellationToken).ConfigureAwait(false);
        return Success(step, "Opened the reward chests.", 1000);
    }

    private static bool TryGetScriptContext(
        AutoTaskRuntimeState state,
        out LoopStageAutoTaskScriptContext context)
    {
        return state.TryGetProperty(LoopStageAutoTaskStateKeys.ResolvedScriptContext, out context!);
    }

    private static LoopStageScriptRunState GetScriptRunState(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<LoopStageScriptRunState>(
            LoopStageAutoTaskStateKeys.ScriptRunState,
            out var runState)
            ? runState
            : LoopStageScriptRunState.NotStarted;
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
}
