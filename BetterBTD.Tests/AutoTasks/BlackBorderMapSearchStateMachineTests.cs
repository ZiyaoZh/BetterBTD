using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.MyScripts;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD.Tests.AutoTasks;

public sealed class BlackBorderMapSearchStateMachineTests
{
    [Fact]
    public void MarkCategorySelected_StartsSearchOnFirstPage()
    {
        var state = CreateState();

        BlackBorderMapSearchStateMachine.EnsureCurrentContext(state, CreateContext());
        BlackBorderMapSearchStateMachine.MarkCategorySelected(state);

        Assert.True(BlackBorderMapSearchStateMachine.IsCategorySelected(state));
        Assert.Equal(1, BlackBorderMapSearchStateMachine.GetCurrentPageIndex(state));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapLocateAttempts));
    }

    [Fact]
    public void CreateMissDecision_AdvancesPagesAndTerminatesAtMaximumPage()
    {
        var state = CreateState();

        BlackBorderMapSearchStateMachine.EnsureCurrentContext(state, CreateContext());
        BlackBorderMapSearchStateMachine.MarkCategorySelected(state);

        var firstMiss = BlackBorderMapSearchStateMachine.CreateMissDecision(state);

        Assert.False(firstMiss.IsTerminal);
        Assert.Equal(1, firstMiss.PageIndex);
        Assert.Equal(1, firstMiss.Attempts);
        Assert.Equal(2, firstMiss.NextPageIndex);

        BlackBorderMapSearchStateMachine.MarkPageAdvanced(state, firstMiss);

        Assert.Equal(2, BlackBorderMapSearchStateMachine.GetCurrentPageIndex(state));
        Assert.Equal(1, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapLocateAttempts));

        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchPageIndex, BlackBorderMapSearchStateMachine.MaxPages);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, BlackBorderMapSearchStateMachine.MaxPages - 1);
        state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, true);

        var terminalMiss = BlackBorderMapSearchStateMachine.CreateMissDecision(state);

        Assert.True(terminalMiss.IsTerminal);
        Assert.Equal(BlackBorderMapSearchStateMachine.MaxPages, terminalMiss.PageIndex);
        Assert.Equal(BlackBorderMapSearchStateMachine.MaxPages, terminalMiss.Attempts);
        Assert.Equal(BlackBorderMapSearchStateMachine.MaxPages, terminalMiss.NextPageIndex);

        BlackBorderMapSearchStateMachine.MarkSearchExhausted(state);

        Assert.False(BlackBorderMapSearchStateMachine.IsCategorySelected(state));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapSearchPageIndex));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapLocateAttempts));
        Assert.False(GetRequiredProperty<bool>(state, BlackBorderAutoTaskStateKeys.HeroSelected));
        Assert.True(GetRequiredProperty<bool>(state, BlackBorderAutoTaskStateKeys.SkipCurrentTaskRequested));
        Assert.False(state.TryGetProperty<BlackBorderAutoTaskScriptContext>(
            BlackBorderAutoTaskStateKeys.ResolvedScriptContext,
            out _));
    }

    [Fact]
    public void EnsureCurrentContext_ResetsSearchProgressWhenTargetChanges()
    {
        var state = CreateState();

        BlackBorderMapSearchStateMachine.EnsureCurrentContext(state, CreateContext(GameMapType.MonkeyMeadow));
        BlackBorderMapSearchStateMachine.MarkCategorySelected(state);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchPageIndex, 4);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, 3);

        BlackBorderMapSearchStateMachine.EnsureCurrentContext(state, CreateContext(GameMapType.Logs));

        Assert.False(BlackBorderMapSearchStateMachine.IsCategorySelected(state));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapSearchPageIndex));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapLocateAttempts));
    }

    [Fact]
    public void MarkMapFound_OnLaterPage_ReturnsPageAndResetsSearchProgress()
    {
        var state = CreateState();

        BlackBorderMapSearchStateMachine.EnsureCurrentContext(state, CreateContext());
        BlackBorderMapSearchStateMachine.MarkCategorySelected(state);
        BlackBorderMapSearchStateMachine.MarkPageAdvanced(
            state,
            BlackBorderMapSearchStateMachine.CreateMissDecision(state));
        BlackBorderMapSearchStateMachine.MarkPageAdvanced(
            state,
            BlackBorderMapSearchStateMachine.CreateMissDecision(state));
        state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, true);

        var foundPage = BlackBorderMapSearchStateMachine.MarkMapFound(state);

        Assert.Equal(3, foundPage);
        Assert.False(BlackBorderMapSearchStateMachine.IsCategorySelected(state));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapSearchPageIndex));
        Assert.Equal(0, GetRequiredProperty<int>(state, BlackBorderAutoTaskStateKeys.MapLocateAttempts));
        Assert.False(GetRequiredProperty<bool>(state, BlackBorderAutoTaskStateKeys.HeroSelected));
        Assert.False(state.TryGetProperty<bool>(BlackBorderAutoTaskStateKeys.SkipCurrentTaskRequested, out _));
        Assert.True(state.TryGetProperty<BlackBorderAutoTaskScriptContext>(
            BlackBorderAutoTaskStateKeys.ResolvedScriptContext,
            out _));
    }

    private static AutoTaskRuntimeState CreateState()
    {
        var context = CreateContext();
        var state = new AutoTaskRuntimeState(new AutoTaskRequest
        {
            Kind = AutoTaskKind.BlackBorder,
            StageTarget = context.Target
        });
        state.SetProperty(BlackBorderAutoTaskStateKeys.ResolvedScriptContext, context);
        return state;
    }

    private static BlackBorderAutoTaskScriptContext CreateContext(GameMapType map = GameMapType.MonkeyMeadow)
    {
        return new BlackBorderAutoTaskScriptContext
        {
            Category = BlackBorderMapCategory.Beginner,
            Target = new StageEntryTarget
            {
                Map = map,
                Difficulty = StageDifficulty.Easy,
                Mode = StageMode.Standard
            },
            Hero = HeroType.Quincy,
            FilePath = "black-border-test.json"
        };
    }

    private static T GetRequiredProperty<T>(AutoTaskRuntimeState state, string key)
    {
        Assert.True(state.TryGetProperty<T>(key, out var value), $"Expected state property '{key}'.");
        return value;
    }
}
