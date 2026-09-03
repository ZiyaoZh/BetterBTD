using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services;
using BetterBTD.Services.ChildSession;
using BetterBTD.Services.Updates;
using BetterBTD.Views.Windows;

namespace BetterBTD.ViewModels;

public sealed class SettingsPageViewModel : ObservableObject
{
    private readonly ConfigurationService _configurationService;
    private readonly LocalizationService _localizationService;
    private readonly ThemeService _themeService;
    private readonly ApplicationUpdateService _applicationUpdateService;
    private bool _isRefreshingSelections;

    private LanguageOption? _selectedUiLanguage;
    private LanguageOption? _selectedGameLanguage;
    private ThemeOption? _selectedTheme;
    private KeyboardMouseSimulationMode _selectedKeyboardMouseSimulationMode;
    private string _startHotkey = string.Empty;
    private string _stopHotkey = string.Empty;
    private string _gameStartHotkey = string.Empty;
    private string _gameStopHotkey = string.Empty;
    private string _updateStatusText = string.Empty;
    private int _autoTaskMaxConsecutiveNavigationFailures;
    private int _autoTaskStuckUiTimeoutSeconds;
    private int _autoTaskVisualFingerprintDistanceTolerance;
    private int _autoTaskStuckRecoveryDelayMs;

    public SettingsPageViewModel()
    {
        _configurationService = ConfigurationService.Instance;
        _localizationService = LocalizationService.Instance;
        _themeService = ThemeService.Instance;
        _applicationUpdateService = ApplicationUpdateService.Instance;

        UiLanguageOptions = [];
        GameLanguageOptions = [];
        ThemeOptions = [];
        AutoTaskStuckRecoveryPoints = [];

        OpenKeyBindingsWindowCommand = new RelayCommand(OpenKeyBindingsWindow);
        CheckUpdateCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        OpenAboutCommand = new RelayCommand(OpenAbout);
        AddAutoTaskRecoveryPointCommand = new RelayCommand(AddAutoTaskRecoveryPoint, CanAddAutoTaskRecoveryPoint);
        RemoveAutoTaskRecoveryPointCommand = new RelayCommand<AutoTaskRecoveryPointViewModel?>(RemoveAutoTaskRecoveryPoint);

        LoadFromConfiguration();
        RefreshOptionsAndSelections();

        _localizationService.LanguageChanged += (_, _) =>
        {
            RefreshOptionsAndSelections();
            RaiseLocalizedProperties();
        };
    }

    public ObservableCollection<LanguageOption> UiLanguageOptions { get; }

    public ObservableCollection<LanguageOption> GameLanguageOptions { get; }

    public ObservableCollection<ThemeOption> ThemeOptions { get; }

    public ObservableCollection<AutoTaskRecoveryPointViewModel> AutoTaskStuckRecoveryPoints { get; }

    public bool CanEditSharedSettings => ChildSessionRuntimeState.CanPersistSharedData;

