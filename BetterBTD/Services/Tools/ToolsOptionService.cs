using BetterBTD.Models;
using BetterBTD.Models.GameElements;
using BetterBTD.Models.Tools;

namespace BetterBTD.Services.Tools;

public sealed class ToolsOptionService
{
    private static readonly Lazy<ToolsOptionService> InstanceHolder = new(() => new ToolsOptionService());

    private ToolsOptionService()
    {
    }

    public static ToolsOptionService Instance => InstanceHolder.Value;

    public ToolOptionRefreshResult BuildHeroOptions(string? selectedCode)
    {
        var options = GameElementCatalog.Heroes
            .Select(hero => new LanguageOption
            {
                Code = hero.Type.ToString(),
                DisplayName = GameElementCatalog.GetHeroDisplayName(hero.Type)
            })
            .ToArray();

        return new ToolOptionRefreshResult
        {
            Options = options,
            SelectedOption = SelectOption(options, selectedCode) ?? options.FirstOrDefault()
        };
    }

    public ToolOptionRefreshResult BuildParagonMonkeyOptions(string? selectedCode)
    {
        var options = GameElementCatalog.MonkeyTowers
            .Select(monkey => new LanguageOption
            {
                Code = monkey.Type.ToString(),
                DisplayName = GameElementCatalog.GetMonkeyTowerDisplayName(monkey.Type)
            })
            .ToArray();

        return new ToolOptionRefreshResult
        {
            Options = options,
            SelectedOption = SelectOption(options, selectedCode) ?? options.FirstOrDefault()
        };
    }

    private static LanguageOption? SelectOption(IEnumerable<LanguageOption> options, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return options.FirstOrDefault(option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}
