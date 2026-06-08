using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Services;
using BetterBTD.Services.Start;
using BetterBTD.Views.Windows;
using Fischless.GameCapture.BitBlt;
using Microsoft.Win32;

namespace BetterBTD.ViewModels;

public sealed class StartPageViewModel : ObservableObject
{
    private static readonly TimeSpan LinkedStartWindowWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LinkedStartWindowPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly LocalizationService _localizationService;
    private readonly MaskWindowService _maskWindowService;
    private readonly GameCaptureService _gameCaptureService;
    private readonly GameLaunchService _gameLaunchService;
    private readonly ConfigurationService _configurationService;
    private readonly AppDialogService _appDialogService;

    private bool _isCapturerRunning;
    private string _selectedCaptureMode = nameof(Fischless.GameCapture.CaptureModes.WindowsGraphicsCapture);
    private int _triggerIntervalMs = 50;
    private bool _linkedStartEnabled;
    private string _installPath = string.Empty;
    private bool _autoFixWin11BitBlt;
    private bool _isLoadingConfiguration;

    public StartPageViewModel(LocalizationService localizationService, MaskWindowService maskWindowService)
    {
        _localizationService = localizationService;
        _maskWindowService = maskWindowService;
        _gameCaptureService = GameCaptureService.Instance;
        _gameLaunchService = GameLaunchService.Instance;
        _configurationService = ConfigurationService.Instance;
        _appDialogService = AppDialogService.Instance;

        _localizationService.LanguageChanged += (_, _) => RaiseLocalizedProperties();
        _gameCaptureService.RunningStateChanged += OnGameCaptureRunningStateChanged;

        CaptureModes = new ObservableCollection<string>(_gameCaptureService.AvailableCaptureModes);

        OpenTutorialCommand = new RelayCommand(OpenTutorial);
        StartCaptureCommand = new AsyncRelayCommand(StartCaptureAsync);
        StopCaptureCommand = new RelayCommand(StopCapture);
        ChangeBannerImageCommand = new RelayCommand(() => { });
        ResetBannerImageCommand = new RelayCommand(() => { });
        StartCaptureTestCommand = new RelayCommand(StartCaptureTest);
        ManualPickWindowCommand = new RelayCommand(ManualPickWindow);
        OpenDisplayAdvancedGraphicsSettingsCommand = new RelayCommand(OpenDisplayAdvancedGraphicsSettings);
        SelectInstallPathCommand = new RelayCommand(SelectInstallPath);

        LoadConfiguration();
        IsCapturerRunning = _gameCaptureService.IsRunning;
    }

    public ObservableCollection<string> CaptureModes { get; }

    public IRelayCommand OpenTutorialCommand { get; }
    public IAsyncRelayCommand StartCaptureCommand { get; }
    public IRelayCommand StopCaptureCommand { get; }
    public IRelayCommand ChangeBannerImageCommand { get; }
    public IRelayCommand ResetBannerImageCommand { get; }
    public IRelayCommand StartCaptureTestCommand { get; }
    public IRelayCommand ManualPickWindowCommand { get; }
    public IRelayCommand OpenDisplayAdvancedGraphicsSettingsCommand { get; }
    public IRelayCommand SelectInstallPathCommand { get; }

    public bool IsCapturerRunning
    {
        get => _isCapturerRunning;
        set => SetProperty(ref _isCapturerRunning, value);
    }

    public string SelectedCaptureMode
    {
        get => _selectedCaptureMode;
        set
        {
            if (!SetProperty(ref _selectedCaptureMode, value))
            {
                return;
            }

            PersistConfiguration(config => config.CaptureModeName = value);
            _gameCaptureService.Configure(BuildCaptureOptions());

            if (_gameCaptureService.IsRunning)
            {
                RestartCapture();
            }
        }
    }

    public int TriggerIntervalMs
    {
        get => _triggerIntervalMs;
        set
        {
            var normalizedValue = Math.Clamp(value, 10, 2000);
            if (!SetProperty(ref _triggerIntervalMs, normalizedValue))
            {
                return;
            }

            PersistConfiguration(config => config.CaptureIntervalMs = normalizedValue);
            _gameCaptureService.Configure(BuildCaptureOptions());

            if (_gameCaptureService.IsRunning)
            {
                RestartCapture();
            }
        }
    }

