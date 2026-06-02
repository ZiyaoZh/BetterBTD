using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ParagonStatsToolService _paragonStatsToolService;

    private int _startRound = 1;
    private int _endRound = 100;
    private int _heroPlacementRound = 1;
    private string _heroTargetRound = string.Empty;
    private string _heroTargetLevel = string.Empty;
    private double _paragonTotalPops;
    private double _paragonGeneratedCash;
    private double _paragonCashSpent;
    private double _paragonSliderCashInvestment;
    private int _paragonTierFiveCount = 3;
    private int _paragonUpgradeCount;
    private int _paragonTotemCount;
    private double _paragonSliderMaximum;
    private int _paragonStatsDegree = 1;
    private double _paragonStatsAttackIntervalSeconds = 0.5d;
    private double _paragonStatsPierce = 200d;
    private double _paragonStatsBaseDamage = 15d;
    private double _paragonStatsMoabDamageBonus;
    private double _paragonStatsBossDamageBonus;
    private double _paragonStatsOtherDamageBonus1;
    private double _paragonStatsOtherDamageBonus2;
    private double _paragonStatsOtherDamageBonus3;
    private LanguageOption? _selectedHero;
    private LanguageOption? _selectedParagonMonkey;
    private LanguageOption? _selectedParagonDifficulty;
    private string _roundResultText = string.Empty;
    private string _heroResultText = string.Empty;
    private string _paragonResultText = string.Empty;
    private string _paragonStatsResultText = string.Empty;
    private readonly int _maxRound;

    public ToolsPageViewModel()
        : this(
            LocalizationService.Instance,
            ToolsOptionService.Instance,
            RoundToolService.Instance,
            HeroToolService.Instance,
            ParagonToolService.Instance,
            ParagonStatsToolService.Instance)
    {
    }

    internal ToolsPageViewModel(
        LocalizationService localizationService,
        ToolsOptionService toolsOptionService,
        RoundToolService roundToolService,
        HeroToolService heroToolService,
        ParagonToolService paragonToolService,
        ParagonStatsToolService paragonStatsToolService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _toolsOptionService = toolsOptionService ?? throw new ArgumentNullException(nameof(toolsOptionService));
        _roundToolService = roundToolService ?? throw new ArgumentNullException(nameof(roundToolService));
        _heroToolService = heroToolService ?? throw new ArgumentNullException(nameof(heroToolService));
        _paragonToolService = paragonToolService ?? throw new ArgumentNullException(nameof(paragonToolService));
        _paragonStatsToolService = paragonStatsToolService ?? throw new ArgumentNullException(nameof(paragonStatsToolService));

        HeroOptions = [];
        ParagonMonkeyOptions = [];
        ParagonDifficultyOptions = [];

        CalculateRoundCommand = new RelayCommand(UpdateRoundResult);
        CalculateHeroCommand = new RelayCommand(UpdateHeroResult);
        CalculateParagonCommand = new RelayCommand(UpdateParagonResult);
        CalculateParagonStatsCommand = new RelayCommand(UpdateParagonStatsResult);

        _localizationService.LanguageChanged += (_, _) => RefreshLocalizedContent();
        _maxRound = _roundToolService.TryGetMaxRound();
        _startRound = _roundToolService.NormalizeRound(_startRound, _maxRound);
        _endRound = _roundToolService.NormalizeRound(_endRound, _maxRound);
        RefreshLocalizedContent();
    }

    public ObservableCollection<LanguageOption> HeroOptions { get; }

    public ObservableCollection<LanguageOption> ParagonMonkeyOptions { get; }

    public ObservableCollection<LanguageOption> ParagonDifficultyOptions { get; }

    public IRelayCommand CalculateRoundCommand { get; }

    public IRelayCommand CalculateHeroCommand { get; }

    public IRelayCommand CalculateParagonCommand { get; }

    public IRelayCommand CalculateParagonStatsCommand { get; }

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

    public string ParagonDifficultyLabel => _localizationService.T("Tools.Paragon.Difficulty");

    public string ParagonDifficultyDescription => _localizationService.T("Tools.Paragon.DifficultyDescription");

    public string ParagonTotalPopsLabel => _localizationService.T("Tools.Paragon.TotalPops");

    public string ParagonTotalPopsDescription => _localizationService.T("Tools.Paragon.TotalPopsDescription");

    public string ParagonGeneratedCashLabel => _localizationService.T("Tools.Paragon.GeneratedCash");

    public string ParagonGeneratedCashDescription => _localizationService.T("Tools.Paragon.GeneratedCashDescription");

    public string ParagonCashSpentLabel => _localizationService.T("Tools.Paragon.CashSpent");

    public string ParagonCashSpentDescription => _localizationService.T("Tools.Paragon.CashSpentDescription");

    public string ParagonSliderCashInvestmentLabel => _localizationService.T("Tools.Paragon.SliderCashInvestment");

    public string ParagonSliderCashInvestmentDescription => _localizationService.T("Tools.Paragon.SliderCashInvestmentDescription");

    public string ParagonTierFiveCountLabel => _localizationService.T("Tools.Paragon.TierFiveCount");

    public string ParagonTierFiveCountDescription => _localizationService.T("Tools.Paragon.TierFiveCountDescription");

    public string ParagonUpgradeCountLabel => _localizationService.T("Tools.Paragon.UpgradeCount");

    public string ParagonUpgradeCountDescription => _localizationService.T("Tools.Paragon.UpgradeCountDescription");

    public string ParagonTotemCountLabel => _localizationService.T("Tools.Paragon.TotemCount");

    public string ParagonTotemCountDescription => _localizationService.T("Tools.Paragon.TotemCountDescription");

    public string ParagonCostHintText => string.Format(
        _localizationService.T("Tools.Paragon.CostHint"),
        FormatWholeNumber(_paragonToolService.GetActualCost(SelectedParagonMonkey?.Code, SelectedParagonDifficulty?.Code)),
        FormatWholeNumber(ParagonSliderMaximum));

    public string ParagonStatsCardTitle => _localizationService.T("Tools.ParagonStats.Title");

    public string ParagonStatsCardDescription => _localizationService.T("Tools.ParagonStats.Description");

    public string ParagonStatsDegreeLabel => _localizationService.T("Tools.ParagonStats.Degree");

    public string ParagonStatsDegreeDescription => _localizationService.T("Tools.ParagonStats.DegreeDescription");

    public string ParagonStatsAttackIntervalLabel => _localizationService.T("Tools.ParagonStats.AttackInterval");

    public string ParagonStatsAttackIntervalDescription => _localizationService.T("Tools.ParagonStats.AttackIntervalDescription");

    public string ParagonStatsPierceLabel => _localizationService.T("Tools.ParagonStats.Pierce");

    public string ParagonStatsPierceDescription => _localizationService.T("Tools.ParagonStats.PierceDescription");

    public string ParagonStatsBaseDamageLabel => _localizationService.T("Tools.ParagonStats.BaseDamage");

    public string ParagonStatsBaseDamageDescription => _localizationService.T("Tools.ParagonStats.BaseDamageDescription");

    public string ParagonStatsMoabDamageLabel => _localizationService.T("Tools.ParagonStats.MoabDamage");

    public string ParagonStatsMoabDamageDescription => _localizationService.T("Tools.ParagonStats.MoabDamageDescription");

    public string ParagonStatsBossDamageLabel => _localizationService.T("Tools.ParagonStats.BossDamage");

    public string ParagonStatsBossDamageDescription => _localizationService.T("Tools.ParagonStats.BossDamageDescription");

    public string ParagonStatsOtherDamage1Label => _localizationService.T("Tools.ParagonStats.OtherDamage1");

    public string ParagonStatsOtherDamage1Description => _localizationService.T("Tools.ParagonStats.OtherDamage1Description");

    public string ParagonStatsOtherDamage2Label => _localizationService.T("Tools.ParagonStats.OtherDamage2");

    public string ParagonStatsOtherDamage2Description => _localizationService.T("Tools.ParagonStats.OtherDamage2Description");

    public string ParagonStatsOtherDamage3Label => _localizationService.T("Tools.ParagonStats.OtherDamage3");

    public string ParagonStatsOtherDamage3Description => _localizationService.T("Tools.ParagonStats.OtherDamage3Description");

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
                RefreshParagonDerivedState();
                UpdateParagonResult();
            }
        }
    }

    public LanguageOption? SelectedParagonDifficulty
    {
        get => _selectedParagonDifficulty;
        set
        {
            if (SetProperty(ref _selectedParagonDifficulty, value))
            {
                RefreshParagonDerivedState();
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

    public double ParagonGeneratedCash
    {
        get => _paragonGeneratedCash;
        set
        {
            if (SetProperty(ref _paragonGeneratedCash, value))
            {
                UpdateParagonResult();
            }
        }
    }

    public double ParagonCashSpent
    {
        get => _paragonCashSpent;
        set
        {
            if (SetProperty(ref _paragonCashSpent, value))
            {
                UpdateParagonResult();
            }
        }
    }

    public double ParagonSliderCashInvestment
    {
        get => _paragonSliderCashInvestment;
        set
        {
            var normalized = Math.Clamp(value, 0d, ParagonSliderMaximum);
            if (SetProperty(ref _paragonSliderCashInvestment, normalized))
            {
                UpdateParagonResult();
            }
        }
    }

    public int ParagonTierFiveCount
    {
        get => _paragonTierFiveCount;
        set
        {
            var normalized = Math.Clamp(value, 3, 12);
            if (SetProperty(ref _paragonTierFiveCount, normalized))
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
            var normalized = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _paragonUpgradeCount, normalized))
            {
                UpdateParagonResult();
            }
        }
    }

    public int ParagonTotemCount
    {
        get => _paragonTotemCount;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _paragonTotemCount, normalized))
            {
                UpdateParagonResult();
            }
        }
    }

    public double ParagonSliderMaximum
    {
        get => _paragonSliderMaximum;
        private set => SetProperty(ref _paragonSliderMaximum, value);
    }

    public int ParagonStatsDegree
    {
        get => _paragonStatsDegree;
        set
        {
            var normalized = Math.Clamp(value, 1, 100);
            if (SetProperty(ref _paragonStatsDegree, normalized))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsAttackIntervalSeconds
    {
        get => _paragonStatsAttackIntervalSeconds;
        set
        {
            if (SetProperty(ref _paragonStatsAttackIntervalSeconds, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsPierce
    {
        get => _paragonStatsPierce;
        set
        {
            if (SetProperty(ref _paragonStatsPierce, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsBaseDamage
    {
        get => _paragonStatsBaseDamage;
        set
        {
            if (SetProperty(ref _paragonStatsBaseDamage, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsMoabDamageBonus
    {
        get => _paragonStatsMoabDamageBonus;
        set
        {
            if (SetProperty(ref _paragonStatsMoabDamageBonus, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsBossDamageBonus
    {
        get => _paragonStatsBossDamageBonus;
        set
        {
            if (SetProperty(ref _paragonStatsBossDamageBonus, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsOtherDamageBonus1
    {
        get => _paragonStatsOtherDamageBonus1;
        set
        {
            if (SetProperty(ref _paragonStatsOtherDamageBonus1, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsOtherDamageBonus2
    {
        get => _paragonStatsOtherDamageBonus2;
        set
        {
            if (SetProperty(ref _paragonStatsOtherDamageBonus2, value))
            {
                UpdateParagonStatsResult();
            }
        }
    }

    public double ParagonStatsOtherDamageBonus3
    {
        get => _paragonStatsOtherDamageBonus3;
        set
        {
            if (SetProperty(ref _paragonStatsOtherDamageBonus3, value))
            {
                UpdateParagonStatsResult();
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

    public string ParagonStatsResultText
    {
        get => _paragonStatsResultText;
        private set => SetProperty(ref _paragonStatsResultText, value);
    }

    private void RefreshLocalizedContent()
    {
        RefreshHeroOptions(SelectedHero?.Code);
        RefreshParagonMonkeyOptions(SelectedParagonMonkey?.Code);
        RefreshParagonDifficultyOptions(SelectedParagonDifficulty?.Code);

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
        OnPropertyChanged(nameof(ParagonDifficultyLabel));
        OnPropertyChanged(nameof(ParagonDifficultyDescription));
        OnPropertyChanged(nameof(ParagonTotalPopsLabel));
        OnPropertyChanged(nameof(ParagonTotalPopsDescription));
        OnPropertyChanged(nameof(ParagonGeneratedCashLabel));
        OnPropertyChanged(nameof(ParagonGeneratedCashDescription));
        OnPropertyChanged(nameof(ParagonCashSpentLabel));
        OnPropertyChanged(nameof(ParagonCashSpentDescription));
        OnPropertyChanged(nameof(ParagonSliderCashInvestmentLabel));
        OnPropertyChanged(nameof(ParagonSliderCashInvestmentDescription));
        OnPropertyChanged(nameof(ParagonTierFiveCountLabel));
        OnPropertyChanged(nameof(ParagonTierFiveCountDescription));
        OnPropertyChanged(nameof(ParagonUpgradeCountLabel));
        OnPropertyChanged(nameof(ParagonUpgradeCountDescription));
        OnPropertyChanged(nameof(ParagonTotemCountLabel));
        OnPropertyChanged(nameof(ParagonTotemCountDescription));
        OnPropertyChanged(nameof(ParagonStatsCardTitle));
        OnPropertyChanged(nameof(ParagonStatsCardDescription));
        OnPropertyChanged(nameof(ParagonStatsDegreeLabel));
        OnPropertyChanged(nameof(ParagonStatsDegreeDescription));
        OnPropertyChanged(nameof(ParagonStatsAttackIntervalLabel));
        OnPropertyChanged(nameof(ParagonStatsAttackIntervalDescription));
        OnPropertyChanged(nameof(ParagonStatsPierceLabel));
        OnPropertyChanged(nameof(ParagonStatsPierceDescription));
        OnPropertyChanged(nameof(ParagonStatsBaseDamageLabel));
        OnPropertyChanged(nameof(ParagonStatsBaseDamageDescription));
        OnPropertyChanged(nameof(ParagonStatsMoabDamageLabel));
        OnPropertyChanged(nameof(ParagonStatsMoabDamageDescription));
        OnPropertyChanged(nameof(ParagonStatsBossDamageLabel));
        OnPropertyChanged(nameof(ParagonStatsBossDamageDescription));
        OnPropertyChanged(nameof(ParagonStatsOtherDamage1Label));
        OnPropertyChanged(nameof(ParagonStatsOtherDamage1Description));
        OnPropertyChanged(nameof(ParagonStatsOtherDamage2Label));
        OnPropertyChanged(nameof(ParagonStatsOtherDamage2Description));
        OnPropertyChanged(nameof(ParagonStatsOtherDamage3Label));
        OnPropertyChanged(nameof(ParagonStatsOtherDamage3Description));

        RefreshParagonDerivedState();
        UpdateRoundResult();
        UpdateHeroResult();
        UpdateParagonResult();
        UpdateParagonStatsResult();
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

    private void RefreshParagonDifficultyOptions(string? selectedCode)
    {
        ApplyOptions(
            ParagonDifficultyOptions,
            _toolsOptionService.BuildParagonDifficultyOptions(selectedCode),
            option => SelectedParagonDifficulty = option);
    }

    private void RefreshParagonDerivedState()
    {
        var sliderMaximum = _paragonToolService.GetSliderMaximum(SelectedParagonMonkey?.Code, SelectedParagonDifficulty?.Code);
        ParagonSliderMaximum = sliderMaximum;

        if (_paragonSliderCashInvestment > sliderMaximum)
        {
            _paragonSliderCashInvestment = sliderMaximum;
            OnPropertyChanged(nameof(ParagonSliderCashInvestment));
        }

        OnPropertyChanged(nameof(ParagonCostHintText));
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
            MonkeyCode = SelectedParagonMonkey?.Code,
            DifficultyCode = SelectedParagonDifficulty?.Code ?? "Medium",
            TotalPops = ParagonTotalPops,
            GeneratedCash = ParagonGeneratedCash,
            CashSpent = ParagonCashSpent,
            SliderCashInvestment = ParagonSliderCashInvestment,
            TierFiveCount = ParagonTierFiveCount,
            UpgradeCount = ParagonUpgradeCount,
            TotemCount = ParagonTotemCount
        });
    }

    private void UpdateParagonStatsResult()
    {
        ParagonStatsResultText = _paragonStatsToolService.BuildResult(new ParagonStatsToolRequest
        {
            Degree = ParagonStatsDegree,
            AttackIntervalSeconds = ParagonStatsAttackIntervalSeconds,
            Pierce = ParagonStatsPierce,
            BaseDamage = ParagonStatsBaseDamage,
            MoabDamageBonus = ParagonStatsMoabDamageBonus,
            BossDamageBonus = ParagonStatsBossDamageBonus,
            OtherDamageBonus1 = ParagonStatsOtherDamageBonus1,
            OtherDamageBonus2 = ParagonStatsOtherDamageBonus2,
            OtherDamageBonus3 = ParagonStatsOtherDamageBonus3
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

    private static string FormatWholeNumber(double value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
