using System.IO;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Core.AutoTasks;

public sealed class AutoTaskRunner
{
    private const int StageScriptUiMonitorIntervalMs = 250;

    private readonly IAutoTaskStrategyRegistry _strategyRegistry;
    private readonly AutoTaskRuntimeServices _defaultRuntimeServices;
    private readonly AutoTaskRuntimeScriptPreviewService _scriptPreviewService;

    private AutoTaskExecutionSession? _currentSession;
    private IAutoTaskScriptExecutor? _currentScriptExecutor;

    public AutoTaskRunner()
        : this(
            AutoTaskStrategyRegistry.Instance,
            AutoTaskRuntimeServiceFactory.CreateDefault(),
            AutoTaskRuntimeScriptPreviewService.Instance)
    {
    }

    internal AutoTaskRunner(
        IAutoTaskStrategyRegistry strategyRegistry,
        AutoTaskRuntimeServices defaultRuntimeServices,
        AutoTaskRuntimeScriptPreviewService scriptPreviewService)
    {
        _strategyRegistry = strategyRegistry ?? throw new ArgumentNullException(nameof(strategyRegistry));
        _defaultRuntimeServices = defaultRuntimeServices ?? throw new ArgumentNullException(nameof(defaultRuntimeServices));
        _scriptPreviewService = scriptPreviewService ?? throw new ArgumentNullException(nameof(scriptPreviewService));
    }

    public AutoTaskExecutionSession? CurrentSession => _currentSession;

    public bool RequestPause()
    {
        var sessionPaused = _currentSession?.RequestPause() == true;
        var scriptPaused = _currentScriptExecutor?.RequestPause() == true;
        return sessionPaused || scriptPaused;
    }

    public bool Resume()
    {
        var sessionResumed = _currentSession?.Resume() == true;
        var scriptResumed = _currentScriptExecutor?.Resume() == true;
        return sessionResumed || scriptResumed;
    }

