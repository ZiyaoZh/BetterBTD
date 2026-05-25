using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services.MyScripts;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Core.AutoTasks.Strategies;

public sealed class CollectionAutoTaskStrategy : IAutoTaskStrategy
{
    private const int DefaultWaitDelayMs = 500;

    private readonly ManagedAutoTaskScriptResolver _scriptResolver;
    private readonly ScriptDocumentService _scriptDocumentService;

    public CollectionAutoTaskStrategy()
        : this(ManagedAutoTaskScriptResolver.Instance, ScriptDocumentService.Instance)
    {
    }

    internal CollectionAutoTaskStrategy(
        ManagedAutoTaskScriptResolver scriptResolver,
        ScriptDocumentService scriptDocumentService)
    {
        _scriptResolver = scriptResolver ?? throw new ArgumentNullException(nameof(scriptResolver));
        _scriptDocumentService = scriptDocumentService ?? throw new ArgumentNullException(nameof(scriptDocumentService));
    }

    public AutoTaskKind Kind => AutoTaskKind.Collection;

    public async Task<AutoTaskDecision> DecideNextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        cancellationToken.ThrowIfCancellationRequested();

        ResetScriptLifecycleForNextStageIfNeeded(state, snapshot);

        if (state.HasPendingScriptOutcome)
        {
            return DecideAfterScriptExecution(state, snapshot);
        }

        if (snapshot.State == GameUiStateId.MapSearchResults)
        {
            var preloadDecision = await TryPreloadScriptContextAsync(state, snapshot, cancellationToken).ConfigureAwait(false);
            if (preloadDecision is not null)
            {
                return preloadDecision;
            }
        }

