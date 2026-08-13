using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services.MyScripts;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Core.AutoTasks.Strategies;

public sealed class GoldBalloonAutoTaskStrategy : IAutoTaskStrategy
{
    private const int DefaultWaitDelayMs = 500;

    private readonly ManagedAutoTaskScriptResolver _scriptResolver;
    private readonly ScriptDocumentService _scriptDocumentService;

    public GoldBalloonAutoTaskStrategy()
        : this(ManagedAutoTaskScriptResolver.Instance, ScriptDocumentService.Instance)
    {
    }

    internal GoldBalloonAutoTaskStrategy(
        ManagedAutoTaskScriptResolver scriptResolver,
        ScriptDocumentService scriptDocumentService)
    {
        _scriptResolver = scriptResolver ?? throw new ArgumentNullException(nameof(scriptResolver));
        _scriptDocumentService = scriptDocumentService ?? throw new ArgumentNullException(nameof(scriptDocumentService));
    }

    public AutoTaskKind Kind => AutoTaskKind.GoldBalloon;

    public async Task<AutoTaskDecision> DecideNextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.State is not (GameUiStateId.Unknown or GameUiStateId.MapSearch))
        {
            state.RemoveProperty(GoldBalloonAutoTaskStateKeys.MapSearchPixelBaseline);
            state.RemoveProperty(GoldBalloonAutoTaskStateKeys.MapSearchPixelChangedSince);
        }

        ResetScriptLifecycleForNextStageIfNeeded(state, snapshot);

        if (state.HasPendingScriptOutcome)
        {
            return DecideAfterScriptExecution(state, snapshot);
        }

        if (snapshot.State == GameUiStateId.MapSearch && IsMapSearchResultReady(state, snapshot))
        {
            var preloadDecision = await TryPreloadScriptContextAsync(state, snapshot, cancellationToken).ConfigureAwait(false);
            if (preloadDecision is not null)
            {
                return preloadDecision;
            }
        }
        else if (snapshot.State == GameUiStateId.MapSearch &&
                 MapSearchFlowState.IsResultConfirmationInProgress(
                     state,
                     snapshot,
                     GoldBalloonAutoTaskStateKeys.MapSearchPixelBaseline))
        {
            return AutoTaskDecision.Wait(
                "Waiting for gold balloon map search results to remain stable.",
                DefaultWaitDelayMs,
                AutoTaskPhase.NavigatingToStage);
        }

        return snapshot.State switch
        {
            GameUiStateId.InLevel => TryBuildStartScriptDecision(state),
            GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Waiting for the gold balloon stage to finish loading.",
                DefaultWaitDelayMs,
                AutoTaskPhase.WaitingForLevelLoad),
            GameUiStateId.MainMenu => AutoTaskDecision.Navigate(
                "Open the gold balloon flow from the main menu.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.PreparingStage
                    : AutoTaskPhase.NavigatingToStage),
            _ => AutoTaskDecision.Navigate(
                "Advance the gold balloon navigation flow.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.AdvancingObjective
                    : AutoTaskPhase.NavigatingToStage)
        };
    }

    private static AutoTaskDecision TryBuildStartScriptDecision(AutoTaskRuntimeState state)
    {
        if (!state.TryGetProperty<GoldBalloonAutoTaskScriptContext>(GoldBalloonAutoTaskStateKeys.ResolvedScriptContext, out var context))
        {
            return AutoTaskDecision.Fail("Gold balloon script metadata was not loaded before entering the stage.");
        }

        var runState = GetScriptRunState(state);
        if (runState == GoldBalloonAutoTaskScriptRunState.Running)
        {
            return AutoTaskDecision.Wait(
                "Gold balloon script is already running for the current stage.",
                DefaultWaitDelayMs,
                AutoTaskPhase.ExecutingScript);
        }

        if (runState == GoldBalloonAutoTaskScriptRunState.FinishedCurrentStage)
        {
            return AutoTaskDecision.Wait(
                "Gold balloon script already finished for the current stage. Waiting for the result flow.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult);
        }

        SetScriptRunState(state, GoldBalloonAutoTaskScriptRunState.Running);
        return AutoTaskDecision.StartScript(
            BuildExecutionQuery(context),
            "Gold balloon stage entry completed. Start the resolved gold balloon script.",
            AutoTaskPhase.ExecutingScript);
    }

    private async Task<AutoTaskDecision?> TryPreloadScriptContextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!TryGetRecognizedGoldBalloonMap(snapshot, out var map))
        {
            return AutoTaskDecision.Wait(
                "Waiting for gold balloon map OCR to recognize the active beginner map.",
                DefaultWaitDelayMs,
                AutoTaskPhase.NavigatingToStage);
        }

        if (state.TryGetProperty<GoldBalloonAutoTaskScriptContext>(GoldBalloonAutoTaskStateKeys.ResolvedScriptContext, out var existingContext) &&
            existingContext.Map == map)
        {
            return null;
        }

        var resolution = await _scriptResolver.ResolveAsync(BuildScriptQuery(map), state, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsResolved || string.IsNullOrWhiteSpace(resolution.FilePath))
        {
            return AutoTaskDecision.Fail(
                string.IsNullOrWhiteSpace(resolution.Message)
                    ? $"Gold balloon script binding for '{map}' is not configured."
                    : resolution.Message);
        }

        var scriptDocument = _scriptDocumentService.LoadCompatible(resolution.FilePath).Document;
        var context = new GoldBalloonAutoTaskScriptContext
        {
            Map = map,
            Difficulty = ParseEnum(scriptDocument.Metadata.Difficulty, StageDifficulty.Medium),
            Mode = ParseEnum(scriptDocument.Metadata.Mode, StageMode.Standard),
            Hero = ParseEnum(scriptDocument.Metadata.Hero, HeroType.Quincy),
            FilePath = resolution.FilePath
        };

        state.RecordScriptResolution(resolution);
        state.SetProperty(GoldBalloonAutoTaskStateKeys.ResolvedScriptContext, context);
        state.SetProperty(GoldBalloonAutoTaskStateKeys.RecognizedMap, map);
        state.SetProperty(GoldBalloonAutoTaskStateKeys.HeroSelected, false);
        state.SetProperty(GoldBalloonAutoTaskStateKeys.MapSearchAttempts, 0);
        SetScriptRunState(state, GoldBalloonAutoTaskScriptRunState.NotStarted);
        return null;
    }

    private static AutoTaskScriptQuery BuildExecutionQuery(GoldBalloonAutoTaskScriptContext context)
    {
        var stageTarget = new StageEntryTarget
        {
            Map = context.Map,
            Difficulty = context.Difficulty,
            Mode = context.Mode
        };

        return new AutoTaskScriptQuery
        {
            Kind = AutoTaskKind.GoldBalloon,
            StageTarget = stageTarget,
            PreferredFilePath = context.FilePath,
            SlotId = ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(context.Map),
            RequiredTags = ["gold-balloon"],
            Description = "Resolve the preloaded gold balloon script for execution."
        };
    }

    private static AutoTaskScriptQuery BuildScriptQuery(GameMapType map)
    {
        return new AutoTaskScriptQuery
        {
            Kind = AutoTaskKind.GoldBalloon,
            StageTarget = new StageEntryTarget
            {
                Map = map,
                Difficulty = StageDifficulty.Easy,
                Mode = StageMode.Standard
            },
            SlotId = ManagedScriptSlotIdFactory.CreateGoldBalloonSlotId(map),
            RequiredTags = ["gold-balloon"],
            Description = "Resolve a gold-balloon-farming script for the recognized beginner map."
        };
    }

    private static bool TryGetRecognizedGoldBalloonMap(GameUiSnapshot snapshot, out GameMapType map)
    {
        if (snapshot.Facts.TryGetValue(MapSearchFlowState.GoldBalloonMapFact, out var rawMap) && rawMap is GameMapType typedMap)
        {
            map = typedMap;
            return true;
        }

        map = default;
        return false;
    }

    private static bool IsMapSearchResultReady(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        return MapSearchFlowState.IsResultReady(
            state,
            snapshot,
            GoldBalloonAutoTaskStateKeys.MapSearchPixelBaseline,
            GoldBalloonAutoTaskStateKeys.MapSearchPixelChangedSince);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

    private static AutoTaskDecision DecideAfterScriptExecution(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        state.ClearPendingScriptOutcome();
        state.ClearActiveScript();
        SetScriptRunState(state, GoldBalloonAutoTaskScriptRunState.FinishedCurrentStage);

        return snapshot.State switch
        {
            GameUiStateId.InLevel or GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Gold balloon script already finished for the current stage. Waiting for the result UI.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult),
            GameUiStateId.Defeat => AutoTaskDecision.Navigate(
                "Gold balloon stage ended in defeat. Stop script handling and continue the defeat flow.",
                AutoTaskPhase.AdvancingObjective),
            _ => AutoTaskDecision.Navigate(
                "Gold balloon script completed. Continue the reward and chest flow.",
                AutoTaskPhase.AdvancingObjective)
        };
    }

    private static GoldBalloonAutoTaskScriptRunState GetScriptRunState(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<GoldBalloonAutoTaskScriptRunState>(GoldBalloonAutoTaskStateKeys.ScriptRunState, out var runState)
            ? runState
            : GoldBalloonAutoTaskScriptRunState.NotStarted;
    }

    private static void SetScriptRunState(
        AutoTaskRuntimeState state,
        GoldBalloonAutoTaskScriptRunState runState)
    {
        state.SetProperty(GoldBalloonAutoTaskStateKeys.ScriptRunState, runState);
    }

    private static void ResetScriptLifecycleForNextStageIfNeeded(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (!ShouldResetScriptLifecycle(snapshot.State) ||
            GetScriptRunState(state) == GoldBalloonAutoTaskScriptRunState.NotStarted)
        {
            return;
        }

        state.ClearActiveScript();
        SetScriptRunState(state, GoldBalloonAutoTaskScriptRunState.NotStarted);
    }

    private static bool ShouldResetScriptLifecycle(GameUiStateId state)
    {
        return state is
            GameUiStateId.MainMenu or
            GameUiStateId.CollectionEvent or
            GameUiStateId.CollectionEventClaimable or
            GameUiStateId.MapSearch or
            GameUiStateId.MapGrid or
            GameUiStateId.DifficultySelect or
            GameUiStateId.EasyModeSelect or
            GameUiStateId.MediumModeSelect or
            GameUiStateId.HardModeSelect or
            GameUiStateId.ModeSelect or
            GameUiStateId.HeroSelect or
            GameUiStateId.Returnable;
    }
}
