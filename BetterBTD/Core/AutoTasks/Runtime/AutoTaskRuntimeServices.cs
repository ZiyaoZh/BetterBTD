using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;
using OpenCvSharp;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Core.AutoTasks.Runtime;

public sealed class GameUiRecognitionContext
{
    public required DateTimeOffset CapturedAt { get; init; }

    public required GameWindowInfo WindowInfo { get; init; }

    public required Mat Frame { get; init; }

    public required GameStageStateSnapshot? StageState { get; init; }
}

public interface IGameUiRecognizer
{
    int Priority { get; }

    bool TryRecognize(GameUiRecognitionContext context, out GameUiSnapshot snapshot);
}

public interface IGameUiStateService
{
    Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IGameUiNavigator
{
    GameUiNavigationStep GetNextStep(StageEntryTarget target, GameUiSnapshot snapshot);
}

public interface IGameUiActionExecutor
{
    Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public interface IGameUiElementLocator
{
    bool TryLocateScriptPoint(
        GameUiActionKind actionKind,
        StageEntryTarget target,
        GameUiSnapshot snapshot,
        out WpfPoint scriptPoint,
        out string failureMessage);
}

public interface IAutoTaskScriptResolver
{
    Task<AutoTaskScriptResolution> ResolveAsync(
        AutoTaskScriptQuery query,
        AutoTaskRuntimeState state,
        CancellationToken cancellationToken = default);
}

public interface IAutoTaskScriptExecutor
{
    event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged;

    bool IsRunning { get; }

    bool RequestPause();

    bool Resume();

    Task<ScriptExecutionResult> ExecuteAsync(
        string filePath,
        ScriptExecutionOptions options,
        CancellationToken cancellationToken = default);
}

public interface IAutoTaskStrategy
{
    AutoTaskKind Kind { get; }

    Task<AutoTaskDecision> DecideNextAsync(
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public interface IAutoTaskStrategyRegistry
{
    IAutoTaskStrategy GetRequiredStrategy(AutoTaskKind kind);
}

public sealed class AutoTaskRuntimeServices
{
    public required IGameUiStateService GameUiState { get; init; }

    public required IGameUiNavigator Navigator { get; init; }

    public required IGameUiActionExecutor UiActionExecutor { get; init; }

    public required IAutoTaskScriptResolver ScriptResolver { get; init; }

    public required IAutoTaskScriptExecutor ScriptExecutor { get; init; }
}