    public LanguageOption? SelectedUiLanguage
    {
        get => _selectedUiLanguage;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (!SetProperty(ref _selectedUiLanguage, value) || value is null)
            {
                return;
            }

            UpdateUiLanguage();
            SaveCurrentConfiguration();
        }
    }

    public LanguageOption? SelectedGameLanguage
    {
        get => _selectedGameLanguage;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (!SetProperty(ref _selectedGameLanguage, value) || value is null)
            {
                return;
            }

            SaveCurrentConfiguration();
        }
    }

    public ThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (!SetProperty(ref _selectedTheme, value) || value is null)
            {
                return;
            }

            _themeService.ApplyTheme(value.Code);
            SaveCurrentConfiguration();
        }
    }

    public string StartHotkey
    {
        get => _startHotkey;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _startHotkey, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public string StopHotkey
    {
        get => _stopHotkey;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _stopHotkey, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public string GameStartHotkey
    {
        get => _gameStartHotkey;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _gameStartHotkey, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public string GameStopHotkey
    {
        get => _gameStopHotkey;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _gameStopHotkey, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public IRelayCommand OpenKeyBindingsWindowCommand { get; }

    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public IRelayCommand OpenAboutCommand { get; }

    public IRelayCommand AddAutoTaskRecoveryPointCommand { get; }

    public IRelayCommand<AutoTaskRecoveryPointViewModel?> RemoveAutoTaskRecoveryPointCommand { get; }

    public string SoftwareSettingsTitle => _localizationService.T("Settings.Section.Software");
    public string GameSettingsTitle => _localizationService.T("Settings.Section.Game");
    public string AutoTaskSettingsTitle => _localizationService.T("Settings.Section.AutoTasks");
    public string HelpTitle => _localizationService.T("Settings.Section.Help");

    public string AutoTaskRecoveryTitle => _localizationService.T("Settings.AutoTasks.Recovery.Title");
    public string AutoTaskRecoverySubtitle => _localizationService.T("Settings.AutoTasks.Recovery.Subtitle");
    public string AutoTaskNavigationFailureLimitTitle => _localizationService.T("Settings.AutoTasks.NavigationFailureLimit.Title");
    public string AutoTaskNavigationFailureLimitSubtitle => _localizationService.T("Settings.AutoTasks.NavigationFailureLimit.Subtitle");
    public string AutoTaskStuckTimeoutTitle => _localizationService.T("Settings.AutoTasks.StuckTimeout.Title");
    public string AutoTaskStuckTimeoutSubtitle => _localizationService.T("Settings.AutoTasks.StuckTimeout.Subtitle");
    public string AutoTaskVisualToleranceTitle => _localizationService.T("Settings.AutoTasks.VisualTolerance.Title");
    public string AutoTaskVisualToleranceSubtitle => _localizationService.T("Settings.AutoTasks.VisualTolerance.Subtitle");
    public string AutoTaskRecoveryDelayTitle => _localizationService.T("Settings.AutoTasks.RecoveryDelay.Title");
    public string AutoTaskRecoveryDelaySubtitle => _localizationService.T("Settings.AutoTasks.RecoveryDelay.Subtitle");
    public string AutoTaskRecoveryPointsTitle => _localizationService.T("Settings.AutoTasks.RecoveryPoints.Title");
    public string AutoTaskRecoveryPointsSubtitle => _localizationService.T("Settings.AutoTasks.RecoveryPoints.Subtitle");
    public string AutoTaskRecoveryPointXLabel => _localizationService.T("Settings.AutoTasks.RecoveryPoints.X");
    public string AutoTaskRecoveryPointYLabel => _localizationService.T("Settings.AutoTasks.RecoveryPoints.Y");
    public string AddAutoTaskRecoveryPointText => _localizationService.T("Settings.AutoTasks.RecoveryPoints.Add");
    public string RemoveAutoTaskRecoveryPointText => _localizationService.T("Settings.AutoTasks.RecoveryPoints.Remove");

    public int AutoTaskMaxConsecutiveNavigationFailures
    {
        get => _autoTaskMaxConsecutiveNavigationFailures;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _autoTaskMaxConsecutiveNavigationFailures, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public int AutoTaskStuckUiTimeoutSeconds
    {
        get => _autoTaskStuckUiTimeoutSeconds;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _autoTaskStuckUiTimeoutSeconds, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public int AutoTaskStuckRecoveryDelayMs
    {
        get => _autoTaskStuckRecoveryDelayMs;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _autoTaskStuckRecoveryDelayMs, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public int AutoTaskVisualFingerprintDistanceTolerance
    {
        get => _autoTaskVisualFingerprintDistanceTolerance;
        set
        {
            if (!ChildSessionRuntimeState.CanPersistSharedData)
            {
                return;
            }

            if (SetProperty(ref _autoTaskVisualFingerprintDistanceTolerance, value))
            {
                SaveCurrentConfiguration();
            }
        }
    }

    public string UiLanguageTitle => _localizationService.T("Settings.UiLanguage.Title");
    public string UiLanguageSubtitle => _localizationService.T("Settings.UiLanguage.Subtitle");

    public string ThemeTitle => _localizationService.T("Settings.Theme.Title");
    public string ThemeSubtitle => _localizationService.T("Settings.Theme.Subtitle");

    public string GameLanguageTitle => _localizationService.T("Settings.GameLanguage.Title");
    public string GameLanguageSubtitle => _localizationService.T("Settings.GameLanguage.Subtitle");

    public string KeyboardMouseSimulationTitle => _localizationService.T("Settings.InputSimulation.Title");
    public string KeyboardMouseSimulationSubtitle => _localizationService.T("Settings.InputSimulation.Subtitle");
    public string StandardKeyboardMouseSimulationTitle => _localizationService.T("Settings.InputSimulation.Standard.Title");
    public string StandardKeyboardMouseSimulationDescription => _localizationService.T("Settings.InputSimulation.Standard.Description");
    public string HardwareKeyboardMouseSimulationTitle => _localizationService.T("Settings.InputSimulation.Hardware.Title");
    public string HardwareKeyboardMouseSimulationDescription => _localizationService.T("Settings.InputSimulation.Hardware.Description");
    public string KeyboardMouseSimulationStatusText => BuildKeyboardMouseSimulationStatusText();

    public string KeyBindingsTitle => _localizationService.T("Settings.KeyBindings.Title");
    public string KeyBindingsSubtitle => _localizationService.T("Settings.KeyBindings.Subtitle");
    public string KeyBindingsBodyText => _localizationService.T("Settings.KeyBindings.Body");
    public string ConfigureText => _localizationService.T("Settings.Configure");

    public string StartPauseLabel => _localizationService.T("Settings.StartPause");
    public string StopLabel => _localizationService.T("Settings.Stop");
    public string HotkeyHint => _localizationService.T("Settings.HotkeyHint");

    public string CardUpdateTitle => _localizationService.T("Settings.Card.Update.Title");
    public string CardUpdateDescription => _localizationService.T("Settings.Card.Update.Description");
    public string CheckUpdateText => _localizationService.T("Settings.CheckUpdate");

    public string CardAboutTitle => _localizationService.T("Settings.Card.About.Title");
    public string CardAboutDescription => _localizationService.T("Settings.Card.About.Description");
    public string OpenText => _localizationService.T("Settings.Open");
    public string CurrentVersionText => $"Current version: {_applicationUpdateService.CurrentVersion}";
    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public string BetterBtdHotkeysTitle => _localizationService.T("Settings.HotkeyGroup.BetterBTD");
    public string GameHotkeysTitle => _localizationService.T("Settings.HotkeyGroup.Game");
    public string GameStartLabel => _localizationService.T("Settings.GameStart");
    public string GameStopLabel => _localizationService.T("Settings.GameStop");

    public bool IsStandardKeyboardMouseSimulationModeSelected
    {
        get => _selectedKeyboardMouseSimulationMode == KeyboardMouseSimulationMode.Standard;
        set
        {
            if (value && ChildSessionRuntimeState.CanPersistSharedData)
            {
                SetKeyboardMouseSimulationMode(KeyboardMouseSimulationMode.Standard);
            }
        }
    }

    public bool IsHardwareKeyboardMouseSimulationModeSelected
    {
        get => _selectedKeyboardMouseSimulationMode == KeyboardMouseSimulationMode.Hardware;
        set
        {
            if (value && ChildSessionRuntimeState.CanPersistSharedData)
            {
                SetKeyboardMouseSimulationMode(KeyboardMouseSimulationMode.Hardware);
            }
        }
    }

    private void UpdateUiLanguage()
    {
        if (SelectedUiLanguage is null)
        {
            return;
        }

        _localizationService.SetLanguage(SelectedUiLanguage.Code);
    }

    private void OpenKeyBindingsWindow()
    {
        var window = new KeyBindingsWindow
        {
            Owner = Application.Current?.MainWindow
        };

        window.ShowDialog();
    }

    private void SaveCurrentConfiguration()
    {
        if (_isRefreshingSelections || !ChildSessionRuntimeState.CanPersistSharedData)
        {
            return;
        }

        var current = _configurationService.Current;
        _configurationService.Save(new AppConfiguration
        {
            MaskWindowTargetTitle = current.MaskWindowTargetTitle,
            CaptureModeName = current.CaptureModeName,
            CaptureIntervalMs = current.CaptureIntervalMs,
            AutoFixWin11BitBlt = current.AutoFixWin11BitBlt,
            LaunchGameWithCapturer = current.LaunchGameWithCapturer,
            GameInstallPath = current.GameInstallPath,
            LanguageCode = SelectedUiLanguage?.Code ?? "zh-CN",
            ThemeMode = SelectedTheme?.Code ?? "Dark",
            GameLanguageCode = SelectedGameLanguage?.Code ?? "zh-CN",
            KeyboardMouseSimulationModeName = _selectedKeyboardMouseSimulationMode.ToConfigurationValue(),
            StartHotkey = StartHotkey,
            StopHotkey = StopHotkey,
            GameStartHotkey = GameStartHotkey,
            GameStopHotkey = GameStopHotkey,
            ScriptExecutionIntervalStrategyName = current.ScriptExecutionIntervalStrategyName,
            ScriptExecutionCommonOperationIntervalMs = current.ScriptExecutionCommonOperationIntervalMs,
            AutoTaskMaxConsecutiveNavigationFailures = AutoTaskMaxConsecutiveNavigationFailures,
            AutoTaskStuckUiTimeoutSeconds = AutoTaskStuckUiTimeoutSeconds,
            AutoTaskVisualFingerprintDistanceTolerance = AutoTaskVisualFingerprintDistanceTolerance,
            AutoTaskStuckRecoveryDelayMs = AutoTaskStuckRecoveryDelayMs,
            AutoTaskStuckRecoveryPoints = AutoTaskStuckRecoveryPoints
                .Select(point => new GameUiRecoveryPoint(point.X, point.Y))
                .ToList(),
            KeyBindings = current.KeyBindings
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateStatusText = await _applicationUpdateService.CheckAndPromptForUpdateAsync(silentIfUpToDate: false);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Unable to check for updates: {ex.Message}";
        }
    }

    private void OpenAbout()
    {
        _applicationUpdateService.OpenProjectHomePage();
    }

    private void LoadFromConfiguration()
    {
        var config = _configurationService.Current;
        _selectedKeyboardMouseSimulationMode = KeyboardMouseSimulationModeExtensions.Parse(config.KeyboardMouseSimulationModeName);
        _startHotkey = config.StartHotkey;
        _stopHotkey = config.StopHotkey;
        _gameStartHotkey = config.GameStartHotkey;
        _gameStopHotkey = config.GameStopHotkey;
        _autoTaskMaxConsecutiveNavigationFailures = config.AutoTaskMaxConsecutiveNavigationFailures;
        _autoTaskStuckUiTimeoutSeconds = config.AutoTaskStuckUiTimeoutSeconds;
        _autoTaskVisualFingerprintDistanceTolerance = config.AutoTaskVisualFingerprintDistanceTolerance;
        _autoTaskStuckRecoveryDelayMs = config.AutoTaskStuckRecoveryDelayMs;

        foreach (var point in config.AutoTaskStuckRecoveryPoints)
        {
            AddAutoTaskRecoveryPoint(new AutoTaskRecoveryPointViewModel(point.X, point.Y), save: false);
        }
    }

    private void RefreshOptionsAndSelections()
    {
        _isRefreshingSelections = true;
        try
        {
            BuildOptions();
        }
        finally
        {
            _isRefreshingSelections = false;
        }
    }

    private void BuildOptions()
    {
        var uiCode = SelectedUiLanguage?.Code ?? _configurationService.Current.LanguageCode;
        var gameCode = SelectedGameLanguage?.Code ?? _configurationService.Current.GameLanguageCode;
        var themeCode = SelectedTheme?.Code ?? _configurationService.Current.ThemeMode;

        UiLanguageOptions.Clear();
        UiLanguageOptions.Add(new LanguageOption { Code = "zh-CN", DisplayName = _localizationService.T("Settings.LanguageZh") });
        UiLanguageOptions.Add(new LanguageOption { Code = "en-US", DisplayName = _localizationService.T("Settings.LanguageEn") });

        GameLanguageOptions.Clear();
        GameLanguageOptions.Add(new LanguageOption { Code = "zh-CN", DisplayName = _localizationService.T("Settings.LanguageZh") });
        GameLanguageOptions.Add(new LanguageOption { Code = "en-US", DisplayName = _localizationService.T("Settings.LanguageEn") });

        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOption { Code = "Dark", DisplayName = _localizationService.T("Settings.ThemeDark") });
        ThemeOptions.Add(new ThemeOption { Code = "Light", DisplayName = _localizationService.T("Settings.ThemeLight") });

        SelectedUiLanguage = UiLanguageOptions.FirstOrDefault(x => x.Code == uiCode) ?? UiLanguageOptions.First();
        SelectedGameLanguage = GameLanguageOptions.FirstOrDefault(x => x.Code == gameCode) ?? GameLanguageOptions.First();
        SelectedTheme = ThemeOptions.FirstOrDefault(x => x.Code == themeCode) ?? ThemeOptions.First();
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(SoftwareSettingsTitle));
        OnPropertyChanged(nameof(GameSettingsTitle));
        OnPropertyChanged(nameof(AutoTaskSettingsTitle));
        OnPropertyChanged(nameof(HelpTitle));
        OnPropertyChanged(nameof(AutoTaskRecoveryTitle));
        OnPropertyChanged(nameof(AutoTaskRecoverySubtitle));
        OnPropertyChanged(nameof(AutoTaskNavigationFailureLimitTitle));
        OnPropertyChanged(nameof(AutoTaskNavigationFailureLimitSubtitle));
        OnPropertyChanged(nameof(AutoTaskStuckTimeoutTitle));
        OnPropertyChanged(nameof(AutoTaskStuckTimeoutSubtitle));
        OnPropertyChanged(nameof(AutoTaskVisualToleranceTitle));
        OnPropertyChanged(nameof(AutoTaskVisualToleranceSubtitle));
        OnPropertyChanged(nameof(AutoTaskRecoveryDelayTitle));
        OnPropertyChanged(nameof(AutoTaskRecoveryDelaySubtitle));
        OnPropertyChanged(nameof(AutoTaskRecoveryPointsTitle));
        OnPropertyChanged(nameof(AutoTaskRecoveryPointsSubtitle));
        OnPropertyChanged(nameof(AutoTaskRecoveryPointXLabel));
        OnPropertyChanged(nameof(AutoTaskRecoveryPointYLabel));
        OnPropertyChanged(nameof(AddAutoTaskRecoveryPointText));
        OnPropertyChanged(nameof(RemoveAutoTaskRecoveryPointText));
        OnPropertyChanged(nameof(UiLanguageTitle));
        OnPropertyChanged(nameof(UiLanguageSubtitle));
        OnPropertyChanged(nameof(ThemeTitle));
        OnPropertyChanged(nameof(ThemeSubtitle));
        OnPropertyChanged(nameof(GameLanguageTitle));
        OnPropertyChanged(nameof(GameLanguageSubtitle));
        OnPropertyChanged(nameof(KeyboardMouseSimulationTitle));
        OnPropertyChanged(nameof(KeyboardMouseSimulationSubtitle));
        OnPropertyChanged(nameof(StandardKeyboardMouseSimulationTitle));
        OnPropertyChanged(nameof(StandardKeyboardMouseSimulationDescription));
        OnPropertyChanged(nameof(HardwareKeyboardMouseSimulationTitle));
        OnPropertyChanged(nameof(HardwareKeyboardMouseSimulationDescription));
        OnPropertyChanged(nameof(KeyboardMouseSimulationStatusText));
        OnPropertyChanged(nameof(KeyBindingsTitle));
        OnPropertyChanged(nameof(KeyBindingsSubtitle));
        OnPropertyChanged(nameof(KeyBindingsBodyText));
        OnPropertyChanged(nameof(ConfigureText));
        OnPropertyChanged(nameof(StartPauseLabel));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(HotkeyHint));
        OnPropertyChanged(nameof(CardUpdateTitle));
        OnPropertyChanged(nameof(CardUpdateDescription));
        OnPropertyChanged(nameof(CheckUpdateText));
        OnPropertyChanged(nameof(CardAboutTitle));
        OnPropertyChanged(nameof(CardAboutDescription));
        OnPropertyChanged(nameof(OpenText));
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(BetterBtdHotkeysTitle));
        OnPropertyChanged(nameof(GameHotkeysTitle));
        OnPropertyChanged(nameof(GameStartLabel));
        OnPropertyChanged(nameof(GameStopLabel));
    }

    private void SetKeyboardMouseSimulationMode(KeyboardMouseSimulationMode mode)
    {
        if (_selectedKeyboardMouseSimulationMode == mode)
        {
            return;
        }

        _selectedKeyboardMouseSimulationMode = mode;
        _configurationService.Current.KeyboardMouseSimulationModeName = mode.ToConfigurationValue();
        OnPropertyChanged(nameof(IsStandardKeyboardMouseSimulationModeSelected));
        OnPropertyChanged(nameof(IsHardwareKeyboardMouseSimulationModeSelected));
        OnPropertyChanged(nameof(KeyboardMouseSimulationStatusText));
        SaveCurrentConfiguration();
    }

    private string BuildKeyboardMouseSimulationStatusText()
    {
        var hardwareSimulationService = HardwareInputSimulationService.Instance;
        return _selectedKeyboardMouseSimulationMode switch
        {
            KeyboardMouseSimulationMode.Hardware when hardwareSimulationService.IsDriverInstalled =>
                _localizationService.T("Settings.InputSimulation.Hardware.Status.Available"),
            KeyboardMouseSimulationMode.Hardware =>
                _localizationService.T("Settings.InputSimulation.Hardware.Status.Unavailable"),
            _ => _localizationService.T("Settings.InputSimulation.Standard.Status")
        };
    }

    private bool CanAddAutoTaskRecoveryPoint()
    {
        return ChildSessionRuntimeState.CanPersistSharedData && AutoTaskStuckRecoveryPoints.Count < 20;
    }

    private void AddAutoTaskRecoveryPoint()
    {
        AddAutoTaskRecoveryPoint(new AutoTaskRecoveryPointViewModel(960, 540), save: true);
    }

    private void AddAutoTaskRecoveryPoint(AutoTaskRecoveryPointViewModel point, bool save)
    {
        point.PropertyChanged += OnAutoTaskRecoveryPointChanged;
        AutoTaskStuckRecoveryPoints.Add(point);
        AddAutoTaskRecoveryPointCommand.NotifyCanExecuteChanged();
        if (save)
        {
            SaveCurrentConfiguration();
        }
    }

    private void RemoveAutoTaskRecoveryPoint(AutoTaskRecoveryPointViewModel? point)
    {
        if (!ChildSessionRuntimeState.CanPersistSharedData ||
            point is null ||
            !AutoTaskStuckRecoveryPoints.Remove(point))
        {
            return;
        }

        point.PropertyChanged -= OnAutoTaskRecoveryPointChanged;
        AddAutoTaskRecoveryPointCommand.NotifyCanExecuteChanged();
        SaveCurrentConfiguration();
    }

    private void OnAutoTaskRecoveryPointChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveCurrentConfiguration();
    }
}

public sealed class AutoTaskRecoveryPointViewModel : ObservableObject
{
    private int _x;
    private int _y;

    public AutoTaskRecoveryPointViewModel(int x, int y)
    {
        _x = x;
        _y = y;
    }

    public int X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public int Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }
}
