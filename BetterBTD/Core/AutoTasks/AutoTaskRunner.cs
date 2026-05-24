using System.IO;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Core.AutoTasks;

public sealed class AutoTaskRunner
{
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
                    .ReachCheckpointAsync("CaptureUiState", "Capturing current game UI state.", null, cancellationToken)
                    .ConfigureAwait(false);

                var snapshot = await runtimeServices.GameUiState
                    .CaptureSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);

                state.RecordUiSnapshot(snapshot);
                session.UpdateUiSnapshot(snapshot, $"Detected UI state '{snapshot.State}'.");

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
                            .ReachCheckpointAsync("Wait", decision.Description, null, cancellationToken)
                            .ConfigureAwait(false);
                        await session
                            .DelayAsync(ResolveDelay(decision.DelayMs, options), cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case AutoTaskDecisionKind.Navigate:
                        var step = runtimeServices.Navigator.GetNextStep(request.StageTarget, snapshot);
                        await session
                            .ReachCheckpointAsync("Navigate", step.Description, null, cancellationToken)
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
                            .ReachCheckpointAsync("ResolveScript", decision.Description, null, cancellationToken)
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

                        var scriptPreview = TryLoadScriptPreview(scriptResolution.FilePath);
                        session.UpdateActiveScript(
                            scriptResolution.FilePath,
                            scriptPreview.DisplayName,
                            scriptPreview.Steps,
                            "Resolved auto-task script.");

                        var scriptExecutionOptions = new ScriptExecutionOptions
                        {
                            IntervalStrategy = ScriptExecutionOperationIntervalStrategy.CommonOperationInterval,
                            CommonOperationIntervalMs = Math.Max(0, request.OperationIntervalMs),
                            RequireCaptureService = true,
                            RequireTargetWindow = true
                        };

                        EventHandler<ScriptExecutionProgressSnapshot>? scriptProgressHandler = (_, progressSnapshot) =>
                            session.UpdateActiveScriptProgress(progressSnapshot);

                        runtimeServices.ScriptExecutor.ProgressChanged += scriptProgressHandler;

                        ScriptExecutionResult scriptResult;
                        try
                        {
                            scriptResult = await runtimeServices.ScriptExecutor
                                .ExecuteAsync(scriptResolution.FilePath, scriptExecutionOptions, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            runtimeServices.ScriptExecutor.ProgressChanged -= scriptProgressHandler;
                        }

                        if (scriptResult.Status == ScriptExecutionStatus.Cancelled)
                        {
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
                        session.MarkPhase(state.Phase, "Underlying script completed. Continue auto-task state flow.");
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
            _currentScriptExecutor = null;
            _currentSession = null;
        }
    }

    private static int ResolveDelay(int delayMs, AutoTaskExecutionOptions options)
    {
        return delayMs > 0 ? delayMs : options.DefaultDecisionDelayMs;
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