    public bool LinkedStartEnabled
    {
        get => _linkedStartEnabled;
        set
        {
            if (!SetProperty(ref _linkedStartEnabled, value))
            {
                return;
            }

            PersistConfiguration(config => config.LaunchGameWithCapturer = value);
        }
    }

    public string InstallPath
    {
        get => _installPath;
        set
        {
            var normalizedValue = value?.Trim() ?? string.Empty;
            if (!SetProperty(ref _installPath, normalizedValue))
            {
                return;
            }

            OnPropertyChanged(nameof(InstallPathDisplayText));
            PersistConfiguration(config => config.GameInstallPath = normalizedValue);
        }
    }

    public bool AutoFixWin11BitBlt
    {
        get => _autoFixWin11BitBlt;
        set
        {
            if (!SetProperty(ref _autoFixWin11BitBlt, value))
            {
                return;
            }

            PersistConfiguration(config => config.AutoFixWin11BitBlt = value);
            _gameCaptureService.Configure(BuildCaptureOptions());

            if (value && OsVersionHelper.IsWindows11_OrGreater)
            {
                BitBltRegistryHelper.SetDirectXUserGlobalSettings();
            }
        }
    }

    public string HeroImageTitle => _localizationService.T("Start.HeroImageTitle");
    public string HeroImageHint => _localizationService.T("Start.HeroImageHint");
    public string ActionPanelTitle => _localizationService.T("Start.ActionPanelTitle");
    public string ActionPanelDescription => _localizationService.T("Start.ActionPanelDescription");
    public string LaunchGameText => _localizationService.T("Start.LaunchGame");
    public string LaunchCaptureText => _localizationService.T("Start.LaunchCapture");
    public string StopCaptureText => _localizationService.T("Start.StopCapture");
    public string StartHint => _localizationService.T("Start.Hint");
    public string BannerTitle => _localizationService.T("Start.BannerTitle");
    public string BannerSubtitle => _localizationService.T("Start.BannerSubtitle");
    public string BannerLinkText => _localizationService.T("Start.BannerLinkText");
    public string ChangeBannerText => _localizationService.T("Start.ChangeBanner");
    public string ResetBannerText => _localizationService.T("Start.ResetBanner");
    public string CaptureCardTitle => _localizationService.T("Start.CaptureCardTitle");
    public string CaptureCardDescription => _localizationService.T("Start.CaptureCardDescription");
    public string CaptureModeTitle => _localizationService.T("Start.CaptureModeTitle");
    public string CaptureModeDescription => _localizationService.T("Start.CaptureModeDescription");
    public string TriggerIntervalTitle => _localizationService.T("Start.TriggerIntervalTitle");
    public string TriggerIntervalDescription => _localizationService.T("Start.TriggerIntervalDescription");
    public string CaptureTestTitle => _localizationService.T("Start.CaptureTestTitle");
    public string CaptureTestDescription => _localizationService.T("Start.CaptureTestDescription");
    public string CaptureTestButtonText => _localizationService.T("Start.CaptureTestButtonText");
    public string ManualPickWindowTitle => _localizationService.T("Start.ManualPickWindowTitle");
    public string ManualPickWindowDescription => _localizationService.T("Start.ManualPickWindowDescription");
    public string ManualPickWindowButtonText => _localizationService.T("Start.ManualPickWindowButtonText");
    public string AutoFixWin11Title => _localizationService.T("Start.AutoFixWin11Title");
    public string AutoFixWin11Description => _localizationService.T("Start.AutoFixWin11Description");
    public string ManualSettingsText => _localizationService.T("Start.ManualSettings");
    public string LinkedStartTitle => _localizationService.T("Start.LinkedStartTitle");
    public string LinkedStartDescription => _localizationService.T("Start.LinkedStartDescription");
    public string InstallPathTitle => _localizationService.T("Start.InstallPathTitle");
    public string InstallPathDescription => _localizationService.T("Start.InstallPathDescription");
    public string InstallPathDisplayText => string.IsNullOrWhiteSpace(InstallPath)
        ? InstallPathDescription
        : InstallPath;
    public string BrowseText => _localizationService.T("Start.Browse");
    public string SelectInstallPathDialogTitle => _localizationService.T("Start.SelectInstallPathDialogTitle");

