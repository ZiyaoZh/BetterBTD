using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.MyScripts;

namespace BetterBTD.Core.AutoTasks.Strategies;

public sealed class RaceAutoTaskStrategy : IAutoTaskStrategy
{
    private const int DefaultWaitDelayMs = 500;
    private const int MaxUnknownUiAttempts = 8;

    public AutoTaskKind Kind => AutoTaskKind.Race;

    public Task<AutoTaskDecision> DecideNextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        cancellationToken.ThrowIfCancellationRequested();

        EnsureInitialized(state);

        if (snapshot.State != GameUiStateId.Unknown)
        {
            state.SetProperty(RaceAutoTaskStateKeys.UnknownUiAttempts, 0);
        }

        if (state.HasPendingScriptOutcome)
        {
            return Task.FromResult(DecideAfterScriptExecution(state, snapshot));
        }

        return Task.FromResult(snapshot.State switch
        {
            GameUiStateId.Unknown => DecideUnknownUi(state),
            GameUiStateId.InLevel => TryBuildStartScriptDecision(state),
            GameUiStateId.Loading when HasSeenRaceUi(state) => AutoTaskDecision.Wait(
                "Waiting for the race stage to finish loading.",
                DefaultWaitDelayMs,
                AutoTaskPhase.WaitingForLevelLoad),
            GameUiStateId.StageSettlement or
            GameUiStateId.StageHint or
            GameUiStateId.NetworkUnavailableDialog or
            GameUiStateId.Defeat or
            GameUiStateId.StageSettings or
            GameUiStateId.LevelUp or
            GameUiStateId.InstaMonkeyReward => BuildHandledOverlayDecision(state, snapshot.State),
            _ => BuildUnsupportedUiFailure(snapshot.State)
        });
    }

    private static AutoTaskDecision TryBuildStartScriptDecision(AutoTaskRuntimeState state)
    {
        MarkRaceUiSeen(state);

        var runState = GetScriptRunState(state);
        if (runState == RaceAutoTaskScriptRunState.Running)
        {
            return AutoTaskDecision.Wait(
                "Race script is already running for the current attempt.",
                DefaultWaitDelayMs,
                AutoTaskPhase.ExecutingScript);
        }

        if (runState == RaceAutoTaskScriptRunState.FinishedCurrentStage)
        {
            return AutoTaskDecision.Wait(
                "Race script already finished for the current attempt. Waiting for the result flow.",
                DefaultWaitDelayMs,
                AutoTaskPhase.SettlingResult);
        }

        var filePath = state.Request.PreferredScriptPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return AutoTaskDecision.Fail("Race-farming script ID is not configured.");
        }

        SetScriptRunState(state, RaceAutoTaskScriptRunState.Running);
        return AutoTaskDecision.StartScript(
            new AutoTaskScriptQuery
            {
                Kind = AutoTaskKind.Race,
                StageTarget = state.Request.StageTarget,
                PreferredFilePath = filePath,
                SlotId = ManagedScriptSlotIdFactory.CreateRaceCurrentSlotId(),
                RequiredTags = ["race"],
                Description = "Resolve the configured race-farming script for execution."
            },
            "Race stage detected. Start the configured script.",
            AutoTaskPhase.ExecutingScript);
    }

    private static AutoTaskDecision DecideAfterScriptExecution(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (snapshot.State == GameUiStateId.Unknown)
        {
            return DecideUnknownUi(state);
        }

        state.ClearPendingScriptOutcome();
        state.ClearActiveScript();

        return snapshot.State switch
        {
            GameUiStateId.InLevel or GameUiStateId.Loading => MarkScriptFinishedAndWait(state),
            GameUiStateId.StageSettlement or
            GameUiStateId.StageHint or
            GameUiStateId.NetworkUnavailableDialog or
            GameUiStateId.Defeat or
            GameUiStateId.StageSettings or
            GameUiStateId.LevelUp or
            GameUiStateId.InstaMonkeyReward => BuildHandledOverlayDecision(state, snapshot.State),
            _ => BuildUnsupportedUiFailure(snapshot.State)
        };
    }

    private static AutoTaskDecision MarkScriptFinishedAndWait(AutoTaskRuntimeState state)
    {
        MarkRaceUiSeen(state);
        SetScriptRunState(state, RaceAutoTaskScriptRunState.FinishedCurrentStage);
        return AutoTaskDecision.Wait(
            "Race script finished for the current attempt. Waiting for the result UI.",
            DefaultWaitDelayMs,
            AutoTaskPhase.SettlingResult);
    }

    private static AutoTaskDecision BuildHandledOverlayDecision(
        AutoTaskRuntimeState state,
        GameUiStateId uiState)
    {
        MarkRaceUiSeen(state);
        state.ClearActiveScript();
        SetScriptRunState(state, RaceAutoTaskScriptRunState.NotStarted);

        return AutoTaskDecision.Navigate(
            $"Handle race UI state '{uiState}'.",
            AutoTaskPhase.AdvancingObjective);
    }

    private static AutoTaskDecision DecideUnknownUi(AutoTaskRuntimeState state)
    {
        var attempts = state.TryGetProperty<int>(
            RaceAutoTaskStateKeys.UnknownUiAttempts,
            out var currentAttempts)
            ? currentAttempts + 1
            : 1;

        state.SetProperty(RaceAutoTaskStateKeys.UnknownUiAttempts, attempts);

        if (attempts > MaxUnknownUiAttempts)
        {
            return AutoTaskDecision.Fail(
                "Race farming could not recognize the current UI. Enter the race stage, then start the task again.");
        }

        return AutoTaskDecision.Wait(
            "Waiting for the race UI state to stabilize.",
            DefaultWaitDelayMs);
    }

    private static AutoTaskDecision BuildUnsupportedUiFailure(GameUiStateId uiState)
    {
        return AutoTaskDecision.Fail(
            $"Race farming can only start from an active race stage and only handles settlement, stage hint, network unavailable dialog, defeat, stage settings, level-up, and Insta Monkey reward screens. Current UI: {uiState}. Enter the race stage, then start the task again.");
    }

    private static void EnsureInitialized(AutoTaskRuntimeState state)
    {
        if (!state.TryGetProperty<RaceAutoTaskScriptRunState>(
                RaceAutoTaskStateKeys.ScriptRunState,
                out _))
        {
            SetScriptRunState(state, RaceAutoTaskScriptRunState.NotStarted);
        }

        if (!state.TryGetProperty<bool>(RaceAutoTaskStateKeys.HasSeenRaceUi, out _))
        {
            state.SetProperty(RaceAutoTaskStateKeys.HasSeenRaceUi, false);
        }

        if (!state.TryGetProperty<int>(RaceAutoTaskStateKeys.UnknownUiAttempts, out _))
        {
            state.SetProperty(RaceAutoTaskStateKeys.UnknownUiAttempts, 0);
        }
    }

    private static bool HasSeenRaceUi(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<bool>(RaceAutoTaskStateKeys.HasSeenRaceUi, out var hasSeenRaceUi) &&
               hasSeenRaceUi;
    }

    private static void MarkRaceUiSeen(AutoTaskRuntimeState state)
    {
        state.SetProperty(RaceAutoTaskStateKeys.HasSeenRaceUi, true);
    }

    private static RaceAutoTaskScriptRunState GetScriptRunState(AutoTaskRuntimeState state)
    {
        return state.TryGetProperty<RaceAutoTaskScriptRunState>(
            RaceAutoTaskStateKeys.ScriptRunState,
            out var runState)
            ? runState
            : RaceAutoTaskScriptRunState.NotStarted;
    }

    private static void SetScriptRunState(
        AutoTaskRuntimeState state,
        RaceAutoTaskScriptRunState runState)
    {
        state.SetProperty(RaceAutoTaskStateKeys.ScriptRunState, runState);
    }
}
