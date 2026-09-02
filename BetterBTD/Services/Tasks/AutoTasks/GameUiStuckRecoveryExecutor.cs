using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services.Tasks.Input;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class GameUiStuckRecoveryExecutor : IGameUiStuckRecoveryExecutor
{
    private static readonly Lazy<GameUiStuckRecoveryExecutor> InstanceHolder =
        new(() => new GameUiStuckRecoveryExecutor());

    private readonly ScriptInputSimulationService _inputSimulationService;

    private GameUiStuckRecoveryExecutor()
        : this(ScriptInputSimulationService.Instance)
    {
    }

    internal GameUiStuckRecoveryExecutor(ScriptInputSimulationService inputSimulationService)
    {
        _inputSimulationService = inputSimulationService ?? throw new ArgumentNullException(nameof(inputSimulationService));
    }

    public static GameUiStuckRecoveryExecutor Instance => InstanceHolder.Value;

    public Task<GameUiActionExecutionResult> ClickAsync(
        GameUiRecoveryPoint point,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _inputSimulationService.PrepareTargetWindowForInput();
        _inputSimulationService.ClickMouseAtScriptCoordinate(new WpfPoint(point.X, point.Y));

        return Task.FromResult(new GameUiActionExecutionResult
        {
            Succeeded = true,
            Message = $"Clicked stuck-UI recovery point ({point.X}, {point.Y})."
        });
    }
}