    private void OpenTutorial()
    {
        var tutorialUrl = _localizationService.T("Tasks.TutorialUrl");
        if (string.IsNullOrWhiteSpace(tutorialUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = tutorialUrl,
            UseShellExecute = true
        });
    }

    private async Task StartCaptureAsync()
    {
        var options = BuildCaptureOptions();
        _gameCaptureService.Configure(options);

        if (LinkedStartEnabled)
        {
            var launchResult = await _gameLaunchService.EnsureGameStartedAsync(
                InstallPath,
                LinkedStartWindowWaitTimeout,
                LinkedStartWindowPollInterval);
            if (!launchResult.Success)
            {
                ShowErrorDialog("启动游戏失败", launchResult.Message);
                return;
            }
        }

        try
        {
            if (!_gameCaptureService.TryStart(options, out _))
            {
                ShowTargetWindowNotFoundDialog();
                return;
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog("启动截图器失败", ex.Message);
            return;
        }

        _maskWindowService.Start();
        _maskWindowService.RefreshNow();
        IsCapturerRunning = _gameCaptureService.IsRunning;
    }

    private void StopCapture()
    {
        _gameCaptureService.Stop();
        _maskWindowService.Stop();
        IsCapturerRunning = _gameCaptureService.IsRunning;
    }

    private void StartCaptureTest()
    {
        var owner = GetActiveWindow();
        var picker = new PickerWindow(isCaptureTest: true);
        if (!picker.TryPickCaptureTarget(owner, out var selectedWindow))
        {
            return;
        }

        try
        {
            var captureTestWindow = new CaptureTestWindow();
            if (owner is not null)
            {
                captureTestWindow.Owner = owner;
            }

            captureTestWindow.StartCapture(selectedWindow.Handle, BuildCaptureOptions(), selectedWindow.DisplayName);
            captureTestWindow.Show();
        }
        catch (Exception ex)
        {
            ShowErrorDialog("测试图像捕获失败", ex.Message);
        }
    }

    private void ManualPickWindow()
    {
        var owner = GetActiveWindow();
        var picker = new PickerWindow();
        if (!picker.TryPickCaptureTarget(owner, out var selectedWindow))
        {
            return;
        }

        PersistConfiguration(config => config.MaskWindowTargetTitle = selectedWindow.Title);
        _gameCaptureService.Configure(BuildCaptureOptions());

        try
        {
            _gameCaptureService.Start(selectedWindow.Handle, BuildCaptureOptions());
            _maskWindowService.Start();
            _maskWindowService.RefreshNow();
            IsCapturerRunning = _gameCaptureService.IsRunning;
        }
        catch (Exception ex)
        {
            _maskWindowService.Stop();
            ShowErrorDialog("启动截图器失败", ex.Message);
        }
    }

    private void OpenDisplayAdvancedGraphicsSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:display-advancedgraphics",
            UseShellExecute = true
        });
    }

    private void SelectInstallPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = SelectInstallPathDialogTitle
        };

        var initialDirectory = ResolveInitialDirectory(InstallPath);
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var owner = GetActiveWindow();
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        InstallPath = dialog.FolderName;
    }

    private void OnGameCaptureRunningStateChanged(object? sender, bool isRunning)
    {
        if (!isRunning)
        {
            _maskWindowService.Stop();
        }

        IsCapturerRunning = isRunning;
    }

    private void RestartCapture()
    {
        try
        {
            _gameCaptureService.Configure(BuildCaptureOptions());
            _gameCaptureService.Restart();
            _maskWindowService.Start();
            _maskWindowService.RefreshNow();
        }
        catch (Exception ex)
        {
            ShowErrorDialog("重启截图器失败", ex.Message);
        }
    }

    private GameCaptureOptions BuildCaptureOptions()
    {
        return new GameCaptureOptions
        {
            CaptureModeName = SelectedCaptureMode,
            CaptureIntervalMs = TriggerIntervalMs,
            AutoFixWin11BitBlt = AutoFixWin11BitBlt
        };
    }

    private void LoadConfiguration()
    {
        _isLoadingConfiguration = true;
        try
        {
            var configuration = _configurationService.Current;
            var configuredCaptureMode = configuration.CaptureModeName;
            if (string.IsNullOrWhiteSpace(configuredCaptureMode) ||
                !CaptureModes.Contains(configuredCaptureMode))
            {
                configuredCaptureMode = CaptureModes.FirstOrDefault() ?? nameof(Fischless.GameCapture.CaptureModes.WindowsGraphicsCapture);
            }

            _selectedCaptureMode = configuredCaptureMode;
            _triggerIntervalMs = Math.Clamp(configuration.CaptureIntervalMs <= 0 ? 50 : configuration.CaptureIntervalMs, 10, 2000);
            _linkedStartEnabled = configuration.LaunchGameWithCapturer;
            _installPath = configuration.GameInstallPath?.Trim() ?? string.Empty;
            _autoFixWin11BitBlt = configuration.AutoFixWin11BitBlt;
            _gameCaptureService.Configure(BuildCaptureOptions());
        }
        finally
        {
            _isLoadingConfiguration = false;
        }
    }

    private void PersistConfiguration(Action<AppConfiguration> updateConfiguration)
    {
        if (_isLoadingConfiguration)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(updateConfiguration);

        var configuration = _configurationService.Current;
        updateConfiguration(configuration);
        _configurationService.Save(configuration);
    }

    private void ShowTargetWindowNotFoundDialog()
    {
        var windowTitle = _gameCaptureService.TargetWindowTitle;
        var message = string.IsNullOrWhiteSpace(windowTitle)
            ? "未找到目标窗口。请先启动游戏，或使用“手动选择窗口”指定捕获对象。"
            : $"未找到目标窗口“{windowTitle}”。请先启动游戏，或使用“手动选择窗口”指定捕获对象。";
        ShowErrorDialog("未找到目标窗口", message);
    }

    private void ShowErrorDialog(string title, string message)
    {
        _ = _appDialogService.Show(new AppDialogRequest
        {
            Title = title,
            Message = message,
            PrimaryButtonText = "确定"
        });
    }

    private static Window? GetActiveWindow()
    {
        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive) ?? Application.Current?.MainWindow;
    }

    private static string? ResolveInitialDirectory(string installPath)
    {
        var normalizedPath = Environment.ExpandEnvironmentVariables(installPath?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        if (Directory.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        if (File.Exists(normalizedPath))
        {
            return Path.GetDirectoryName(normalizedPath);
        }

        return null;
    }

    private void RaiseLocalizedProperties()
    {
        OnPropertyChanged(nameof(HeroImageTitle));
        OnPropertyChanged(nameof(HeroImageHint));
        OnPropertyChanged(nameof(ActionPanelTitle));
        OnPropertyChanged(nameof(ActionPanelDescription));
        OnPropertyChanged(nameof(LaunchGameText));
        OnPropertyChanged(nameof(LaunchCaptureText));
        OnPropertyChanged(nameof(StopCaptureText));
        OnPropertyChanged(nameof(StartHint));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerSubtitle));
        OnPropertyChanged(nameof(BannerLinkText));
        OnPropertyChanged(nameof(ChangeBannerText));
        OnPropertyChanged(nameof(ResetBannerText));
        OnPropertyChanged(nameof(CaptureCardTitle));
        OnPropertyChanged(nameof(CaptureCardDescription));
        OnPropertyChanged(nameof(CaptureModeTitle));
        OnPropertyChanged(nameof(CaptureModeDescription));
        OnPropertyChanged(nameof(TriggerIntervalTitle));
        OnPropertyChanged(nameof(TriggerIntervalDescription));
        OnPropertyChanged(nameof(CaptureTestTitle));
        OnPropertyChanged(nameof(CaptureTestDescription));
        OnPropertyChanged(nameof(CaptureTestButtonText));
        OnPropertyChanged(nameof(ManualPickWindowTitle));
        OnPropertyChanged(nameof(ManualPickWindowDescription));
        OnPropertyChanged(nameof(ManualPickWindowButtonText));
        OnPropertyChanged(nameof(AutoFixWin11Title));
        OnPropertyChanged(nameof(AutoFixWin11Description));
        OnPropertyChanged(nameof(ManualSettingsText));
        OnPropertyChanged(nameof(LinkedStartTitle));
        OnPropertyChanged(nameof(LinkedStartDescription));
        OnPropertyChanged(nameof(InstallPathTitle));
        OnPropertyChanged(nameof(InstallPathDescription));
        OnPropertyChanged(nameof(InstallPathDisplayText));
        OnPropertyChanged(nameof(BrowseText));
        OnPropertyChanged(nameof(SelectInstallPathDialogTitle));
    }
}
