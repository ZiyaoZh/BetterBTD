using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Tasks.CaptureAnalysis;
using BetterBTD.Services.Tasks.Input;
using WpfPoint = System.Windows.Point;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal sealed class RaceGameUiActionHandler : AutoTaskGameUiActionHandlerBase
{
    public RaceGameUiActionHandler(
        ScriptInputSimulationService inputSimulationService,
        GameCaptureService gameCaptureService,
        GameUiNavigationOcrService navigationOcrService)
        : base(inputSimulationService, gameCaptureService, navigationOcrService)
    {
    }

    public override AutoTaskKind Kind => AutoTaskKind.Race;

    public override Task<GameUiActionExecutionResult> ExecuteAsync(
        GameUiNavigationStep step,
        AutoTaskRuntimeState state,
        GameUiSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = snapshot.State switch
        {
            GameUiStateId.InLevel => Success(
                step,
                "Race stage is active. Waiting for the configured script lifecycle.",
                step.PostActionDelayMs),
            GameUiStateId.StageSettlement => Click(
                step,
                new WpfPoint(1340, 850),
                "Advanced past the race settlement screen."),
            GameUiStateId.StageHint or GameUiStateId.NetworkUnavailableDialog => Click(
                step,
                new WpfPoint(1135, 730),
                "Dismissed the race stage hint."),
            GameUiStateId.Defeat => Click(
                step,
                new WpfPoint(850, 810),
                "Confirmed the race defeat screen."),
            GameUiStateId.StageSettings => Click(
                step,
                new WpfPoint(960, 840),
                "Confirmed the race stage settings screen."),
            GameUiStateId.LevelUp => Click(
                step,
                new WpfPoint(960, 840),
                "Confirmed the level-up prompt."),
            GameUiStateId.InstaMonkeyReward => Click(
                step,
                new WpfPoint(960, 540),
                "Confirmed the Insta Monkey reward."),
            GameUiStateId.Loading => Success(
                step,
                "Waiting for the race stage to finish loading.",
                step.PostActionDelayMs),
            _ => new GameUiActionExecutionResult
            {
                Succeeded = false,
                Message = $"Race action executor does not handle UI state '{snapshot.State}'.",
                RecommendedDelayMs = step.PostActionDelayMs
            }
        };

        return Task.FromResult(result);
    }
}