    public async Task<AutoTaskExecutionResult> ExecuteAsync(
        AutoTaskRequest request,
        AutoTaskExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        options ??= new AutoTaskExecutionOptions();

        var runtimeServices = options.RuntimeServices ?? _defaultRuntimeServices;
        var strategy = _strategyRegistry.GetRequiredStrategy(request.Kind);
        var state = new AutoTaskRuntimeState(request);
        var session = new AutoTaskExecutionSession(
            string.IsNullOrWhiteSpace(request.Key) ? request.Kind.ToKey() : request.Key,
            request.Kind);

        runtimeServices.GameUiState.ResetStabilizationState();
        _currentSession = session;
        _currentScriptExecutor = runtimeServices.ScriptExecutor;

        session.MarkStarted(state.Phase, "Auto task execution started.");

        try
        {
            while (state.LoopIteration < options.MaxLoopIterations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                state.IncrementLoopIteration();
                session.MarkLoopIteration(state.LoopIteration);

                await session
                    .ReachCheckpointAsync(
                        "CaptureUiState",
                        AutoTaskActivityKind.CapturingUi,
                        "Capturing current game UI state.",
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);

                var snapshot = await runtimeServices.GameUiState
                    .CaptureSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);

                state.RecordUiSnapshot(snapshot);
                session.UpdateUiSnapshot(snapshot, $"Detected UI state '{snapshot.State}'.");
                UpdateStageCompletion(state, session, snapshot);

                var decision = await strategy
                    .DecideNextAsync(state, snapshot, cancellationToken)
                    .ConfigureAwait(false);

                if (decision.NextPhase.HasValue && state.Phase != decision.NextPhase.Value)
                {
                    state.Phase = decision.NextPhase.Value;
                    session.MarkPhase(state.Phase, decision.Description);
                }

                switch (decision.Kind)
                {
                    case AutoTaskDecisionKind.Wait:
                        await session
                            .ReachCheckpointAsync(
                                "Wait",
                                AutoTaskActivityKind.Waiting,
                                decision.Description,
                                null,
                                cancellationToken)
                            .ConfigureAwait(false);
                        await session
                            .DelayAsync(ResolveDelay(decision.DelayMs, options), cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case AutoTaskDecisionKind.Navigate:
                        var step = runtimeServices.Navigator.GetNextStep(request.StageTarget, snapshot);
                        await session
                            .ReachCheckpointAsync(
                                "Navigate",
                                AutoTaskActivityKind.Navigating,
                                step.Description,
                                null,
                                cancellationToken)
                            .ConfigureAwait(false);

                        var navigationResult = await runtimeServices.UiActionExecutor
                            .ExecuteAsync(step, state, snapshot, cancellationToken)
                            .ConfigureAwait(false);

                        if (!navigationResult.Succeeded)
                        {
                            state.RecordNavigationFailure();
                            session.UpdateNavigationFailures(
                                state.ConsecutiveNavigationFailures,
                                navigationResult.Message);

                            if (state.ConsecutiveNavigationFailures >= options.MaxConsecutiveNavigationFailures)
                            {
                                return BuildFailedResult(
                                    session,
                                    state,
                                    "Navigate",
                                    navigationResult.Message);
                            }
                        }
                        else
                        {
                            state.ResetNavigationFailures();
                            session.UpdateNavigationFailures(0, navigationResult.Message);
                        }

                        await session
                            .DelayAsync(ResolveDelay(navigationResult.RecommendedDelayMs, options), cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case AutoTaskDecisionKind.StartScriptExecution:
                        if (decision.ScriptQuery is null)
                        {
                            return BuildFailedResult(
                                session,
                                state,
                                "ResolveScript",
                                "Strategy did not provide a script query.");
                        }

                        await session
                            .ReachCheckpointAsync(
                                "ResolveScript",
                                AutoTaskActivityKind.ResolvingScript,
                                decision.Description,
                                null,
                                cancellationToken)
                            .ConfigureAwait(false);

                        var scriptResolution = await runtimeServices.ScriptResolver
                            .ResolveAsync(decision.ScriptQuery, state, cancellationToken)
                            .ConfigureAwait(false);

                        if (!scriptResolution.IsResolved || string.IsNullOrWhiteSpace(scriptResolution.FilePath))
                        {
                            return BuildFailedResult(
                                session,
                                state,
                                "ResolveScript",
                                string.IsNullOrWhiteSpace(scriptResolution.Message)
                                    ? "Script resolver did not return a runnable script."
                                    : scriptResolution.Message);
                        }

                        state.RecordScriptResolution(scriptResolution);
                        state.BeginStageAttempt();

                        var scriptPreview = TryLoadScriptPreview(scriptResolution.FilePath);
                        session.UpdateActiveScript(
                            scriptResolution.FilePath,
                            scriptPreview.DisplayName,
                            scriptPreview.Steps,
                            "Resolved auto-task script.");

                        var scriptExecutionOptions = new ScriptExecutionOptions
                        {
                            StartStepIndex = decision.ScriptQuery.StartStepIndex,
                            EndStepIndexExclusive = decision.ScriptQuery.EndStepIndexExclusive,
                            IntervalStrategy = ScriptExecutionOperationIntervalStrategy.CommonOperationInterval,
                            CommonOperationIntervalMs = Math.Max(0, request.OperationIntervalMs),
                            RequireCaptureService = true,
                            RequireTargetWindow = true
                        };

                        await session
                            .ReachCheckpointAsync(
                                "ExecuteScript",
                                AutoTaskActivityKind.ExecutingScript,
                                "Executing resolved auto-task script.",
                                null,
                                cancellationToken)
                            .ConfigureAwait(false);

                        EventHandler<ScriptExecutionProgressSnapshot>? scriptProgressHandler = (_, progressSnapshot) =>
                            session.UpdateActiveScriptProgress(progressSnapshot);

                        runtimeServices.ScriptExecutor.ProgressChanged += scriptProgressHandler;

                        ScriptExecutionResult scriptResult;
                        GameUiSnapshot? scriptInterruptedSnapshot;
                        try
                        {
                            (scriptResult, scriptInterruptedSnapshot) = await ExecuteScriptWithUiMonitoringAsync(
                                    request,
                                    state,
                                    session,
                                    runtimeServices,
                                    scriptResolution.FilePath,
                                    scriptExecutionOptions,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            runtimeServices.ScriptExecutor.ProgressChanged -= scriptProgressHandler;
                        }

                        if (scriptResult.Status == ScriptExecutionStatus.Cancelled)
                        {
                            if (scriptInterruptedSnapshot is not null && !cancellationToken.IsCancellationRequested)
                            {
                                state.SetProperty(
                                    LoopStageAutoTaskStateKeys.InterruptedUiState,
                                    scriptInterruptedSnapshot.State);
                                state.SetProperty(
                                    LoopStageAutoTaskStateKeys.ResumeStepIndex,
                                    Math.Max(0, scriptResult.LastCompletedStepIndex + 1));
                                state.RecordScriptExecutionResult(scriptResult);
                                state.Phase = AutoTaskPhase.SettlingResult;
                                session.MarkPhase(
                                    state.Phase,
                                    AutoTaskActivityKind.HandlingResult,
                                    $"Detected stage result UI '{scriptInterruptedSnapshot.State}'. Stopped the running script and continued the result flow.");
                                break;
                            }

                            session.MarkCancelled(AutoTaskPhase.ExecutingScript, "Underlying script execution was cancelled.");
                            return new AutoTaskExecutionResult
                            {
                                Status = AutoTaskExecutionStatus.Cancelled,
                                FinalProgress = session.GetSnapshot()
                            };
                        }

                        if (scriptResult.Status == ScriptExecutionStatus.Failed)
                        {
                            return BuildFailedResult(
                                session,
                                state,
                                "ExecuteScript",
                                scriptResult.Failure?.Message ?? scriptResult.Exception?.Message ?? "Underlying script execution failed.",
                                scriptResult.Exception);
                        }

                        state.RecordScriptExecutionResult(scriptResult);
                        state.Phase = AutoTaskPhase.SettlingResult;
                        session.MarkPhase(
                            state.Phase,
                            AutoTaskActivityKind.HandlingResult,
                            "Underlying script completed. Continue auto-task state flow.");
                        break;

                    case AutoTaskDecisionKind.Complete:
                        state.Phase = decision.NextPhase ?? AutoTaskPhase.Completed;
                        session.MarkCompleted(state.Phase, decision.Description);
                        return new AutoTaskExecutionResult
                        {
                            Status = AutoTaskExecutionStatus.Completed,
                            FinalProgress = session.GetSnapshot()
                        };

                    case AutoTaskDecisionKind.Fail:
                        return BuildFailedResult(session, state, "Decision", decision.Description);

                    default:
                        throw new InvalidOperationException($"Unsupported auto-task decision kind '{decision.Kind}'.");
                }
            }

            return BuildFailedResult(
                session,
                state,
                "LoopLimit",
                $"Auto-task exceeded the maximum loop count of {options.MaxLoopIterations}.");
        }
        catch (OperationCanceledException)
        {
            session.MarkCancelled(state.Phase, "Auto task execution cancelled.");
            return new AutoTaskExecutionResult
            {
                Status = AutoTaskExecutionStatus.Cancelled,
                FinalProgress = session.GetSnapshot()
            };
        }
        catch (Exception ex)
        {
            return BuildFailedResult(
                session,
                state,
                "UnhandledException",
                ex.Message,
                ex);
        }
        finally
        {
            runtimeServices.GameUiState.ResetStabilizationState();
            _currentScriptExecutor = null;
            _currentSession = null;
        }
    }

    private static int ResolveDelay(int delayMs, AutoTaskExecutionOptions options)
    {
        return delayMs > 0 ? delayMs : options.DefaultDecisionDelayMs;
    }

    private static void UpdateStageCompletion(
        AutoTaskRuntimeState state,
        AutoTaskExecutionSession session,
        GameUiSnapshot snapshot)
    {
        if (snapshot.State == GameUiStateId.Defeat)
        {
            state.RecordStageFailure();
            return;
        }

        if (state.TryRecordStageCompletion(snapshot.State))
        {
            session.MarkCompletedStageCount(
                state.CompletedStageCount,
                $"Recorded completed stage from result UI '{snapshot.State}'.");
        }
    }

    private static async Task<(ScriptExecutionResult Result, GameUiSnapshot? InterruptedSnapshot)> ExecuteScriptWithUiMonitoringAsync(
        AutoTaskRequest request,
        AutoTaskRuntimeState state,
        AutoTaskExecutionSession session,
        AutoTaskRuntimeServices runtimeServices,
        string scriptFilePath,
        ScriptExecutionOptions scriptExecutionOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(runtimeServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFilePath);
        ArgumentNullException.ThrowIfNull(scriptExecutionOptions);

        using var linkedScriptCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var scriptTask = runtimeServices.ScriptExecutor.ExecuteAsync(
            scriptFilePath,
            scriptExecutionOptions,
            linkedScriptCancellationSource.Token);

        try
        {
            if (!ShouldMonitorStageScriptUi(request.Kind))
            {
                return (await scriptTask.ConfigureAwait(false), null);
            }

            GameUiSnapshot? interruptedSnapshot = null;
            var lastPublishedUiSnapshot = state.LastUiSnapshot;
            while (!scriptTask.IsCompleted)
            {
                var completedTask = await Task
                    .WhenAny(scriptTask, Task.Delay(StageScriptUiMonitorIntervalMs, cancellationToken))
                    .ConfigureAwait(false);

                if (completedTask == scriptTask)
                {
                    break;
                }

                var snapshot = await runtimeServices.GameUiState
                    .CaptureSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);

                ObserveFreeplayTargetRound(request, state, snapshot);

                var shouldInterrupt = ShouldInterruptStageScript(request, state, snapshot.State);
                state.RecordUiSnapshot(snapshot);

                if (HasDisplayableUiStateChanged(lastPublishedUiSnapshot, snapshot) || shouldInterrupt)
                {
                    session.UpdateUiSnapshot(
                        snapshot,
                        shouldInterrupt
                            ? $"Detected stage result UI '{snapshot.State}' while the script was running."
                            : $"Updated UI state '{snapshot.State}' while the script was running.");
                    lastPublishedUiSnapshot = snapshot;
                }

                if (!shouldInterrupt)
                {
                    continue;
                }

                interruptedSnapshot = snapshot;
                linkedScriptCancellationSource.Cancel();
                break;
            }

            return (await scriptTask.ConfigureAwait(false), interruptedSnapshot);
        }
        catch
        {
            linkedScriptCancellationSource.Cancel();
            await ((Task)scriptTask)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }
    }

    private static bool ShouldMonitorStageScriptUi(AutoTaskKind kind)
    {
        return kind is AutoTaskKind.Collection
            or AutoTaskKind.GoldBalloon
            or AutoTaskKind.BlackBorder
            or AutoTaskKind.LoopStage
            or AutoTaskKind.Odyssey
            or AutoTaskKind.Race;
    }

    private static bool ShouldInterruptStageScript(
        AutoTaskRequest request,
        AutoTaskRuntimeState runtimeState,
        GameUiStateId state)
    {
        if (request.Kind == AutoTaskKind.LoopStage &&
            request.LoopStageRunMode == LoopStageRunMode.FreeplayUntilRound)
        {
            var targetRoundReached = runtimeState.TryGetProperty<bool>(
                LoopStageAutoTaskStateKeys.TargetRoundReached,
                out var reached) && reached;
            return runtimeState.TryGetProperty<LoopStageScriptRunState>(
                       LoopStageAutoTaskStateKeys.ScriptRunState,
                       out var runState) &&
                   runState == LoopStageScriptRunState.RunningAfterBoundary &&
                   (targetRoundReached || state is
                       GameUiStateId.Defeat or
                       GameUiStateId.LevelUp or
                       GameUiStateId.InstaMonkeyReward or
                       GameUiStateId.StageChallengeWithHint or
                       GameUiStateId.StageHint or
                       GameUiStateId.Reward or
                       GameUiStateId.ChestOpened or
                       GameUiStateId.TwoChests or
                       GameUiStateId.ThreeChests or
                       GameUiStateId.ConfirmDialog);
        }

        if (request.Kind == AutoTaskKind.Race)
        {
            return state is
                GameUiStateId.StageSettlement or
                GameUiStateId.StageHint or
                GameUiStateId.Defeat or
                GameUiStateId.StageSettings or
                GameUiStateId.LevelUp or
                GameUiStateId.InstaMonkeyReward;
        }

        return state is
            GameUiStateId.Defeat or
            GameUiStateId.Victory or
            GameUiStateId.StageSettlement or
            GameUiStateId.OdysseyStageVictory or
            GameUiStateId.OdysseySettlement or
            GameUiStateId.OdysseyReward;
    }

    private static bool HasDisplayableUiStateChanged(
        GameUiSnapshot? previous,
        GameUiSnapshot current)
    {
        if (previous is null || previous.State != current.State)
        {
            return true;
        }

        return current.State switch
        {
            GameUiStateId.InLevel => HasDisplayableStageStateChanged(previous.StageState, current.StageState),
            GameUiStateId.MapSearch => !Equals(ResolveRecognizedMap(previous), ResolveRecognizedMap(current)),
            _ => false
        };
    }

    private static bool HasDisplayableStageStateChanged(
        GameStageStateSnapshot? previous,
        GameStageStateSnapshot? current)
    {
        return previous?.Gold != current?.Gold ||
               previous?.Round != current?.Round ||
               HasDisplayableUpgradePanelChanged(previous?.LeftUpgradePanel, current?.LeftUpgradePanel) ||
               HasDisplayableUpgradePanelChanged(previous?.RightUpgradePanel, current?.RightUpgradePanel);
    }

    private static bool HasDisplayableUpgradePanelChanged(
        GameStageUpgradePanelState? previous,
        GameStageUpgradePanelState? current)
    {
        var wasVisible = previous?.IsVisible == true;
        var isVisible = current?.IsVisible == true;
        if (wasVisible != isVisible)
        {
            return true;
        }

        return isVisible &&
               (previous?.TopPathLevel != current?.TopPathLevel ||
                previous?.MiddlePathLevel != current?.MiddlePathLevel ||
                previous?.BottomPathLevel != current?.BottomPathLevel);
    }

    private static GameMapType? ResolveRecognizedMap(GameUiSnapshot snapshot)
    {
        if (snapshot.Facts.TryGetValue(MapSearchFlowState.CollectionMapFact, out var map) &&
            map is GameMapType collectionMap)
        {
            return collectionMap;
        }

        return snapshot.Facts.TryGetValue(MapSearchFlowState.GoldBalloonMapFact, out map) &&
               map is GameMapType goldBalloonMap
            ? goldBalloonMap
            : null;
    }

    private static void ObserveFreeplayTargetRound(
        AutoTaskRequest request,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot)
    {
        if (request.Kind != AutoTaskKind.LoopStage ||
            request.LoopStageRunMode != LoopStageRunMode.FreeplayUntilRound ||
            !state.TryGetProperty<LoopStageScriptRunState>(
                LoopStageAutoTaskStateKeys.ScriptRunState,
                out var runState) ||
            runState != LoopStageScriptRunState.RunningAfterBoundary ||
            (state.TryGetProperty<bool>(
                 LoopStageAutoTaskStateKeys.TargetRoundReached,
                 out var targetRoundReached) &&
             targetRoundReached) ||
            !state.TryGetProperty<LoopStageRoundProgressTracker>(
                LoopStageAutoTaskStateKeys.RoundProgressTracker,
                out var tracker))
        {
            return;
        }

        if (tracker.Observe(snapshot.StageState?.Round))
        {
            state.SetProperty(LoopStageAutoTaskStateKeys.TargetRoundReached, true);
        }
    }

    private AutoTaskRuntimeScriptPreview TryLoadScriptPreview(string filePath)
    {
        try
        {
            return _scriptPreviewService.Load(filePath);
        }
        catch
        {
            return new AutoTaskRuntimeScriptPreview
            {
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                Steps = Array.Empty<string>()
            };
        }
    }

    private static AutoTaskExecutionResult BuildFailedResult(
        AutoTaskExecutionSession session,
        AutoTaskRuntimeState state,
        string checkpoint,
        string message,
        Exception? exception = null)
    {
        session.MarkFailed(state.Phase, message);

        return new AutoTaskExecutionResult
        {
            Status = AutoTaskExecutionStatus.Failed,
            FinalProgress = session.GetSnapshot(),
            Exception = exception,
            Failure = new AutoTaskFailureDetails
            {
                Phase = state.Phase,
                UiState = state.LastUiSnapshot?.State ?? GameUiStateId.Unknown,
                Checkpoint = checkpoint,
                Attempt = state.ConsecutiveNavigationFailures,
                Message = message
            }
        };
    }
}
