using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class GameUiActionExecutor : IGameUiActionExecutor
{
    private static readonly Lazy<GameUiActionExecutor> InstanceHolder = new(() => new GameUiActionExecutor());

    private readonly ScriptInputSimulationService _inputSimulationService;
    private readonly IGameUiElementLocator _elementLocator;
    private readonly IReadOnlyDictionary<AutoTaskKind, IGameUiTaskActionHandler> _taskHandlers;

    private GameUiActionExecutor()
        : this(
            ScriptInputSimulationService.Instance,
            UnimplementedGameUiElementLocator.Instance,
            GameCaptureService.Instance,
            GameUiNavigationOcrService.Instance)
    {
    }

    internal GameUiActionExecutor(
        ScriptInputSimulationService inputSimulationService,
        IGameUiElementLocator elementLocator,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
    {
        _inputSimulationService = inputSimulationService ?? throw new ArgumentNullException(nameof(inputSimulationService));
        _elementLocator = elementLocator ?? throw new ArgumentNullException(nameof(elementLocator));

        var collectionHandler = new CollectionGameUiActionHandler(inputSimulationService, gameCaptureService, navigationOcrService);
        var goldBalloonHandler = new GoldBalloonGameUiActionHandler(inputSimulationService, gameCaptureService, navigationOcrService);
        var blackBorderHandler = new BlackBorderGameUiActionHandler(inputSimulationService, gameCaptureService, navigationOcrService);
        var loopStageHandler = new LoopStageGameUiActionHandler(inputSimulationService, gameCaptureService, navigationOcrService);
        var odysseyHandler = new OdysseyGameUiActionHandler(inputSimulationService, gameCaptureService, navigationOcrService);
        var raceHandler = new RaceGameUiActionHandler(inputSimulationService, gameCaptureService, navigationOcrService);
        _taskHandlers = new Dictionary<AutoTaskKind, IGameUiTaskActionHandler>
        {
            [AutoTaskKind.Collection] = collectionHandler,
            [AutoTaskKind.GoldBalloon] = goldBalloonHandler,
            [AutoTaskKind.BlackBorder] = blackBorderHandler,
            [AutoTaskKind.LoopStage] = loopStageHandler,
            [AutoTaskKind.Odyssey] = odysseyHandler,
            [AutoTaskKind.Race] = raceHandler
        };
    }

    public static GameUiActionExecutor Instance => InstanceHolder.Value;

    public async Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        cancellationToken.ThrowIfCancellationRequested();

        if (_taskHandlers.TryGetValue(state.Request.Kind, out var handler))
        {
            return await handler.ExecuteAsync(step, state, snapshot, cancellationToken).ConfigureAwait(false);
        }

        if (step.ActionKind is GameUiActionKind.None or GameUiActionKind.Wait)
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = true,
                Message = step.Description,
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        if (!_elementLocator.TryLocateScriptPoint(
                step.ActionKind,
                state.Request.StageTarget,
                snapshot,
                out var scriptPoint,
                out var failureMessage))
        {
            return new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = string.IsNullOrWhiteSpace(failureMessage)
                    ? $"No locator is available for action '{step.ActionKind}'."
                    : failureMessage,
                RecommendedDelayMs = step.PostActionDelayMs
            };
        }

        _inputSimulationService.PrepareTargetWindowForInput();
        _inputSimulationService.ClickMouseAtScriptCoordinate(scriptPoint);

        return new GameUiActionExecutionResult
        {
            Succeeded = true,
            Message = step.Description,
            RecommendedDelayMs = step.PostActionDelayMs
        };
    }
}
