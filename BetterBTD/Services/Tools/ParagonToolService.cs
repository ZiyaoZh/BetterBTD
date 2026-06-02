using BetterBTD.Models.Tools;

namespace BetterBTD.Services.Tools;

public sealed class ParagonToolService
{
    private static readonly Lazy<ParagonToolService> InstanceHolder = new(() => new ParagonToolService(LocalizationService.Instance));

    private readonly LocalizationService _localizationService;

    internal ParagonToolService(LocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public static ParagonToolService Instance => InstanceHolder.Value;

    public string BuildResult(ParagonToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var monkeyName = request.MonkeyDisplayName ?? _localizationService.T("Tools.Paragon.Monkey");
        return string.Format(
            _localizationService.T("Tools.Paragon.ResultPlaceholder"),
            monkeyName,
            FormatWholeNumber(request.TotalPops),
            request.UpgradeCount,
            FormatWholeNumber(request.ExtraCash));
    }

    private static string FormatWholeNumber(double value)
    {
        return Math.Round(value).ToString("0");
    }
}
