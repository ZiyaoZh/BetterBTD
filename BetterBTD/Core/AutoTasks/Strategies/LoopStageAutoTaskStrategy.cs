using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Models.ScriptEditor;
using BetterBTD.Services.MyScripts;

namespace BetterBTD.Core.AutoTasks.Strategies;

public sealed class LoopStageAutoTaskStrategy : IAutoTaskStrategy
{
    private const int DefaultWaitDelayMs = 500;
    private readonly ScriptDocumentService _scriptDocumentService;

    public LoopStageAutoTaskStrategy()
        : this(ScriptDocumentService.Instance)
    {
    }

    internal LoopStageAutoTaskStrategy(ScriptDocumentService scriptDocumentService)
    {
        _scriptDocumentService = scriptDocumentService ?? throw new ArgumentNullException(nameof(scriptDocumentService));
    }

    public AutoTaskKind Kind => AutoTaskKind.LoopStage;

    public Task<AutoTaskDecision> DecideNextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        CompleteFreeplayExitIfNeeded(state, snapshot);
        ResetForNextLoopIfNeeded(state, snapshot);
        RecoverScriptLifecycleIfNeeded(state, snapshot);

        if (state.HasPendingScriptOutcome)
        {
            return Task.FromResult(DecideAfterScriptExecution(state, snapshot));
        }

        var preloadDecision = TryPreloadScriptContext(state, cancellationToken);
        if (preloadDecision is not null)
        {
            return Task.FromResult(preloadDecision);
        }

