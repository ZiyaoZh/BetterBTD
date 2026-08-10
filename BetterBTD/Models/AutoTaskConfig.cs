using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BetterBTD.Models.AutoTasks;

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
    private bool _showBlackBorderVariantConfiguration;

    [ObservableProperty]
    private bool _showScriptConfiguration;

    [ObservableProperty]
    private bool _showScriptIdConfiguration;

    [ObservableProperty]
    private bool _showLoopStageModeConfiguration;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _loopStageModeOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedLoopStageModeOption;

    [ObservableProperty]
    private int _exitAfterRound = 102;

    public bool ShowExitAfterRoundConfiguration =>
        ShowLoopStageModeConfiguration &&
        string.Equals(
            SelectedLoopStageModeOption?.Code,
            LoopStageRunMode.FreeplayUntilRound.ToString(),
            StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedLoopStageModeOptionChanged(LanguageOption? value)
    {
        OnPropertyChanged(nameof(ShowExitAfterRoundConfiguration));
    }

    [ObservableProperty]
    private bool _showOdysseyScriptIdConfiguration;

    [ObservableProperty]
    private bool _showRobotControlConfiguration;

    [ObservableProperty]
    private string _robotControlListenUrl = "http://127.0.0.1:18766/";

    [ObservableProperty]
    private bool _showCollectionSubscriptionActions;

    [ObservableProperty]
    private string _subscriptionLabel = string.Empty;

    [ObservableProperty]
    private string _subscriptionDescription = string.Empty;

    [ObservableProperty]
    private string _scriptId = string.Empty;

    [ObservableProperty]
    private string _scriptId2 = string.Empty;

    [ObservableProperty]
    private string _scriptId3 = string.Empty;

    [ObservableProperty]
    private string _scriptId4 = string.Empty;

    [ObservableProperty]
    private string _scriptId5 = string.Empty;

    [ObservableProperty]
    private bool _showBlackBorderSubscriptionActions;

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

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _subscriptionExportTypeOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedSubscriptionExportTypeOption;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _subscriptionMapOptions = [];

    [ObservableProperty]
    private LanguageOption? _selectedSubscriptionMapOption;

    [ObservableProperty]
    private bool _showSubscriptionMapSelection;
}
