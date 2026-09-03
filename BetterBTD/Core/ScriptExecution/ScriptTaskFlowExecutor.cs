using BetterBTD.Models.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Runtime;
using BetterBTD.Services.Tasks.ScriptExecution;
using BetterBTD.Core.ScriptExecution.Handlers;
using BetterBTD.Core.GameControl;
using BetterBTD.Services.ChildSession;
using System.Diagnostics;
using System.Linq;

namespace BetterBTD.Core.ScriptExecution;

public sealed class ScriptTaskFlowExecutor : IScriptTaskFlowExecutionEngine
{
    private static readonly Lazy<ScriptTaskFlowExecutor> InstanceHolder = new(() => new ScriptTaskFlowExecutor());

    private readonly object _syncRoot = new();
    private readonly ScriptTaskFlowService _scriptTaskFlowService;
    private readonly ScriptInstructionHandlerRegistry _handlerRegistry;
    private readonly GameControlLeaseCoordinator _gameControlLeaseCoordinator;

    private bool _isRunning;
    private ScriptExecutionSession? _currentSession;
    private CancellationTokenSource? _currentCancellationSource;

    private ScriptTaskFlowExecutor()
        : this(GameControlLeaseCoordinator.Instance)
    {
    }

    internal ScriptTaskFlowExecutor(GameControlLeaseCoordinator gameControlLeaseCoordinator)
    {
        _scriptTaskFlowService = ScriptTaskFlowService.Instance;
        _handlerRegistry = ScriptInstructionHandlerRegistry.Instance;
        _gameControlLeaseCoordinator = gameControlLeaseCoordinator ??
            throw new ArgumentNullException(nameof(gameControlLeaseCoordinator));
    }