        return Task.FromResult(state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound
            ? DecideFreeplay(state, snapshot)
            : DecideStandard(state, snapshot));
    }

    private static AutoTaskDecision DecideStandard(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        return snapshot.State switch
        {
            GameUiStateId.InLevel => TryBuildStartScriptDecision(state, 0, null, LoopStageScriptRunState.RunningBeforeBoundary),
            GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Waiting for the configured stage to finish loading.",
                DefaultWaitDelayMs,
                AutoTaskPhase.WaitingForLevelLoad),
            GameUiStateId.MainMenu => AutoTaskDecision.Navigate(
                "Open the configured stage flow from the main menu.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.PreparingStage
                    : AutoTaskPhase.NavigatingToStage),
            _ => AutoTaskDecision.Navigate(
                "Advance the configured stage navigation flow.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.AdvancingObjective
                    : AutoTaskPhase.NavigatingToStage)
        };
    }

    private static AutoTaskDecision DecideFreeplay(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        var runState = GetScriptRunState(state);
        if (runState == LoopStageScriptRunState.WaitingForBlockingUi)
        {
            if (IsBlockingFreeplayUi(snapshot.State))
            {
                return AutoTaskDecision.Navigate(
                    "Dismiss the blocking freeplay reward screen before resuming the script.",
                    AutoTaskPhase.SettlingResult);
            }

            if (snapshot.State == GameUiStateId.InLevel)
            {
                if (IsTargetRoundReached(state, snapshot))
                {
                    SetScriptRunState(state, LoopStageScriptRunState.WaitingForExit);
                    return AutoTaskDecision.Navigate(
                        "Target round reached after dismissing the blocking reward screen. Exit the freeplay stage.",
                        AutoTaskPhase.SettlingResult);
                }

                var resumeStepIndex = state.TryGetProperty<int>(
                    LoopStageAutoTaskStateKeys.ResumeStepIndex,
                    out var storedResumeStepIndex)
                    ? storedResumeStepIndex
                    : 0;
                return TryBuildStartScriptDecision(
                    state,
                    resumeStepIndex,
                    null,
                    LoopStageScriptRunState.RunningAfterBoundary);
            }

            return AutoTaskDecision.Wait(
                "Waiting for the freeplay reward screen to close.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult);
        }

        if (runState == LoopStageScriptRunState.WaitingForFreeplayPrompt)
        {
            if (snapshot.State == GameUiStateId.InLevel)
            {
                if (!state.TryGetProperty<bool>(
                        LoopStageAutoTaskStateKeys.FreeplayPromptConfirmed,
                        out var freeplayPromptConfirmed) ||
                    !freeplayPromptConfirmed)
                {
                    return AutoTaskDecision.Wait(
                        "Waiting for the freeplay confirmation prompt to be processed.",
                        DefaultWaitDelayMs,
                        AutoTaskPhase.SettlingResult);
                }

                if (!TryGetScriptContext(state, out var context))
                {
                    return AutoTaskDecision.Fail("Freeplay script metadata was lost before the freeplay segment started.");
                }

                return TryBuildStartScriptDecision(
                    state,
                    context.FreeplayBoundaryIndex + 1,
                    null,
                    LoopStageScriptRunState.RunningAfterBoundary);
            }

            return AutoTaskDecision.Navigate(
                "Advance the victory flow into freeplay.",
                AutoTaskPhase.SettlingResult);
        }

        if (runState == LoopStageScriptRunState.WaitingForExit)
        {
            var targetRoundReached = IsTargetRoundReached(state, snapshot);
            return snapshot.State == GameUiStateId.MainMenu
                ? targetRoundReached
                    ? AutoTaskDecision.Wait(
                        "Freeplay loop completed. Preparing the next stage loop.",
                        DefaultWaitDelayMs,
                        AutoTaskPhase.AdvancingObjective)
                    : AutoTaskDecision.Fail(
                        "Freeplay exited before the configured target round was confirmed.")
                : AutoTaskDecision.Navigate(
                    "Exit freeplay from the in-level settings menu.",
                    AutoTaskPhase.SettlingResult);
        }

        if (runState == LoopStageScriptRunState.WaitingForTargetRound)
        {
            if (snapshot.State == GameUiStateId.MainMenu)
            {
                return AutoTaskDecision.Fail(
                    "Freeplay exited before the configured target round was confirmed.");
            }

            if (IsBlockingFreeplayUi(snapshot.State))
            {
                return AutoTaskDecision.Navigate(
                    "Dismiss the blocking freeplay reward screen before continuing to monitor the target round.",
                    AutoTaskPhase.SettlingResult);
            }

            if (snapshot.State is (GameUiStateId.InLevel or GameUiStateId.StageSettings) &&
                IsTargetRoundReached(state, snapshot))
            {
                SetScriptRunState(state, LoopStageScriptRunState.WaitingForExit);
                return AutoTaskDecision.Navigate(
                    "Target round reached after the freeplay script completed. Exit the freeplay stage.",
                    AutoTaskPhase.SettlingResult);
            }

            return AutoTaskDecision.Wait(
                "Freeplay script completed. Waiting for consecutive confirmation of the target round.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult);
        }

        if (runState == LoopStageScriptRunState.RunningBeforeBoundary ||
            runState == LoopStageScriptRunState.RunningAfterBoundary)
        {
            if (runState == LoopStageScriptRunState.RunningAfterBoundary &&
                IsTargetRoundReached(state, snapshot))
            {
                SetScriptRunState(state, LoopStageScriptRunState.WaitingForExit);
                return AutoTaskDecision.Navigate(
                    "Target round reached. Exit the freeplay stage.",
                    AutoTaskPhase.SettlingResult);
            }

            return AutoTaskDecision.Wait(
                "Configured freeplay script is already running.",
                DefaultWaitDelayMs,
                AutoTaskPhase.ExecutingScript);
        }

        return snapshot.State switch
        {
            GameUiStateId.InLevel => TryBuildStartScriptDecision(
                state,
                0,
                context => context.FreeplayBoundaryIndex + 1,
                LoopStageScriptRunState.RunningBeforeBoundary),
            GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Waiting for the configured stage to finish loading.",
                DefaultWaitDelayMs,
                AutoTaskPhase.WaitingForLevelLoad),
            GameUiStateId.MainMenu => AutoTaskDecision.Navigate(
                "Open the configured stage flow from the main menu.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.PreparingStage
                    : AutoTaskPhase.NavigatingToStage),
            _ => AutoTaskDecision.Navigate(
                "Advance the configured stage navigation flow.",
                state.Phase == AutoTaskPhase.AdvancingObjective
                    ? AutoTaskPhase.AdvancingObjective
                    : AutoTaskPhase.NavigatingToStage)
        };
    }

    private AutoTaskDecision? TryPreloadScriptContext(
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken)
    {
        if (state.TryGetProperty<LoopStageAutoTaskScriptContext>(
                LoopStageAutoTaskStateKeys.ResolvedScriptContext,
                out _))
        {
            return null;
        }

        var filePath = state.Request.PreferredScriptPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return AutoTaskDecision.Fail("Loop-stage script ID is not configured.");
        }

        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound &&
            state.Request.ExitAfterRound < 1)
        {
            return AutoTaskDecision.Fail("Freeplay loop mode requires a positive target round.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var scriptDocument = _scriptDocumentService.LoadCompatible(filePath).Document;
        var boundaryIndexes = scriptDocument.Instructions
            .Select((instruction, index) => new { instruction, index })
            .Where(item => string.Equals(
                item.instruction.CommandType,
                ScriptCommandType.FreeplayBoundary.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .ToList();

        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound && boundaryIndexes.Count != 1)
        {
            return AutoTaskDecision.Fail(
                boundaryIndexes.Count == 0
                    ? "Freeplay loop mode requires exactly one FreeplayBoundary instruction."
                    : "Freeplay loop mode requires exactly one FreeplayBoundary instruction; duplicate boundaries are not supported.");
        }

        var target = new StageEntryTarget
        {
            Map = ParseEnum(scriptDocument.Metadata.Map, GameMapType.MonkeyMeadow),
            Difficulty = ParseEnum(scriptDocument.Metadata.Difficulty, StageDifficulty.Easy),
            Mode = ParseEnum(scriptDocument.Metadata.Mode, StageMode.Standard)
        };
        var context = new LoopStageAutoTaskScriptContext
        {
            Category = InferCategoryFromMap(target.Map),
            Target = target,
            Hero = ParseEnum(scriptDocument.Metadata.Hero, HeroType.Quincy),
            FilePath = filePath,
            FreeplayBoundaryIndex = boundaryIndexes.Count == 1 ? boundaryIndexes[0] : -1
        };

        state.SetProperty(LoopStageAutoTaskStateKeys.ResolvedScriptContext, context);
        state.SetProperty(LoopStageAutoTaskStateKeys.HeroSelected, false);
        state.SetProperty(LoopStageAutoTaskStateKeys.MapLocateAttempts, 0);
        state.SetProperty(LoopStageAutoTaskStateKeys.ResumeStepIndex, 0);
        state.SetProperty(LoopStageAutoTaskStateKeys.InterruptedUiState, GameUiStateId.Unknown);
        state.SetProperty(LoopStageAutoTaskStateKeys.TargetRoundReached, false);
        state.SetProperty(LoopStageAutoTaskStateKeys.FreeplayPromptConfirmed, false);
        SetScriptRunState(state, LoopStageScriptRunState.NotStarted);
        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound)
        {
            state.SetProperty(
                LoopStageAutoTaskStateKeys.RoundProgressTracker,
                new LoopStageRoundProgressTracker(state.Request.ExitAfterRound));
        }

        return null;
    }

    private static AutoTaskDecision TryBuildStartScriptDecision(
        AutoTaskRuntimeState state,
        int startStepIndex,
        Func<LoopStageAutoTaskScriptContext, int?>? endStepSelector,
        LoopStageScriptRunState runState)
    {
        if (!TryGetScriptContext(state, out var context))
        {
            return AutoTaskDecision.Fail("Loop-stage script metadata was not loaded before entering the stage.");
        }

        if (GetScriptRunState(state) == runState)
        {
            return AutoTaskDecision.Wait(
                "Configured loop-stage script is already running.",
                DefaultWaitDelayMs,
                AutoTaskPhase.ExecutingScript);
        }

        if (GetScriptRunState(state) == LoopStageScriptRunState.FinishedCurrentStage)
        {
            return AutoTaskDecision.Wait(
                "Configured loop-stage script already finished for the current stage. Waiting for the result flow.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult);
        }

        SetScriptRunState(state, runState);
        state.SetProperty(LoopStageAutoTaskStateKeys.ResumeStepIndex, startStepIndex);
        state.SetProperty(LoopStageAutoTaskStateKeys.InterruptedUiState, GameUiStateId.Unknown);
        return AutoTaskDecision.StartScript(
            new AutoTaskScriptQuery
            {
                Kind = AutoTaskKind.LoopStage,
                StageTarget = context.Target,
                PreferredFilePath = context.FilePath,
                StartStepIndex = startStepIndex,
                EndStepIndexExclusive = endStepSelector?.Invoke(context),
                Description = "Resolve the configured loop-stage script for execution."
            },
            "Start the resolved loop-stage script.",
            AutoTaskPhase.ExecutingScript);
    }

    private static AutoTaskDecision DecideAfterScriptExecution(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        var interruptedUiState = state.TryGetProperty<GameUiStateId>(
            LoopStageAutoTaskStateKeys.InterruptedUiState,
            out var storedInterruptedUiState)
            ? storedInterruptedUiState
            : GameUiStateId.Unknown;

        state.ClearPendingScriptOutcome();
        state.ClearActiveScript();

        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound)
        {
            if (interruptedUiState == GameUiStateId.Defeat)
            {
                SetScriptRunState(state, LoopStageScriptRunState.FinishedCurrentStage);
                return AutoTaskDecision.Navigate(
                    "Freeplay stage ended in defeat. Return to the main menu and retry.",
                    AutoTaskPhase.AdvancingObjective);
            }

            if (IsBlockingFreeplayUi(interruptedUiState))
            {
                SetScriptRunState(state, LoopStageScriptRunState.WaitingForBlockingUi);
                return AutoTaskDecision.Navigate(
                    "Dismiss the blocking freeplay reward screen before resuming the script.",
                    AutoTaskPhase.SettlingResult);
            }

            if (GetScriptRunState(state) == LoopStageScriptRunState.RunningBeforeBoundary)
            {
                SetScriptRunState(state, LoopStageScriptRunState.WaitingForFreeplayPrompt);
                return AutoTaskDecision.Navigate(
                    "Continue the stage settlement and freeplay confirmation flow.",
                    AutoTaskPhase.SettlingResult);
            }

            if (GetScriptRunState(state) == LoopStageScriptRunState.RunningAfterBoundary)
            {
                SetScriptRunState(state, LoopStageScriptRunState.WaitingForTargetRound);
                return AutoTaskDecision.Wait(
                    "Freeplay script completed. Waiting for consecutive confirmation of the target round.",
                    DefaultWaitDelayMs,
                    AutoTaskPhase.SettlingResult);
            }
        }

        SetScriptRunState(state, LoopStageScriptRunState.FinishedCurrentStage);
        return snapshot.State switch
        {
            GameUiStateId.InLevel or GameUiStateId.Loading => AutoTaskDecision.Wait(
                "Configured stage script already finished for the current loop. Waiting for the result flow.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult),
            GameUiStateId.Defeat => AutoTaskDecision.Navigate(
                "Configured stage ended in defeat. Return to the main menu and retry.",
                AutoTaskPhase.AdvancingObjective),
            _ => AutoTaskDecision.Navigate(
                "Configured stage script completed. Continue the result flow and start the next loop.",
                AutoTaskPhase.AdvancingObjective)
        };
    }

    private static bool IsTargetRoundReached(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        if (state.TryGetProperty<bool>(
                LoopStageAutoTaskStateKeys.TargetRoundReached,
                out var targetRoundReached) &&
            targetRoundReached)
        {
            return true;
        }

        if (!state.TryGetProperty<LoopStageRoundProgressTracker>(
                LoopStageAutoTaskStateKeys.RoundProgressTracker,
                out var tracker) ||
            !tracker.Observe(snapshot.StageState?.Round))
        {
            return false;
        }

        state.SetProperty(LoopStageAutoTaskStateKeys.TargetRoundReached, true);
        return true;
    }

    private static void CompleteFreeplayExitIfNeeded(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (state.Request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound &&
            snapshot.State == GameUiStateId.MainMenu &&
            GetScriptRunState(state) == LoopStageScriptRunState.WaitingForExit &&
            IsTargetRoundReached(state, snapshot))
        {
            SetScriptRunState(state, LoopStageScriptRunState.FinishedCurrentStage);
        }
    }

    private static void ResetForNextLoopIfNeeded(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        if (GetScriptRunState(state) != LoopStageScriptRunState.FinishedCurrentStage ||
            snapshot.State != GameUiStateId.MainMenu)
        {
            return;
        }

        state.SetProperty(LoopStageAutoTaskStateKeys.HeroSelected, false);
        state.SetProperty(LoopStageAutoTaskStateKeys.MapLocateAttempts, 0);
        state.SetProperty(LoopStageAutoTaskStateKeys.ResumeStepIndex, 0);
        state.RemoveProperty(LoopStageAutoTaskStateKeys.ResolvedScriptContext);
        state.RemoveProperty(LoopStageAutoTaskStateKeys.RoundProgressTracker);
        state.RemoveProperty(LoopStageAutoTaskStateKeys.TargetRoundReached);
        state.RemoveProperty(LoopStageAutoTaskStateKeys.FreeplayPromptConfirmed);
        state.ClearActiveScript();
        SetScriptRunState(state, LoopStageScriptRunState.NotStarted);
    }

    private static void RecoverScriptLifecycleIfNeeded(AutoTaskRuntimeState state, GameUiSnapshot snapshot)
    {
        var runState = GetScriptRunState(state);
        if (runState is not (LoopStageScriptRunState.RunningBeforeBoundary or LoopStageScriptRunState.RunningAfterBoundary) ||
            !ShouldResetScriptLifecycle(snapshot.State))
        {
            return;
        }

        state.ClearActiveScript();
        SetScriptRunState(state, LoopStageScriptRunState.NotStarted);
    }

    private static bool ShouldResetScriptLifecycle(GameUiStateId state)
    {
        return state is
            GameUiStateId.MainMenu or
            GameUiStateId.MapCategorySelect or
            GameUiStateId.MapGrid or
            GameUiStateId.DifficultySelect or
            GameUiStateId.EasyModeSelect or
            GameUiStateId.MediumModeSelect or
            GameUiStateId.HardModeSelect or
            GameUiStateId.ModeSelect or
            GameUiStateId.HeroSelect or
            GameUiStateId.Returnable;
    }

    private static bool IsBlockingFreeplayUi(GameUiStateId state)
    {
        return state is
            GameUiStateId.LevelUp or
            GameUiStateId.InstaMonkeyReward or
            GameUiStateId.StageChallengeWithHint or
            GameUiStateId.StageHint or
            GameUiStateId.Reward or
            GameUiStateId.ChestOpened or
            GameUiStateId.TwoChests or
            GameUiStateId.ThreeChests or
            GameUiStateId.ConfirmDialog;
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

    private static void SetScriptRunState(
        AutoTaskRuntimeState state,
        LoopStageScriptRunState runState)
    {
        state.SetProperty(LoopStageAutoTaskStateKeys.ScriptRunState, runState);
    }

    private static BlackBorderMapCategory InferCategoryFromMap(GameMapType map)
    {
        var definition = GameElementCatalog.Maps.FirstOrDefault(item => item.Type == map);
        return definition?.Tier switch
        {
            MapDifficultyTier.Beginner => BlackBorderMapCategory.Beginner,
            MapDifficultyTier.Intermediate => BlackBorderMapCategory.Intermediate,
            MapDifficultyTier.Advanced => BlackBorderMapCategory.Advanced,
            MapDifficultyTier.Expert => BlackBorderMapCategory.Expert,
            _ => BlackBorderMapCategory.Beginner
        };
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}
