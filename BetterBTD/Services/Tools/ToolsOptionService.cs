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
        var options = ParagonToolCatalog.EligibleMonkeys
            .Select(monkey => new LanguageOption
            {
                Code = monkey.TowerType.ToString(),
                DisplayName = GameElementCatalog.GetMonkeyTowerDisplayName(monkey.TowerType)
            })
            .ToArray();

        return new ToolOptionRefreshResult
        {
            Options = options,
            SelectedOption = SelectOption(options, selectedCode) ?? options.FirstOrDefault()
        };
    }

    public ToolOptionRefreshResult BuildParagonDifficultyOptions(string? selectedCode)
    {
        var options = new[]
        {
            new LanguageOption
            {
                Code = "Easy",
                DisplayName = LocalizationService.Instance.T("Tools.Paragon.Difficulty.Easy")
            },
            new LanguageOption
            {
                Code = "Medium",
                DisplayName = LocalizationService.Instance.T("Tools.Paragon.Difficulty.Medium")
            },
            new LanguageOption
            {
                Code = "Hard",
                DisplayName = LocalizationService.Instance.T("Tools.Paragon.Difficulty.Hard")
            },
            new LanguageOption
            {
                Code = "Impoppable",
                DisplayName = LocalizationService.Instance.T("Tools.Paragon.Difficulty.Impoppable")
            }
        };

        return new ToolOptionRefreshResult
        {
            Options = options,
            SelectedOption = SelectOption(options, selectedCode) ?? options.ElementAtOrDefault(1) ?? options.FirstOrDefault()
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
