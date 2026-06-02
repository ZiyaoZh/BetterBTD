using BetterBTD.Models.Tools;

namespace BetterBTD.Services.Tools;

public sealed class HeroToolService
{
    private static readonly Lazy<HeroToolService> InstanceHolder = new(() => new HeroToolService(LocalizationService.Instance));

    private readonly LocalizationService _localizationService;

    internal HeroToolService(LocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public static HeroToolService Instance => InstanceHolder.Value;

    public string BuildResult(HeroToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var heroName = request.HeroDisplayName ?? _localizationService.T("Tools.Hero.Hero");
        var hasTargetRound = !string.IsNullOrWhiteSpace(request.TargetRound);
        var hasTargetLevel = !string.IsNullOrWhiteSpace(request.TargetLevel);

        if (hasTargetRound && hasTargetLevel)
        {
            return _localizationService.T("Tools.Hero.Result.BothTargets");
        }

        if (hasTargetRound)
        {
            return string.Format(
                _localizationService.T("Tools.Hero.Result.TargetRound"),
                heroName,
                request.PlacementRound,
                request.TargetRound.Trim());
        }

        if (hasTargetLevel)
        {
            return string.Format(
                _localizationService.T("Tools.Hero.Result.TargetLevel"),
                heroName,
                request.PlacementRound,
                request.TargetLevel.Trim());
        }

        return _localizationService.T("Tools.Hero.Result.NoTarget");
    }
}
