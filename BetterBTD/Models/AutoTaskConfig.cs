using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBTD.Models;

public partial class AutoTaskConfig : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _tutorialUrl = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _runningButtonText = string.Empty;

    [ObservableProperty]
    private int _operationIntervalMs = 200;

    [ObservableProperty]
    private bool _showStageTargetConfiguration = true;

    [ObservableProperty]
    private bool _showCollectionVariantConfiguration;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _mapOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedMapOption;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _difficultyOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedDifficultyOption;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _modeOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedModeOption;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _variantOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedVariantOption;
}
