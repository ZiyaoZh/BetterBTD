using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Services;

namespace BetterBTD.ViewModels;

public sealed class StartPageViewModel : ObservableObject
{
    private readonly LocalizationService _localizationService;
    private readonly MaskWindowService _maskWindowService;

    private bool _isCapturerRunning;
    private string _selectedCaptureMode = "BitBlt";
    private int _triggerIntervalMs = 50;
    private bool _captureBitmapCacheEnabled;
    private string _selectedInferenceDevice = "Auto";
    private bool _linkedStartEnabled;
    private string _installPath = string.Empty;
    private string _startArguments = string.Empty;
    private bool _autoEnterGameEnabled;
    private bool _startGameWithCmd;
    private bool _recordGameTimeEnabled;
    private bool _autoFixWin11BitBlt;

    public StartPageViewModel(LocalizationService localizationService, MaskWindowService maskWindowService)
    {
        _localizationService = localizationService;
        _maskWindowService = maskWindowService;
        _localizationService.LanguageChanged += (_, _) => RaiseLocalizedProperties();
        _maskWindowService.RunningStateChanged += OnMaskWindowRunningStateChanged;

        CaptureModes = ["BitBlt", "WindowsGraphicsCapture"];
        InferenceDevices = ["Auto", "CPU", "GPU"];

        OpenTutorialCommand = new RelayCommand(OpenTutorial);
        StartCaptureCommand = new RelayCommand(StartCapture);
        StopCaptureCommand = new RelayCommand(StopCapture);
        ChangeBannerImageCommand = new RelayCommand(() => { });
        ResetBannerImageCommand = new RelayCommand(() => { });
        OpenHardwareAccelerationSettingsCommand = new RelayCommand(() => { });
        StartCaptureTestCommand = new RelayCommand(() => { });
        ManualPickWindowCommand = new RelayCommand(RefreshMaskWindowTarget);
        OpenDisplayAdvancedGraphicsSettingsCommand = new RelayCommand(() => { });
        OpenGameCommandLineDocumentCommand = new RelayCommand(OpenTutorial);
        SelectInstallPathCommand = new RelayCommand(() => { });

        IsCapturerRunning = _maskWindowService.IsRunning;
    }

    public ObservableCollection<string> CaptureModes { get; }

    public ObservableCollection<string> InferenceDevices { get; }

    public IRelayCommand OpenTutorialCommand { get; }
    public IRelayCommand StartCaptureCommand { get; }
    public IRelayCommand StopCaptureCommand { get; }
    public IRelayCommand ChangeBannerImageCommand { get; }
    public IRelayCommand ResetBannerImageCommand { get; }
    public IRelayCommand OpenHardwareAccelerationSettingsCommand { get; }
    public IRelayCommand StartCaptureTestCommand { get; }
    public IRelayCommand ManualPickWindowCommand { get; }
    public IRelayCommand OpenDisplayAdvancedGraphicsSettingsCommand { get; }
    public IRelayCommand OpenGameCommandLineDocumentCommand { get; }
    public IRelayCommand SelectInstallPathCommand { get; }

    public bool IsCapturerRunning
    {
        get => _isCapturerRunning;
        set => SetProperty(ref _isCapturerRunning, value);
    }

    public string SelectedCaptureMode
    {
        get => _selectedCaptureMode;
        set => SetProperty(ref _selectedCaptureMode, value);
    }

    public int TriggerIntervalMs
    {
        get => _triggerIntervalMs;
        set => SetProperty(ref _triggerIntervalMs, value);
    }

    public bool CaptureBitmapCacheEnabled
    {
        get => _captureBitmapCacheEnabled;
        set => SetProperty(ref _captureBitmapCacheEnabled, value);
    }

    public string SelectedInferenceDevice
    {
        get => _selectedInferenceDevice;
        set => SetProperty(ref _selectedInferenceDevice, value);
    }

    public bool LinkedStartEnabled
    {
        get => _linkedStartEnabled;
        set => SetProperty(ref _linkedStartEnabled, value);
    }

