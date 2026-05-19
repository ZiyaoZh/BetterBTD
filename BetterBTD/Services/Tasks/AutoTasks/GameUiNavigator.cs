using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class GameUiNavigator : IGameUiNavigator
{
    private static readonly Lazy<GameUiNavigator> InstanceHolder = new(() => new GameUiNavigator());

    private static readonly IReadOnlyList<GameUiStateId> MapSelectionStates =
    [
        GameUiStateId.MapCategorySelect,
        GameUiStateId.MapGrid,
        GameUiStateId.DifficultySelect,
        GameUiStateId.ModeSelect,
        GameUiStateId.InLevel
    ];

    private GameUiNavigator()
    {
    }

    public static GameUiNavigator Instance => InstanceHolder.Value;

    public GameUiNavigationStep GetNextStep(StageEntryTarget target, GameUiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.State switch
        {
            GameUiStateId.MainMenu => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.OpenMapSelection,
                Description = "Open map selection from the main menu.",
                ExpectedNextStates = MapSelectionStates
            },
            GameUiStateId.MapCategorySelect => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMapCategory,
                Description = "Select the map category that contains the target map.",
                ExpectedNextStates = [GameUiStateId.MapGrid, GameUiStateId.DifficultySelect]
            },
            GameUiStateId.MapSearch => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.Wait,
                Description = "Wait for map search interaction to be implemented.",
                PostActionDelayMs = 400,
                ExpectedNextStates = [GameUiStateId.MapSearchResults, GameUiStateId.MapGrid]
            },
            GameUiStateId.MapSearchResults => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMap,
                Description = "Select the searched map entry.",
                ExpectedNextStates = [GameUiStateId.DifficultySelect, GameUiStateId.EasyModeSelect, GameUiStateId.ModeSelect]
            },
            GameUiStateId.MapGrid => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMap,
                Description = "Select the target map entry.",
                ExpectedNextStates = [GameUiStateId.DifficultySelect, GameUiStateId.ModeSelect]
            },
            GameUiStateId.DifficultySelect => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectDifficulty,
                Description = "Select the target difficulty.",
                ExpectedNextStates = [GameUiStateId.EasyModeSelect, GameUiStateId.MediumModeSelect, GameUiStateId.HardModeSelect, GameUiStateId.ModeSelect, GameUiStateId.Loading, GameUiStateId.InLevel]
            },
            GameUiStateId.EasyModeSelect => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMode,
                Description = "Select the target easy-mode variant.",
                ExpectedNextStates = [GameUiStateId.Loading, GameUiStateId.InLevel]
            },
            GameUiStateId.MediumModeSelect => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMode,
                Description = "Select the target medium-mode variant.",
                ExpectedNextStates = [GameUiStateId.Loading, GameUiStateId.InLevel]
            },
            GameUiStateId.HardModeSelect => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMode,
                Description = "Select the target hard-mode variant.",
                ExpectedNextStates = [GameUiStateId.Loading, GameUiStateId.InLevel]
            },
            GameUiStateId.ModeSelect => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.SelectMode,
                Description = "Select the target mode and enter the stage.",
                ExpectedNextStates = [GameUiStateId.Loading, GameUiStateId.InLevel]
            },
            GameUiStateId.StageSettlement => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.CollectReward,
                Description = "Continue past the settlement screen.",
                ExpectedNextStates = [GameUiStateId.Victory, GameUiStateId.Reward, GameUiStateId.MainMenu]
            },
            GameUiStateId.Loading => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.Wait,
                Description = "Wait for the level to finish loading.",
                PostActionDelayMs = 600,
                ExpectedNextStates = [GameUiStateId.InLevel]
            },
            GameUiStateId.Victory => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.CollectReward,
                Description = "Continue past the victory screen.",
                ExpectedNextStates = [GameUiStateId.Reward, GameUiStateId.ConfirmDialog, GameUiStateId.MainMenu]
            },
            GameUiStateId.Defeat => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.RetryStage,
                Description = "Retry after defeat.",
                ExpectedNextStates = [GameUiStateId.Loading, GameUiStateId.InLevel, GameUiStateId.MainMenu]
            },
            GameUiStateId.Reward => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.CollectReward,
                Description = "Dismiss reward and continue.",
                ExpectedNextStates = [GameUiStateId.MainMenu, GameUiStateId.MapGrid, GameUiStateId.ConfirmDialog]
            },
            GameUiStateId.ConfirmDialog => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.ConfirmDialog,
                Description = "Confirm the blocking dialog.",
                ExpectedNextStates = [GameUiStateId.MainMenu, GameUiStateId.MapGrid, GameUiStateId.Loading]
            },
            GameUiStateId.ChestOpened or
            GameUiStateId.TwoChests or
            GameUiStateId.ThreeChests or
            GameUiStateId.LevelUp or
            GameUiStateId.StageHint or
            GameUiStateId.InstaMonkeyReward or
            GameUiStateId.RaceResult or
            GameUiStateId.BossResult => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.CollectReward,
                Description = "Dismiss the blocking overlay result screen.",
                ExpectedNextStates = [GameUiStateId.MainMenu, GameUiStateId.EventMenu, GameUiStateId.InLevel]
            },
            GameUiStateId.InLevel => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.None,
                Description = "Already inside the target stage.",
                PostActionDelayMs = 0,
                ExpectedNextStates = [GameUiStateId.InLevel]
            },
            _ => new GameUiNavigationStep
            {
                ActionKind = GameUiActionKind.Wait,
                Description = "Wait for a recognizable game UI state.",
                PostActionDelayMs = 500,
                ExpectedNextStates = [GameUiStateId.MainMenu, GameUiStateId.MapCategorySelect, GameUiStateId.InLevel]
            }
        };
    }
}
