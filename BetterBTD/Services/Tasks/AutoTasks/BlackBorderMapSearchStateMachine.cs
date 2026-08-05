using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Services.Tasks.AutoTasks;

internal static class BlackBorderMapSearchStateMachine
{
    internal const int MaxPages = 10;

    internal static void EnsureCurrentContext(
        AutoTaskRuntimeState state,
        BlackBorderAutoTaskScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        var signature = BuildSignature(context);
        if (state.TryGetProperty<string>(BlackBorderAutoTaskStateKeys.MapSearchSignature, out var storedSignature) &&
            string.Equals(storedSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        ResetSearchProgress(state);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchSignature, signature);
    }

    internal static bool IsCategorySelected(AutoTaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.TryGetProperty<bool>(BlackBorderAutoTaskStateKeys.MapSearchCategorySelected, out var selected) &&
               selected;
    }

    internal static void MarkCategorySelected(AutoTaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchCategorySelected, true);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchPageIndex, 1);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, 0);
    }

    internal static BlackBorderMapSearchMissDecision CreateMissDecision(AutoTaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var pageIndex = GetCurrentPageIndex(state);
        var attempts = state.TryGetProperty<int>(BlackBorderAutoTaskStateKeys.MapLocateAttempts, out var currentAttempts)
            ? currentAttempts + 1
            : 1;
        var isTerminal = pageIndex >= MaxPages;
        return new BlackBorderMapSearchMissDecision(
            pageIndex,
            attempts,
            isTerminal,
            isTerminal ? pageIndex : pageIndex + 1);
    }

    internal static void MarkPageAdvanced(
        AutoTaskRuntimeState state,
        BlackBorderMapSearchMissDecision decision)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (decision.IsTerminal)
        {
            throw new ArgumentException("Terminal map search decisions cannot advance to another page.", nameof(decision));
        }

        state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, decision.Attempts);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchPageIndex, decision.NextPageIndex);
    }

    internal static void MarkSearchExhausted(AutoTaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.RemoveProperty(BlackBorderAutoTaskStateKeys.ResolvedScriptContext);
        state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, false);
        state.SetProperty(BlackBorderAutoTaskStateKeys.SkipCurrentTaskRequested, true);
        ResetSearchProgress(state);
    }

    internal static int MarkMapFound(AutoTaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var pageIndex = GetCurrentPageIndex(state);
        state.SetProperty(BlackBorderAutoTaskStateKeys.HeroSelected, false);
        ResetSearchProgress(state);
        return pageIndex;
    }

    internal static int GetCurrentPageIndex(AutoTaskRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.TryGetProperty<int>(BlackBorderAutoTaskStateKeys.MapSearchPageIndex, out var pageIndex)
            ? Math.Max(1, pageIndex)
            : 1;
    }

    private static void ResetSearchProgress(AutoTaskRuntimeState state)
    {
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchCategorySelected, false);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapSearchPageIndex, 0);
        state.SetProperty(BlackBorderAutoTaskStateKeys.MapLocateAttempts, 0);
    }

    private static string BuildSignature(BlackBorderAutoTaskScriptContext context)
    {
        return $"{context.Category}|{context.Target.Map}|{context.Target.Difficulty}|{context.Target.Mode}";
    }
}

internal readonly record struct BlackBorderMapSearchMissDecision(
    int PageIndex,
    int Attempts,
    bool IsTerminal,
    int NextPageIndex);