    public string InstallPath
    {
        get => _installPath;
        set => SetProperty(ref _installPath, value);
    }

    public string StartArguments
    {
        get => _startArguments;
        set => SetProperty(ref _startArguments, value);
    }

    public bool AutoEnterGameEnabled
    {
        get => _autoEnterGameEnabled;
        set => SetProperty(ref _autoEnterGameEnabled, value);
    }

    public bool StartGameWithCmd
    {
        get => _startGameWithCmd;
        set => SetProperty(ref _startGameWithCmd, value);
    }

    public bool RecordGameTimeEnabled
    {
        get => _recordGameTimeEnabled;
        set => SetProperty(ref _recordGameTimeEnabled, value);
    }

    public bool AutoFixWin11BitBlt
    {
        get => _autoFixWin11BitBlt;
        set => SetProperty(ref _autoFixWin11BitBlt, value);
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
    public string CaptureBitmapCacheTitle => _localizationService.T("Start.CaptureBitmapCacheTitle");
    public string CaptureBitmapCacheDescription => _localizationService.T("Start.CaptureBitmapCacheDescription");
    public string InferenceDeviceTitle => _localizationService.T("Start.InferenceDeviceTitle");
    public string InferenceDeviceDescription => _localizationService.T("Start.InferenceDeviceDescription");
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
    public string StartArgsTitle => _localizationService.T("Start.StartArgsTitle");
    public string StartArgsDescription => _localizationService.T("Start.StartArgsDescription");
    public string OpenDocText => _localizationService.T("Start.OpenDoc");
    public string AutoEnterGameTitle => _localizationService.T("Start.AutoEnterGameTitle");
    public string AutoEnterGameDescription => _localizationService.T("Start.AutoEnterGameDescription");
    public string StartWithCmdTitle => _localizationService.T("Start.StartWithCmdTitle");
    public string StartWithCmdDescription => _localizationService.T("Start.StartWithCmdDescription");
    public string RecordGameTimeTitle => _localizationService.T("Start.RecordGameTimeTitle");
    public string RecordGameTimeDescription => _localizationService.T("Start.RecordGameTimeDescription");
    public string BrowseText => _localizationService.T("Start.Browse");
    public string MoreText => _localizationService.T("Start.More");

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

    private void StartCapture()
    {
        _maskWindowService.Start();
        IsCapturerRunning = _maskWindowService.IsRunning;
    }

    private void StopCapture()
    {
        _maskWindowService.Stop();
        IsCapturerRunning = _maskWindowService.IsRunning;
    }

    private void RefreshMaskWindowTarget()
    {
        _maskWindowService.Start();
        _maskWindowService.RefreshNow();
        IsCapturerRunning = _maskWindowService.IsRunning;
    }

    private void OnMaskWindowRunningStateChanged(object? sender, bool isRunning)
    {
        IsCapturerRunning = isRunning;
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
        OnPropertyChanged(nameof(CaptureBitmapCacheTitle));
        OnPropertyChanged(nameof(CaptureBitmapCacheDescription));
        OnPropertyChanged(nameof(InferenceDeviceTitle));
        OnPropertyChanged(nameof(InferenceDeviceDescription));
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
        OnPropertyChanged(nameof(StartArgsTitle));
        OnPropertyChanged(nameof(StartArgsDescription));
        OnPropertyChanged(nameof(OpenDocText));
        OnPropertyChanged(nameof(AutoEnterGameTitle));
        OnPropertyChanged(nameof(AutoEnterGameDescription));
        OnPropertyChanged(nameof(StartWithCmdTitle));
        OnPropertyChanged(nameof(StartWithCmdDescription));
        OnPropertyChanged(nameof(RecordGameTimeTitle));
        OnPropertyChanged(nameof(RecordGameTimeDescription));
        OnPropertyChanged(nameof(BrowseText));
        OnPropertyChanged(nameof(MoreText));
    }
}
