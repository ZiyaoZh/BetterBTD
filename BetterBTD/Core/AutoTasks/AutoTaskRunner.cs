using System.IO;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Core.AutoTasks;

public sealed class AutoTaskRunner
{
    private readonly IAutoTaskStrategyRegistry _strategyRegistry;
    private readonly AutoTaskRuntimeServices _defaultRuntimeServices;
    private readonly AutoTaskRuntimeScriptPreviewService _scriptPreviewService;

    private AutoTaskExecutionSession? _currentSession;
    private IAutoTaskScriptExecutor? _currentScriptExecutor;
    private int _stuckTrackingGeneration;

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
        if (sessionResumed || scriptResumed)
        {
            Interlocked.Increment(ref _stuckTrackingGeneration);
        }

        return sessionResumed || scriptResumed;
    }

    public async Task<AutoTaskExecutionResult> ExecuteAsync(
        AutoTaskRequest request,
        AutoTaskExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        options ??= new AutoTaskExecutionOptions();
        if (options.WorkerAcknowledgementTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Worker acknowledgement timeout must be positive.");
        }
        if (options.ScriptOffLevelGracePeriod < TimeSpan.Zero ||
            options.ScriptRecoveryTimeout <= TimeSpan.Zero ||
            options.ScriptRecoveryClickInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Script recovery timings must be non-negative and the recovery timeout must be positive.");
        }

        var runtimeServices = options.RuntimeServices ?? _defaultRuntimeServices;
        var result = await ExecuteCoreAsync(request, options, runtimeServices, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == AutoTaskExecutionStatus.Failed &&
            runtimeServices.FailureArtifactWriter is not null)
        {
            try
            {
                await runtimeServices.FailureArtifactWriter
                    .WriteAsync(result, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to write auto-task failure artifacts: {ex}");
            }
        }

        return result;
    }

    private async Task<AutoTaskExecutionResult> ExecuteCoreAsync(
        AutoTaskRequest request,
        AutoTaskExecutionOptions options,
        AutoTaskRuntimeServices runtimeServices,
        CancellationToken cancellationToken)
    {
        var strategy = _strategyRegistry.GetRequiredStrategy(request.Kind);
        var state = new AutoTaskRuntimeState(request);
        var stuckUiTracker = ShouldUseStuckUiRecovery(request.Kind)
            ? new AutoTaskStuckUiTracker(
                options.StuckUiTimeout,
                options.VisualFingerprintDistanceTolerance)
            : null;
        var observedStuckTrackingGeneration = Volatile.Read(ref _stuckTrackingGeneration);
        var session = new AutoTaskExecutionSession(
            string.IsNullOrWhiteSpace(request.Key) ? request.Kind.ToKey() : request.Key,
            request.Kind);
        var navigationObservation = runtimeServices.NavigationObservation;
        IAsyncEnumerator<NavigationObservation>? navigationSnapshots = null;
        var lastNavigationSequence = navigationObservation?.LatestObservation?.Sequence ?? 0;

        runtimeServices.GameUiState.ResetStabilizationState();
        _currentSession = session;
        _currentScriptExecutor = runtimeServices.ScriptExecutor;

        session.MarkStarted(state.Phase, "Auto task execution started.");

        try
        {
            if (navigationObservation is not null)
            {
                navigationObservation.Start(cancellationToken);
                navigationSnapshots = navigationObservation
                    .SubscribeAsync(cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
            }

            async Task<GameUiSnapshot> ObserveNextUiSnapshotAsync(CancellationToken token)
            {
                if (navigationSnapshots is null)
                {
                    return await runtimeServices.GameUiState
                        .CaptureSnapshotAsync(token)
                        .ConfigureAwait(false);
                }

                while (await navigationSnapshots.MoveNextAsync().ConfigureAwait(false))
                {
                    var observation = navigationSnapshots.Current;
                    if (observation.Sequence <= lastNavigationSequence)
                    {
                        continue;
                    }

                    lastNavigationSequence = observation.Sequence;
                    return observation.Snapshot;
                }

                throw new InvalidOperationException("Navigation observation stream ended unexpectedly.");
            }

            while (options.MaxLoopIterations is null || state.LoopIteration < options.MaxLoopIterations.Value)
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

                var snapshot = await ObserveNextUiSnapshotAsync(cancellationToken).ConfigureAwait(false);

                state.RecordUiSnapshot(snapshot);
                session.UpdateUiSnapshot(snapshot, $"Detected UI state '{snapshot.State}'.");
                UpdateStageCompletion(state, session, snapshot);

                var currentStuckTrackingGeneration = Volatile.Read(ref _stuckTrackingGeneration);
                if (observedStuckTrackingGeneration != currentStuckTrackingGeneration)
                {
                    stuckUiTracker?.Reset();
                    observedStuckTrackingGeneration = currentStuckTrackingGeneration;
                }

                if (stuckUiTracker?.Observe(snapshot, state.Phase, state.CompletedStageCount) == true)
                {
                    if (snapshot.State == GameUiStateId.Loading)
                    {
                        return BuildFailedResult(
                            session,
                            state,
                            "StuckUiRecovery",
                            $"UI state '{snapshot.State}' did not change for {options.StuckUiTimeout.TotalSeconds:F1} seconds. Recovery clicks are disabled while loading.");
                    }

                    var recoveryFailure = await TryRecoverStuckUiAsync(
                            runtimeServices,
                            session,
                            state,
                            snapshot,
                            options,
                            ObserveNextUiSnapshotAsync,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (recoveryFailure is not null)
                    {
                        return BuildFailedResult(
                            session,
                            state,
                            "StuckUiRecovery",
                            recoveryFailure,
                            attempt: options.StuckRecoveryPoints.Count);
                    }

                    stuckUiTracker.Reset();
                    continue;
                }

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
                            if (navigationObservation is not null &&
                                runtimeServices.ScriptWorker is { } scriptWorker)
                            {
                                var controllerResult = await ExecuteScriptViaNavigationControllerAsync(
                                        navigationObservation,
                                        scriptWorker,
                                        runtimeServices.StuckRecoveryExecutor,
                                        scriptResolution.FilePath,
                                        scriptExecutionOptions,
                                        options,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                                scriptResult = controllerResult.ScriptResult;
                                scriptInterruptedSnapshot = controllerResult.HandoffSnapshot;
                            }
                            else
                            {
                                return BuildFailedResult(
                                    session,
                                    state,
                                    "NavigationController",
                                    "Auto-task runtime is missing the navigation observation service or script worker.");
                            }
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
            if (navigationSnapshots is not null)
            {
                await navigationSnapshots.DisposeAsync().ConfigureAwait(false);
            }

            if (navigationObservation is not null)
            {
                await navigationObservation.StopAsync().ConfigureAwait(false);
            }

            runtimeServices.GameUiState.ResetStabilizationState();
            _currentScriptExecutor = null;
            _currentSession = null;
        }
    }

    private static int ResolveDelay(int delayMs, AutoTaskExecutionOptions options)
    {
        return delayMs > 0 ? delayMs : options.DefaultDecisionDelayMs;
    }

    private static async Task<AutoTaskNavigationControllerResult> ExecuteScriptViaNavigationControllerAsync(
        INavigationObservationService observations,
        IScriptTaskFlowWorker worker,
        IGameUiStuckRecoveryExecutor? recoveryExecutor,
        string scriptFilePath,
        ScriptExecutionOptions scriptOptions,
        AutoTaskExecutionOptions executionOptions,
        CancellationToken cancellationToken)
    {
        var controller = new AutoTaskNavigationController(
            observations,
            worker,
            recoveryExecutor,
            acknowledgementTimeout: executionOptions.WorkerAcknowledgementTimeout,
            offLevelGracePeriod: executionOptions.ScriptOffLevelGracePeriod,
            recoveryTimeout: executionOptions.ScriptRecoveryTimeout,
            recoveryClickInterval: executionOptions.ScriptRecoveryClickInterval,
            recoveryPoint: executionOptions.ScriptRecoveryPoint);
        return await controller.RunAsync(scriptFilePath, scriptOptions, cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldUseStuckUiRecovery(AutoTaskKind kind)
    {
        return kind is AutoTaskKind.Collection
            or AutoTaskKind.GoldBalloon
            or AutoTaskKind.BlackBorder
            or AutoTaskKind.LoopStage
            or AutoTaskKind.Race;
    }

    private static async Task<string?> TryRecoverStuckUiAsync(
        AutoTaskRuntimeServices runtimeServices,
        AutoTaskExecutionSession session,
        AutoTaskRuntimeState state,
        GameUiSnapshot stuckSnapshot,
        AutoTaskExecutionOptions options,
        Func<CancellationToken, Task<GameUiSnapshot>> observeNextUiSnapshotAsync,
        CancellationToken cancellationToken)
    {
        if (runtimeServices.StuckRecoveryExecutor is null)
        {
            return $"UI state '{stuckSnapshot.State}' is stuck, but no recovery executor is configured.";
        }

        if (options.StuckRecoveryPoints.Count == 0)
        {
            return $"UI state '{stuckSnapshot.State}' is stuck, but no recovery points are configured.";
        }

        for (var index = 0; index < options.StuckRecoveryPoints.Count; index++)
        {
            var point = options.StuckRecoveryPoints[index];
            var attempt = index + 1;
            await session
                .ReachCheckpointAsync(
                    "StuckUiRecovery",
                    AutoTaskActivityKind.Navigating,
                    $"Trying stuck-UI recovery point {attempt}/{options.StuckRecoveryPoints.Count} at ({point.X}, {point.Y}).",
                    attempt,
                    cancellationToken)
                .ConfigureAwait(false);

            await runtimeServices.StuckRecoveryExecutor
                .ClickAsync(point, cancellationToken)
                .ConfigureAwait(false);
            await session
                .DelayAsync(Math.Max(0, options.StuckRecoveryDelayMs), cancellationToken)
                .ConfigureAwait(false);

            var recoveredSnapshot = await observeNextUiSnapshotAsync(cancellationToken).ConfigureAwait(false);
            state.RecordUiSnapshot(recoveredSnapshot);
            session.UpdateUiSnapshot(
                recoveredSnapshot,
                $"Observed UI state '{recoveredSnapshot.State}' after stuck-recovery attempt {attempt}.");

            if (!AutoTaskStuckUiTracker.IsSameInterface(
                    stuckSnapshot,
                    recoveredSnapshot,
                    options.VisualFingerprintDistanceTolerance))
            {
                return null;
            }
        }

        return $"UI state '{stuckSnapshot.State}' did not change for {options.StuckUiTimeout.TotalSeconds:F1} seconds and remained unchanged after {options.StuckRecoveryPoints.Count} recovery clicks.";
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
        Exception? exception = null,
        int? attempt = null)
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
                Attempt = attempt ?? state.ConsecutiveNavigationFailures,
                Message = message
            }
        };
    }
}