    public static ScriptTaskFlowExecutor Instance => InstanceHolder.Value;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _isRunning;
            }
        }
    }

    public ScriptExecutionProgressSnapshot? CurrentProgress
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentSession?.GetSnapshot();
            }
        }
    }

    public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

    public event EventHandler<ScriptExecutionRuntimeLogEntry>? RuntimeLogEmitted;

    public bool RequestPause()
    {
        ScriptExecutionSession? session;
        lock (_syncRoot)
        {
            session = _currentSession;
        }

        return session?.RequestPause() == true;
    }

    public bool Resume()
    {
        ScriptExecutionSession? session;
        lock (_syncRoot)
        {
            session = _currentSession;
        }

        return session?.Resume() == true;
    }

    public bool RequestStop()
    {
        lock (_syncRoot)
        {
            if (_currentCancellationSource is null)
            {
                return false;
            }

            _currentCancellationSource.Cancel();
            return true;
        }
    }

    public Task<ScriptExecutionResult> ExecuteAsync(
        string filePath,
        ScriptExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var taskFlow = _scriptTaskFlowService.LoadFromFile(filePath);
        return ExecuteAsync(taskFlow, options, cancellationToken);
    }

    public async Task<ScriptExecutionResult> ExecuteAsync(
        ScriptTaskFlow taskFlow,
        ScriptExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskFlow);
        ChildSessionRuntimeState.EnsurePrimaryCanControl();

        using var gameControlScope = _gameControlLeaseCoordinator.AcquireOrJoinForScriptExecution();
        using var inputArbiterLease = AcquireInputArbiterLease();
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = linkedCancellationSource.Token;
        options ??= new ScriptExecutionOptions();
        var runtimeServices = options.RuntimeServices ?? ScriptExecutionRuntimeServiceFactory.CreateDefault();
        var executedStepCount = 0;
        var lastCompletedStepIndex = -1;
        var normalizedStartStepIndex = taskFlow.Steps.Count == 0
            ? 0
            : Math.Clamp(options.StartStepIndex, 0, taskFlow.Steps.Count);
        var normalizedEndStepIndexExclusive = options.EndStepIndexExclusive.HasValue
            ? Math.Clamp(options.EndStepIndexExclusive.Value, 0, taskFlow.Steps.Count)
            : taskFlow.Steps.Count;
        if (normalizedEndStepIndexExclusive < normalizedStartStepIndex)
        {
            throw new ArgumentException(
                "The script execution end step must be greater than or equal to the start step.",
                nameof(options));
        }
        var executionSession = new ScriptExecutionSession(taskFlow.SourceFilePath);

        EnterRunningState(executionSession, linkedCancellationSource);
        try
        {
            using var runtimeLogScope = ScriptExecutionRuntimeDiagnostics.PushLogger(executionSession.RuntimeLogger);
            ValidateRuntimePrerequisites(options, runtimeServices);
            executionSession.MarkStarted();

            var state = new ScriptExecutionState();
            state.SeedMonkeyStates(taskFlow.Document.MonkeyObjects);
            if (normalizedStartStepIndex > 0 && normalizedStartStepIndex < taskFlow.Steps.Count)
            {
                var startStep = taskFlow.Steps[normalizedStartStepIndex];
                executionSession.MarkContextBuilding(
                    startStep.Index,
                    startStep.CommandType.ToString(),
                    $"Building runtime context from {normalizedStartStepIndex} prior step(s).");

                var contextBuildSummary = ScriptExecutionRuntimeContextBuilder.Build(
                    taskFlow,
                    state,
                    normalizedStartStepIndex);

                await executionSession
                    .ReachCheckpointAsync(
                        "BuildRuntimeContextCompleted",
                        $"Built runtime context from {contextBuildSummary.ReplayedStepCount} prior step(s); applied {contextBuildSummary.MutatedStepCount} runtime state update(s).",
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var step in taskFlow.Steps
                         .Skip(normalizedStartStepIndex)
                         .TakeWhile(step => step.Index < normalizedEndStepIndexExclusive))
            {
                cancellationToken.ThrowIfCancellationRequested();
                executionSession.EnterStep(step.Index, step.CommandType.ToString());
                await executionSession
                    .ReachCheckpointAsync("BeforeInstruction", "Waiting to enter instruction handler.", null, cancellationToken)
                    .ConfigureAwait(false);

                var context = new ScriptInstructionExecutionContext
                {
                    TaskFlow = taskFlow,
                    Step = step,
                    State = state,
                    Options = options,
                    RuntimeServices = runtimeServices,
                    ExecutionSession = executionSession
                };

                var handler = _handlerRegistry.GetRequiredHandler(step.CommandType);
                await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);

                executedStepCount++;
                lastCompletedStepIndex = step.Index;
                executionSession.MarkStepCompleted(executedStepCount, lastCompletedStepIndex);

                var instructionIntervalMs = ResolveInstructionInterval(step, options);
                if (instructionIntervalMs > 0)
                {
                    await executionSession
                        .ReachCheckpointAsync("InstructionInterval", $"Waiting {instructionIntervalMs} ms before next instruction.", null, cancellationToken)
                        .ConfigureAwait(false);
                    await executionSession.DelayAsync(instructionIntervalMs, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            executionSession.MarkCompleted(executedStepCount, lastCompletedStepIndex);
            var finalProgress = executionSession.GetSnapshot();

            return new ScriptExecutionResult
            {
                Status = ScriptExecutionStatus.Completed,
                ExecutedStepCount = executedStepCount,
                LastCompletedStepIndex = lastCompletedStepIndex,
                FinalProgress = finalProgress
            };
        }
        catch (OperationCanceledException)
        {
            executionSession.MarkCancelled(executedStepCount, lastCompletedStepIndex);
            return new ScriptExecutionResult
            {
                Status = ScriptExecutionStatus.Cancelled,
                ExecutedStepCount = executedStepCount,
                LastCompletedStepIndex = lastCompletedStepIndex,
                FinalProgress = executionSession.GetSnapshot()
            };
        }
        catch (ScriptExecutionException ex)
        {
            executionSession.MarkFailed(executedStepCount, lastCompletedStepIndex, ex.Message);
            return new ScriptExecutionResult
            {
                Status = ScriptExecutionStatus.Failed,
                ExecutedStepCount = executedStepCount,
                LastCompletedStepIndex = lastCompletedStepIndex,
                Exception = ex,
                FinalProgress = executionSession.GetSnapshot(),
                Failure = new ScriptExecutionFailureDetails
                {
                    StepIndex = ex.StepIndex,
                    CommandType = ex.CommandType,
                    Checkpoint = ex.Checkpoint,
                    Attempt = ex.Attempt,
                    Message = ex.Message
                }
            };
        }
        catch (Exception ex)
        {
            executionSession.MarkFailed(executedStepCount, lastCompletedStepIndex, ex.Message);
            var currentProgress = executionSession.GetSnapshot();
            return new ScriptExecutionResult
            {
                Status = ScriptExecutionStatus.Failed,
                ExecutedStepCount = executedStepCount,
                LastCompletedStepIndex = lastCompletedStepIndex,
                Exception = ex,
                FinalProgress = currentProgress,
                Failure = new ScriptExecutionFailureDetails
                {
                    StepIndex = currentProgress.CurrentStepIndex,
                    CommandType = currentProgress.CurrentCommandType,
                    Checkpoint = currentProgress.CurrentCheckpoint,
                    Attempt = currentProgress.CurrentAttempt,
                    Message = ex.Message
                }
            };
        }
        finally
        {
            try
            {
                runtimeServices.Input.ReleaseAllKeys();
                gameControlScope.ConfirmInputReleased();
            }
            catch (Exception ex)
            {
                gameControlScope.MarkPoisoned();
                Debug.WriteLine($"Script input release failed; the control lease was poisoned: {ex}");
                ScriptExecutionRuntimeDiagnostics.Error(
                    ScriptExecutionRuntimeLogCategory.Action,
                    $"Input release failed during script cleanup: {ex.GetBaseException().Message}");
            }
            finally
            {
                executionSession.DisposeRuntimeLogging();
                ExitRunningState();
            }
        }
    }

    private static InputArbiterLease AcquireInputArbiterLease()
    {
        var context = GameInputArbiterContext.Current;
        if (context is null)
        {
            return InputArbiterLease.None;
        }

        var ownerId = $"script-{Guid.NewGuid():N}";
        if (context.Arbiter.TryAcquire(
                ownerId,
                GameInputPriority.Script,
                out var lease,
                context.NavigationLease.OwnerId))
        {
            return lease;
        }

        throw new InvalidOperationException("Unable to acquire the script input scope from the navigation controller.");
    }

    private static int ResolveInstructionInterval(ScriptTaskFlowStep step, ScriptExecutionOptions options)
    {
        return ScriptInstructionHandlerSupport.ResolveInstructionIntervalMilliseconds(step, options);
    }

    private static void ValidateRuntimePrerequisites(
        ScriptExecutionOptions options,
        ScriptExecutionRuntimeServices runtimeServices)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtimeServices);

        if (options.RequireCaptureService && !runtimeServices.Capture.IsRunning)
        {
            throw new InvalidOperationException("Game capture service must be running before executing a script task flow.");
        }

        if (options.RequireTargetWindow && !runtimeServices.Input.TryGetTargetWindowInfo(out _))
        {
            throw new InvalidOperationException("Target game window is not available for script execution.");
        }
    }

    private void EnterRunningState(
        ScriptExecutionSession executionSession,
        CancellationTokenSource cancellationSource)
    {
        ArgumentNullException.ThrowIfNull(executionSession);
        ArgumentNullException.ThrowIfNull(cancellationSource);

        lock (_syncRoot)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Another script task flow is already running.");
            }

            _isRunning = true;
            _currentSession = executionSession;
            _currentCancellationSource = cancellationSource;
            _currentSession.ProgressChanged += OnCurrentSessionProgressChanged;
            _currentSession.RuntimeLogAdded += OnCurrentSessionRuntimeLogAdded;
        }
    }

    private void ExitRunningState()
    {
        ScriptExecutionSession? session;
        lock (_syncRoot)
        {
            session = _currentSession;
            if (session is not null)
            {
                session.ProgressChanged -= OnCurrentSessionProgressChanged;
                session.RuntimeLogAdded -= OnCurrentSessionRuntimeLogAdded;
            }

            _currentSession = null;
            _currentCancellationSource = null;
            _isRunning = false;
        }
    }

    private void OnCurrentSessionProgressChanged(object? sender, ScriptExecutionProgressSnapshot snapshot)
    {
        ProgressChanged?.Invoke(this, snapshot);
    }

    private void OnCurrentSessionRuntimeLogAdded(object? sender, ScriptExecutionRuntimeLogEntry entry)
    {
        RuntimeLogEmitted?.Invoke(this, entry);
    }
}
