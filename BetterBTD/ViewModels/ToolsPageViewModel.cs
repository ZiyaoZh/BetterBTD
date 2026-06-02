using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Models;
using BetterBTD.Models.Tools;
using BetterBTD.Services.Tools;

namespace BetterBTD.ViewModels;

public sealed class ToolsPageViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private readonly ToolsOptionService _toolsOptionService;
    private readonly RoundToolService _roundToolService;
    private readonly HeroToolService _heroToolService;
    private readonly ParagonToolService _paragonToolService;

    private int _startRound = 1;
    private int _endRound = 100;
    private int _heroPlacementRound = 1;
    private string _heroTargetRound = string.Empty;
    private string _heroTargetLevel = string.Empty;
    private double _paragonTotalPops;
    private int _paragonUpgradeCount;
    private double _paragonExtraCash;
    private LanguageOption? _selectedHero;
    private LanguageOption? _selectedParagonMonkey;
    private string _roundResultText = string.Empty;
    private string _heroResultText = string.Empty;
    private string _paragonResultText = string.Empty;
    private readonly int _maxRound;

    public ToolsPageViewModel()
        : this(
            LocalizationService.Instance,
            ToolsOptionService.Instance,
            RoundToolService.Instance,
            HeroToolService.Instance,
            ParagonToolService.Instance)
    {
    }

    internal ToolsPageViewModel(
        LocalizationService localizationService,
        ToolsOptionService toolsOptionService,
        RoundToolService roundToolService,
        HeroToolService heroToolService,
        ParagonToolService paragonToolService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _toolsOptionService = toolsOptionService ?? throw new ArgumentNullException(nameof(toolsOptionService));
        _roundToolService = roundToolService ?? throw new ArgumentNullException(nameof(roundToolService));
        _heroToolService = heroToolService ?? throw new ArgumentNullException(nameof(heroToolService));
        _paragonToolService = paragonToolService ?? throw new ArgumentNullException(nameof(paragonToolService));

        HeroOptions = [];
        ParagonMonkeyOptions = [];

        CalculateRoundCommand = new RelayCommand(UpdateRoundResult);
        CalculateHeroCommand = new RelayCommand(UpdateHeroResult);
        CalculateParagonCommand = new RelayCommand(UpdateParagonResult);

        _localizationService.LanguageChanged += (_, _) => RefreshLocalizedContent();
        _maxRound = _roundToolService.TryGetMaxRound();
        _startRound = _roundToolService.NormalizeRound(_startRound, _maxRound);
        _endRound = _roundToolService.NormalizeRound(_endRound, _maxRound);
        RefreshLocalizedContent();
    }

    public ObservableCollection<LanguageOption> HeroOptions { get; }

    public ObservableCollection<LanguageOption> ParagonMonkeyOptions { get; }

    public IRelayCommand CalculateRoundCommand { get; }

    public IRelayCommand CalculateHeroCommand { get; }

    public IRelayCommand CalculateParagonCommand { get; }

    public string PageTitle => _localizationService.T("Tools.PageTitle");

    public string PageDescription => _localizationService.T("Tools.PageDescription");

    public string ParametersSectionTitle => _localizationService.T("Tools.Section.Parameters");

    public string ParametersSectionDescription => _localizationService.T("Tools.Section.ParametersDescription");

    public string ResultSectionTitle => _localizationService.T("Tools.Section.Result");

    public string ResultSectionDescription => _localizationService.T("Tools.Section.ResultDescription");

    public string CalculateButtonText => _localizationService.T("Tools.Action.Calculate");

    public string RoundCardTitle => _localizationService.T("Tools.Round.Title");

    public string RoundCardDescription => _localizationService.T("Tools.Round.Description");

    public string StartRoundLabel => _localizationService.T("Tools.Round.StartRound");

    public string StartRoundDescription => _localizationService.T("Tools.Round.StartRoundDescription");

    public string EndRoundLabel => _localizationService.T("Tools.Round.EndRound");

    public string EndRoundDescription => _localizationService.T("Tools.Round.EndRoundDescription");

    public string HeroCardTitle => _localizationService.T("Tools.Hero.Title");

    public string HeroCardDescription => _localizationService.T("Tools.Hero.Description");

    public string HeroLabel => _localizationService.T("Tools.Hero.Hero");

    public string HeroDescription => _localizationService.T("Tools.Hero.HeroDescription");

    public string HeroPlacementRoundLabel => _localizationService.T("Tools.Hero.PlacementRound");

    public string HeroPlacementRoundDescription => _localizationService.T("Tools.Hero.PlacementRoundDescription");

    public string HeroTargetRoundLabel => _localizationService.T("Tools.Hero.TargetRound");

    public string HeroTargetRoundDescription => _localizationService.T("Tools.Hero.TargetRoundDescription");

    public string HeroTargetLevelLabel => _localizationService.T("Tools.Hero.TargetLevel");

    public string HeroTargetLevelDescription => _localizationService.T("Tools.Hero.TargetLevelDescription");

    public string HeroHintText => _localizationService.T("Tools.Hero.Hint");

    public string ParagonCardTitle => _localizationService.T("Tools.Paragon.Title");

    public string ParagonCardDescription => _localizationService.T("Tools.Paragon.Description");

    public string ParagonMonkeyLabel => _localizationService.T("Tools.Paragon.Monkey");

    public string ParagonMonkeyDescription => _localizationService.T("Tools.Paragon.MonkeyDescription");

    public string ParagonTotalPopsLabel => _localizationService.T("Tools.Paragon.TotalPops");

    public string ParagonTotalPopsDescription => _localizationService.T("Tools.Paragon.TotalPopsDescription");

    public string ParagonUpgradeCountLabel => _localizationService.T("Tools.Paragon.UpgradeCount");

    public string ParagonUpgradeCountDescription => _localizationService.T("Tools.Paragon.UpgradeCountDescription");

    public string ParagonExtraCashLabel => _localizationService.T("Tools.Paragon.ExtraCash");

    public string ParagonExtraCashDescription => _localizationService.T("Tools.Paragon.ExtraCashDescription");

    public int MaxRound => _maxRound;

    public int StartRound
    {
        get => _startRound;
        set
        {
            var normalized = _roundToolService.NormalizeRound(value, MaxRound);
            if (SetProperty(ref _startRound, normalized))
            {
                UpdateRoundResult();
            }
        }
    }

    public int EndRound
    {
        get => _endRound;
        set
        {
            var normalized = _roundToolService.NormalizeRound(value, MaxRound);
            if (SetProperty(ref _endRound, normalized))
            {
                UpdateRoundResult();
            }
        }
    }

    public LanguageOption? SelectedHero
    {
        get => _selectedHero;
        set
        {
            if (SetProperty(ref _selectedHero, value))
            {
                UpdateHeroResult();
            }
        }
    }

    public int HeroPlacementRound
    {
        get => _heroPlacementRound;
        set
        {
            if (SetProperty(ref _heroPlacementRound, value))
            {
                UpdateHeroResult();
            }
        }
    }

    public string HeroTargetRound
    {
        get => _heroTargetRound;
        set
        {
            if (SetProperty(ref _heroTargetRound, value))
            {
                UpdateHeroResult();
            }
        }
    }

    public string HeroTargetLevel
    {
        get => _heroTargetLevel;
        set
        {
            if (SetProperty(ref _heroTargetLevel, value))
            {
                UpdateHeroResult();
            }
        }
    }

    public LanguageOption? SelectedParagonMonkey
    {
        get => _selectedParagonMonkey;
        set
        {
            if (SetProperty(ref _selectedParagonMonkey, value))
            {
                UpdateParagonResult();
            }
        }
    }

    public double ParagonTotalPops
    {
        get => _paragonTotalPops;
        set
        {
            if (SetProperty(ref _paragonTotalPops, value))
            {
                UpdateParagonResult();
            }
        }
    }

    public int ParagonUpgradeCount
    {
        get => _paragonUpgradeCount;
        set
        {
            if (SetProperty(ref _paragonUpgradeCount, value))
            {
                UpdateParagonResult();
            }
        }
    }

    public double ParagonExtraCash
    {
        get => _paragonExtraCash;
        set
        {
            if (SetProperty(ref _paragonExtraCash, value))
            {
                UpdateParagonResult();
            }
        }
    }

    public string RoundResultText
    {
        get => _roundResultText;
        private set => SetProperty(ref _roundResultText, value);
    }

    public string HeroResultText
    {
        get => _heroResultText;
        private set => SetProperty(ref _heroResultText, value);
    }

    public string ParagonResultText
    {
        get => _paragonResultText;
        private set => SetProperty(ref _paragonResultText, value);
    }

    private void RefreshLocalizedContent()
    {
        RefreshHeroOptions(SelectedHero?.Code);
        RefreshParagonMonkeyOptions(SelectedParagonMonkey?.Code);

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(ParametersSectionTitle));
        OnPropertyChanged(nameof(ParametersSectionDescription));
        OnPropertyChanged(nameof(ResultSectionTitle));
        OnPropertyChanged(nameof(ResultSectionDescription));
        OnPropertyChanged(nameof(CalculateButtonText));
        OnPropertyChanged(nameof(RoundCardTitle));
        OnPropertyChanged(nameof(RoundCardDescription));
        OnPropertyChanged(nameof(StartRoundLabel));
        OnPropertyChanged(nameof(StartRoundDescription));
        OnPropertyChanged(nameof(EndRoundLabel));
        OnPropertyChanged(nameof(EndRoundDescription));
        OnPropertyChanged(nameof(HeroCardTitle));
        OnPropertyChanged(nameof(HeroCardDescription));
        OnPropertyChanged(nameof(HeroLabel));
        OnPropertyChanged(nameof(HeroDescription));
        OnPropertyChanged(nameof(HeroPlacementRoundLabel));
        OnPropertyChanged(nameof(HeroPlacementRoundDescription));
        OnPropertyChanged(nameof(HeroTargetRoundLabel));
        OnPropertyChanged(nameof(HeroTargetRoundDescription));
        OnPropertyChanged(nameof(HeroTargetLevelLabel));
        OnPropertyChanged(nameof(HeroTargetLevelDescription));
        OnPropertyChanged(nameof(HeroHintText));
        OnPropertyChanged(nameof(ParagonCardTitle));
        OnPropertyChanged(nameof(ParagonCardDescription));
        OnPropertyChanged(nameof(ParagonMonkeyLabel));
        OnPropertyChanged(nameof(ParagonMonkeyDescription));
        OnPropertyChanged(nameof(ParagonTotalPopsLabel));
        OnPropertyChanged(nameof(ParagonTotalPopsDescription));
        OnPropertyChanged(nameof(ParagonUpgradeCountLabel));
        OnPropertyChanged(nameof(ParagonUpgradeCountDescription));
        OnPropertyChanged(nameof(ParagonExtraCashLabel));
        OnPropertyChanged(nameof(ParagonExtraCashDescription));

        UpdateRoundResult();
        UpdateHeroResult();
        UpdateParagonResult();
    }

    private void RefreshHeroOptions(string? selectedCode)
    {
        ApplyOptions(HeroOptions, _toolsOptionService.BuildHeroOptions(selectedCode), option => SelectedHero = option);
    }

    private void RefreshParagonMonkeyOptions(string? selectedCode)
    {
        ApplyOptions(
            ParagonMonkeyOptions,
            _toolsOptionService.BuildParagonMonkeyOptions(selectedCode),
            option => SelectedParagonMonkey = option);
    }

    private void UpdateRoundResult()
    {
        RoundResultText = _roundToolService.BuildResult(
            new RoundToolRequest
            {
                StartRound = StartRound,
                EndRound = EndRound
            },
            MaxRound);
    }

    private void UpdateHeroResult()
    {
        HeroResultText = _heroToolService.BuildResult(new HeroToolRequest
        {
            HeroDisplayName = SelectedHero?.DisplayName,
            PlacementRound = HeroPlacementRound,
            TargetRound = HeroTargetRound,
            TargetLevel = HeroTargetLevel
        });
    }

    private void UpdateParagonResult()
    {
        ParagonResultText = _paragonToolService.BuildResult(new ParagonToolRequest
        {
            MonkeyDisplayName = SelectedParagonMonkey?.DisplayName,
            TotalPops = ParagonTotalPops,
            UpgradeCount = ParagonUpgradeCount,
            ExtraCash = ParagonExtraCash
        });
    }

    private static void ApplyOptions(
        ObservableCollection<LanguageOption> targetCollection,
        ToolOptionRefreshResult refreshResult,
        Action<LanguageOption?> applySelectedOption)
    {
        ArgumentNullException.ThrowIfNull(targetCollection);
        ArgumentNullException.ThrowIfNull(refreshResult);
        ArgumentNullException.ThrowIfNull(applySelectedOption);

        targetCollection.Clear();
        foreach (var option in refreshResult.Options)
        {
            targetCollection.Add(option);
        }

        applySelectedOption(refreshResult.SelectedOption);
    }
}
