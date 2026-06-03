using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Models;
using BetterBTD.Services;
using BetterBTD.Services.Updates;
using BetterBTD.Views.Pages;
using BetterBTD.Views.Windows;

namespace BetterBTD.ViewModels;

public sealed class SettingsPageViewModel : ObservableObject
{
    private readonly ConfigurationService _configurationService;
    private readonly LocalizationService _localizationService;
    private readonly ThemeService _themeService;
    private readonly ApplicationUpdateService _applicationUpdateService;

    private LanguageOption? _selectedUiLanguage;
    private LanguageOption? _selectedGameLanguage;
    private ThemeOption? _selectedTheme;
    private KeyboardMouseSimulationMode _selectedKeyboardMouseSimulationMode;
    private string _startHotkey = string.Empty;
    private string _stopHotkey = string.Empty;
    private string _gameStartHotkey = string.Empty;
    private string _gameStopHotkey = string.Empty;
    private string _updateStatusText = string.Empty;

    public SettingsPageViewModel()
    {
        _configurationService = ConfigurationService.Instance;
        _localizationService = LocalizationService.Instance;
        _themeService = ThemeService.Instance;
        _applicationUpdateService = ApplicationUpdateService.Instance;

        UiLanguageOptions = [];
        GameLanguageOptions = [];
        ThemeOptions = [];

        UpdateUiLanguageCommand = new RelayCommand(UpdateUiLanguage);
        OpenKeyBindingsWindowCommand = new RelayCommand(OpenKeyBindingsWindow);
        CheckUpdateCommand = new RelayCommand(OpenUpdater);
        OpenAboutCommand = new RelayCommand(OpenAbout);
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(ResetDefaults);

        LoadFromConfiguration();
        BuildOptions();
        ApplySelections();

        _localizationService.LanguageChanged += (_, _) =>
        {
            BuildOptions();
            RaiseLocalizedProperties();
        };
    }

    public ObservableCollection<LanguageOption> UiLanguageOptions { get; }

    public ObservableCollection<LanguageOption> GameLanguageOptions { get; }

    public ObservableCollection<ThemeOption> ThemeOptions { get; }

    public LanguageOption? SelectedUiLanguage
    {
        get => _selectedUiLanguage;
        set
        {
            if (!SetProperty(ref _selectedUiLanguage, value) || value is null)
            {
                return;
            }

            UpdateUiLanguage();
        }
    }

    public LanguageOption? SelectedGameLanguage
    {
        get => _selectedGameLanguage;
        set => SetProperty(ref _selectedGameLanguage, value);
    }

    public ThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value) || value is null)
            {
                return;
            }

            _themeService.ApplyTheme(value.Code);
        }
    }

    public string StartHotkey
    {
        get => _startHotkey;
        set => SetProperty(ref _startHotkey, value);
    }

    public string StopHotkey
    {
        get => _stopHotkey;
        set => SetProperty(ref _stopHotkey, value);
    }

    public string GameStartHotkey
    {
        get => _gameStartHotkey;
        set => SetProperty(ref _gameStartHotkey, value);
    }

    public string GameStopHotkey
    {
        get => _gameStopHotkey;
        set => SetProperty(ref _gameStopHotkey, value);
    }

    public IRelayCommand UpdateUiLanguageCommand { get; }

    public IRelayCommand OpenKeyBindingsWindowCommand { get; }

    public IRelayCommand CheckUpdateCommand { get; }

    public IRelayCommand OpenAboutCommand { get; }

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public string SoftwareSettingsTitle => _localizationService.T("Settings.Section.Software");
    public string GameSettingsTitle => _localizationService.T("Settings.Section.Game");
    public string HelpTitle => _localizationService.T("Settings.Section.Help");

    public string UiLanguageTitle => _localizationService.T("Settings.UiLanguage.Title");
    public string UiLanguageSubtitle => _localizationService.T("Settings.UiLanguage.Subtitle");
    public string UpdateText => _localizationService.T("Settings.Update");

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

    public string SaveButtonText => _localizationService.T("Settings.Save");
    public string ResetButtonText => _localizationService.T("Settings.Reset");

    public string BetterBtdHotkeysTitle => _localizationService.T("Settings.HotkeyGroup.BetterBTD");
    public string GameHotkeysTitle => _localizationService.T("Settings.HotkeyGroup.Game");
    public string GameStartLabel => _localizationService.T("Settings.GameStart");
    public string GameStopLabel => _localizationService.T("Settings.GameStop");

    public bool IsStandardKeyboardMouseSimulationModeSelected
    {
        get => _selectedKeyboardMouseSimulationMode == KeyboardMouseSimulationMode.Standard;
        set
        {
            if (value)
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
            if (value)
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

    private void Save()
    {
        _configurationService.Save(new AppConfiguration
        {
            LanguageCode = SelectedUiLanguage?.Code ?? "zh-CN",
            ThemeMode = SelectedTheme?.Code ?? "Dark",
            GameLanguageCode = SelectedGameLanguage?.Code ?? "zh-CN",
            KeyboardMouseSimulationModeName = _selectedKeyboardMouseSimulationMode.ToConfigurationValue(),
            StartHotkey = StartHotkey,
            StopHotkey = StopHotkey,
            GameStartHotkey = GameStartHotkey,
            GameStopHotkey = GameStopHotkey
        });
    }

    private void OpenUpdater()
    {
        try
        {
            _applicationUpdateService.TryLaunchUpdater(out var message);
            UpdateStatusText = message;
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Unable to open updater: {ex.Message}";
        }
    }

    private void OpenAbout()
    {
        _applicationUpdateService.OpenProjectHomePage();
    }

    private void ResetDefaults()
    {
        SelectedUiLanguage = UiLanguageOptions.FirstOrDefault(x => x.Code == "zh-CN");
        SelectedGameLanguage = GameLanguageOptions.FirstOrDefault(x => x.Code == "zh-CN");
        SelectedTheme = ThemeOptions.FirstOrDefault(x => x.Code == "Dark");
        SetKeyboardMouseSimulationMode(KeyboardMouseSimulationMode.Standard);
        StartHotkey = "F1";
        StopHotkey = "F2";
        GameStartHotkey = "F5";
        GameStopHotkey = "F6";

        UpdateUiLanguage();
    }

    private void LoadFromConfiguration()
    {
        var config = _configurationService.Current;
        SetKeyboardMouseSimulationMode(KeyboardMouseSimulationModeExtensions.Parse(config.KeyboardMouseSimulationModeName));
        StartHotkey = config.StartHotkey;
        StopHotkey = config.StopHotkey;
        GameStartHotkey = config.GameStartHotkey;
        GameStopHotkey = config.GameStopHotkey;
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

    private void ApplySelections()
    {
        SelectedUiLanguage = UiLanguageOptions.FirstOrDefault(x => x.Code == _configurationService.Current.LanguageCode) ?? UiLanguageOptions.First();
        SelectedGameLanguage = GameLanguageOptions.FirstOrDefault(x => x.Code == _configurationService.Current.GameLanguageCode) ?? GameLanguageOptions.First();
        SelectedTheme = ThemeOptions.FirstOrDefault(x => x.Code == _configurationService.Current.ThemeMode) ?? ThemeOptions.First();
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(SoftwareSettingsTitle));
        OnPropertyChanged(nameof(GameSettingsTitle));
        OnPropertyChanged(nameof(HelpTitle));
        OnPropertyChanged(nameof(UiLanguageTitle));
        OnPropertyChanged(nameof(UiLanguageSubtitle));
        OnPropertyChanged(nameof(UpdateText));
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
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(ResetButtonText));
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
}