        return snapshot.State switch
        {
            GameUiStateId.InLevel => TryBuildStartScriptDecision(state),
            GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Waiting for the collection stage to finish loading.",
                DefaultWaitDelayMs,
                AutoTaskPhase.WaitingForLevelLoad),
            GameUiStateId.MainMenu => AutoTaskDecision.Navigate(
                "Open the collection stage flow from the main menu.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.PreparingStage
                    : AutoTaskPhase.NavigatingToStage),
            _ => AutoTaskDecision.Navigate(
                "Advance the collection navigation flow.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.AdvancingObjective
                    : AutoTaskPhase.NavigatingToStage)
        };
    }

    private static AutoTaskDecision TryBuildStartScriptDecision(AutoTaskRuntimeState state)
    {
        if (!state.TryGetProperty<CollectionAutoTaskScriptContext>(CollectionAutoTaskStateKeys.ResolvedScriptContext, out var context))
        {
            return AutoTaskDecision.Fail("Collection script metadata was not loaded before entering the stage.");
        }

        var runState = GetScriptRunState(state);
        if (runState == CollectionAutoTaskScriptRunState.Running)
        {
            return AutoTaskDecision.Wait(
                "Collection script is already running for the current stage.",
                DefaultWaitDelayMs,
                AutoTaskPhase.ExecutingScript);
        }

        if (runState == CollectionAutoTaskScriptRunState.FinishedCurrentStage)
        {
            return AutoTaskDecision.Wait(
                "Collection script already finished for the current stage. Waiting for the result flow.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult);
        }

        SetScriptRunState(state, CollectionAutoTaskScriptRunState.Running);
        return AutoTaskDecision.StartScript(
            BuildExecutionQuery(state, context),
            "Collection stage entry completed. Start the resolved collection script.",
            AutoTaskPhase.ExecutingScript);
    }

    private async Task<AutoTaskDecision?> TryPreloadScriptContextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!TryGetRecognizedCollectionMap(snapshot, out var map))
        {
            return AutoTaskDecision.Wait(
                "Waiting for collection map OCR to recognize the active expert map.",
                DefaultWaitDelayMs,
                AutoTaskPhase.NavigatingToStage);
        }

        if (state.TryGetProperty<CollectionAutoTaskScriptContext>(CollectionAutoTaskStateKeys.ResolvedScriptContext, out var existingContext) &&
            existingContext.Map == map)
        {
            return null;
        }

        var query = BuildScriptQuery(state, map);
        var resolution = await _scriptResolver.ResolveAsync(query, state, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsResolved || string.IsNullOrWhiteSpace(resolution.FilePath))
        {
            return AutoTaskDecision.Fail(
                string.IsNullOrWhiteSpace(resolution.Message)
                    ? $"Collection script binding for '{map}' is not configured."
                    : resolution.Message);
        }

        var scriptDocument = _scriptDocumentService.LoadCompatible(resolution.FilePath).Document;
        var context = new CollectionAutoTaskScriptContext
        {
            Map = map,
            Difficulty = ParseEnum(scriptDocument.Metadata.Difficulty, StageDifficulty.Medium),
            Mode = ParseEnum(scriptDocument.Metadata.Mode, StageMode.Standard),
            Hero = ParseEnum(scriptDocument.Metadata.Hero, HeroType.Quincy),
            FilePath = resolution.FilePath
        };

        state.RecordScriptResolution(resolution);
        state.SetProperty(CollectionAutoTaskStateKeys.ResolvedScriptContext, context);
        state.SetProperty(CollectionAutoTaskStateKeys.RecognizedMap, map);
        state.SetProperty(CollectionAutoTaskStateKeys.HeroSelected, false);
        state.SetProperty(CollectionAutoTaskStateKeys.MapSearchAttempts, 0);
        SetScriptRunState(state, CollectionAutoTaskScriptRunState.NotStarted);
        return null;
    }

    private static AutoTaskScriptQuery BuildExecutionQuery(
        AutoTaskRuntimeState state,
        CollectionAutoTaskScriptContext context)
    {
        var stageTarget = new StageEntryTarget
        {
            Map = context.Map,
            Difficulty = context.Difficulty,
            Mode = context.Mode
        };

        return new AutoTaskScriptQuery
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = stageTarget,
            VariantKey = state.Request.VariantKey,
            PreferredFilePath = context.FilePath,
            SlotId = BuildSlotId(state.Request.VariantKey, context.Map),
            RequiredTags = ["collection"],
            Description = "Resolve the preloaded collection script for execution."
        };
    }

    private static AutoTaskScriptQuery BuildScriptQuery(AutoTaskRuntimeState state, GameMapType map)
    {
        var stageTarget = new StageEntryTarget
        {
            Map = map,
            Difficulty = state.Request.StageTarget.Difficulty,
            Mode = state.Request.StageTarget.Mode
        };

        return new AutoTaskScriptQuery
        {
            Kind = AutoTaskKind.Collection,
            StageTarget = stageTarget,
            VariantKey = state.Request.VariantKey,
            SlotId = BuildSlotId(state.Request.VariantKey, map),
            RequiredTags = ["collection"],
            Description = "Resolve a collection-farming script for the recognized expert map."
        };
    }

    private static string BuildSlotId(string variantKey, GameMapType map)
    {
        return ManagedScriptCollectionModeCatalog.TryNormalizeKey(variantKey, out var normalizedVariantKey)
            ? ManagedScriptSlotIdFactory.CreateCollectionSlotId(normalizedVariantKey, map)
            : string.Empty;
    }

    private static bool TryGetRecognizedCollectionMap(GameUiSnapshot snapshot, out GameMapType map)
    {
        if (snapshot.Facts.TryGetValue("collectionMap", out var rawMap) && rawMap is GameMapType typedMap)
        {
            map = typedMap;
            return true;
        }

        map = default;
        return false;
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
        SetScriptRunState(state, CollectionAutoTaskScriptRunState.FinishedCurrentStage);

        return snapshot.State switch
        {
            GameUiStateId.InLevel or GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Collection script already finished for the current stage. Waiting for the result UI.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult),
            GameUiStateId.Defeat => AutoTaskDecision.Navigate(
                "Collection stage ended in defeat. Stop script handling and continue the defeat flow.",
                AutoTaskPhase.AdvancingObjective),
            _ => AutoTaskDecision.Navigate(
                "Collection script completed. Continue the reward and chest flow.",
                AutoTaskPhase.AdvancingObjective)
        };
    }

    private static CollectionAutoTaskScriptRunState GetScriptRunState(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<CollectionAutoTaskScriptRunState>(CollectionAutoTaskStateKeys.ScriptRunState, out var runState)
            ? runState
            : CollectionAutoTaskScriptRunState.NotStarted;
    }

    private static void SetScriptRunState(
        AutoTaskRuntimeState state,
        CollectionAutoTaskScriptRunState runState)
    {
        state.SetProperty(CollectionAutoTaskStateKeys.ScriptRunState, runState);
    }

    private static void ResetScriptLifecycleForNextStageIfNeeded(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (!ShouldResetScriptLifecycle(snapshot.State) ||
            GetScriptRunState(state) == CollectionAutoTaskScriptRunState.NotStarted)
        {
            return;
        }

        state.ClearActiveScript();
        SetScriptRunState(state, CollectionAutoTaskScriptRunState.NotStarted);
    }

    private static bool ShouldResetScriptLifecycle(GameUiStateId state)
    {
        return state is
            GameUiStateId.MainMenu or
            GameUiStateId.CollectionEvent or
            GameUiStateId.CollectionEventClaimable or
            GameUiStateId.MapSearch or
            GameUiStateId.MapSearchResults or
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
