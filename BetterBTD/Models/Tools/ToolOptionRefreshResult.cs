using BetterBTD.Models;

namespace BetterBTD.Models.Tools;

public sealed class ToolOptionRefreshResult
{
    public required IReadOnlyList<LanguageOption> Options { get; init; }

    public LanguageOption? SelectedOption { get; init; }
}
