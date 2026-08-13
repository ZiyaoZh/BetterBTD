using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal sealed class GoldBalloonGameUiActionHandler : AutoTaskGameUiActionHandlerBase
{
    public GoldBalloonGameUiActionHandler(
        ScriptInputSimulationService inputSimulationService,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
        : base(inputSimulationService, gameCaptureService, navigationOcrService)
    {
    }

    public override AutoTaskKind Kind => AutoTaskKind.GoldBalloon;

    public override async Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (snapshot.State)
        {
            case GameUiStateId.MainMenu:
                return Click(step, new WpfPoint(960, 940), "Opened gold balloon flow from the main menu.");
            case GameUiStateId.RaceResult:
                return Click(step, new WpfPoint(960, 800), "Closed the race result overlay.");
            case GameUiStateId.BossResult:
                return Click(step, new WpfPoint(960, 880), "Closed the boss result overlay.");
            case GameUiStateId.MapGrid:
                return Click(step, new WpfPoint(80, 170), "Opened map select screen.");
            case GameUiStateId.CollectionEvent:
                return Click(step, new WpfPoint(80, 55), "Returned from the current gold balloon screen.");
            case GameUiStateId.CollectionEventClaimable:
                return Click(step, new WpfPoint(960, 680), "Opened the claimable gold balloon chest.");
            case GameUiStateId.MapSearch:
                return ExecuteMapSearch(step, state, snapshot);
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
            case GameUiStateId.StageSettings:
                return Click(step, new WpfPoint(850, 850), "Surrendered from the stage settings menu.");
            case GameUiStateId.Victory:
                return Click(step, new WpfPoint(720, 850), "Confirmed the stage victory result.");
            case GameUiStateId.StageSettlement:
                return Click(step, new WpfPoint(960, 910), "Advanced past the stage settlement screen.");
            case GameUiStateId.LevelUp:
                return Click(step, new WpfPoint(960, 980), "Confirmed the level-up prompt.");
            case GameUiStateId.Defeat:
                return await ExecuteDefeatReturnAsync(step, snapshot, cancellationToken).ConfigureAwait(false);
            case GameUiStateId.Returnable:
                return Click(step, new WpfPoint(80, 55), "Returned from the current gold balloon screen.");
            case GameUiStateId.ThreeChests:
                await OpenChestsAsync(
                    [new WpfPoint(660, 540), new WpfPoint(960, 540), new WpfPoint(1260, 540)],
                    2000,
                    1000,
                    cancellationToken).ConfigureAwait(false);
                return Success(step, "Opened all three gold balloon chests.", 1000);
            case GameUiStateId.TwoChests:
                await OpenChestsAsync(
                    [new WpfPoint(810, 540), new WpfPoint(1110, 540)],
                    1000,
                    1000,
                    cancellationToken).ConfigureAwait(false);
                return Success(step, "Opened both gold balloon chests.", 1000);
            case GameUiStateId.InstaMonkeyReward:
                return Click(step, new WpfPoint(960, 540), "Confirmed the Insta Monkey reward.");
            case GameUiStateId.ChestOpened:
                return Click(step, new WpfPoint(960, 1000), "Closed the opened-chest result overlay.");
            default:
                return new GameUiActionExecutionResult
                {
                    Succeeded = false,
                    Message = $"Gold balloon action executor does not handle UI state '{snapshot.State}' yet.",
                    RecommendedDelayMs = step.PostActionDelayMs
                };
        }
    }

    private GameUiActionExecutionResult ExecuteMapSearch(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (MapSearchFlowState.IsResultReady(
                state,
                snapshot,
                GoldBalloonAutoTaskStateKeys.MapSearchPixelBaseline,
                GoldBalloonAutoTaskStateKeys.MapSearchPixelChangedSince) &&
            snapshot.Facts.TryGetValue(MapSearchFlowState.GoldBalloonMapFact, out var rawMap) &&
            rawMap is GameMapType recognizedMap)
        {
            state.SetProperty(GoldBalloonAutoTaskStateKeys.RecognizedMap, recognizedMap);
            state.SetProperty(GoldBalloonAutoTaskStateKeys.MapSearchAttempts, 0);
            state.SetProperty(GoldBalloonAutoTaskStateKeys.HeroSelected, false);
            return Click(step, new WpfPoint(540, 650), "Entered the recognized gold balloon map.");
        }

        var attempts = state.TryGetProperty<int>(GoldBalloonAutoTaskStateKeys.MapSearchAttempts, out var currentAttempts)
            ? currentAttempts
            : 0;
        var searchButtonPoint = attempts >= 3
            ? new WpfPoint(1275, 45)
            : new WpfPoint(1350, 45);

        MapSearchFlowState.CapturePixelBaseline(
            state,
            snapshot,
            GoldBalloonAutoTaskStateKeys.MapSearchPixelBaseline);
        state.SetProperty(GoldBalloonAutoTaskStateKeys.MapSearchAttempts, attempts + 1);
        return Click(step, searchButtonPoint, "Triggered gold balloon map search.");
    }

    private GameUiActionExecutionResult ExecuteDifficultySelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Gold balloon script metadata is unavailable. Returning from difficulty select.");
        }

        var point = context.Difficulty switch
        {
            StageDifficulty.Easy => new WpfPoint(630, 400),
            StageDifficulty.Medium => new WpfPoint(970, 400),
            StageDifficulty.Hard => new WpfPoint(1300, 400),
            _ => new WpfPoint(970, 400)
        };

        return Click(step, point, $"Selected gold balloon difficulty '{context.Difficulty}'.");
    }

    private GameUiActionExecutionResult ExecuteModeSelect(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        StageDifficulty expectedDifficulty)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return PressEscape(step, "Gold balloon script metadata is unavailable. Returning from mode select.");
        }

        if (context.Difficulty != expectedDifficulty)
        {
            return PressEscape(
                step,
                $"Resolved script difficulty '{context.Difficulty}' does not match the current mode screen '{expectedDifficulty}'.");
        }

        var heroSelected = state.TryGetProperty<bool>(GoldBalloonAutoTaskStateKeys.HeroSelected, out var selected) && selected;
        if (!heroSelected)
        {
            return Click(step, new WpfPoint(100, 1000), "Opening hero selection before choosing the gold balloon mode.");
        }

        return TryGetModeSelectionPoint(context.Mode, out var point)
            ? Click(step, point, $"Selected gold balloon mode '{context.Mode}'.")
            : new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Gold balloon mode '{context.Mode}' does not have a configured coordinate.",
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
            return Click(step, new WpfPoint(80, 55), "Gold balloon script metadata is unavailable. Returning from hero selection.");
        }

        var heroSelected = state.TryGetProperty<bool>(GoldBalloonAutoTaskStateKeys.HeroSelected, out var selected) && selected;
        if (heroSelected)
        {
            return Click(step, new WpfPoint(80, 55), "Hero already selected. Returning from hero selection.");
        }

        return await ExecuteHeroSelectionAsync(
                step,
                context.Hero,
                () => state.SetProperty(GoldBalloonAutoTaskStateKeys.HeroSelected, true),
                $"Selected hero '{context.Hero}' for the gold balloon script.",
                $"Hero '{context.Hero}' not found yet. Scrolled to continue searching.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryGetScriptContext(
        AutoTaskRuntimeState state,
        out GoldBalloonAutoTaskScriptContext context)
    {
        return state.TryGetProperty(GoldBalloonAutoTaskStateKeys.ResolvedScriptContext, out context!);
    }
}
