using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterBTD.Services.ChildSession;

namespace BetterBTD.ViewModels;

internal sealed class ChildSessionWindowViewModel : ObservableObject, IDisposable
{
    private readonly ChildSessionService _service;
    private readonly Window _window;
    private readonly LocalizationService _localizationService;
    private string _statusText = string.Empty;
    private bool _disposed;

    public ChildSessionWindowViewModel(ChildSessionService service, Window window)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _localizationService = LocalizationService.Instance;
        ReconnectCommand = new AsyncRelayCommand(() => _service.StartAsync());
        HideCommand = new RelayCommand(_service.HideWindow);
        LogoffCommand = new AsyncRelayCommand(_service.LogoffAndHideAsync);
        ToggleMuteCommand = new RelayCommand(_service.ToggleAudioMute);
        _service.StateChanged += OnStateChanged;
        _localizationService.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    public string TitleText => $"{_localizationService.T("ChildSession.Title")} | {_statusText}";

    public string ReconnectText => _localizationService.T("ChildSession.Reconnect");

    public string HideText => _localizationService.T("ChildSession.Hide");

    public string LogoffText => _localizationService.T("ChildSession.Logoff");

    public string MuteText => _service.IsAudioMuted
        ? _localizationService.T("ChildSession.Unmute")
        : _localizationService.T("ChildSession.Mute");

    public IAsyncRelayCommand ReconnectCommand { get; }

    public IRelayCommand HideCommand { get; }

    public IAsyncRelayCommand LogoffCommand { get; }

    public IRelayCommand ToggleMuteCommand { get; }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window.Dispatcher.CheckAccess())
        {
            Refresh();
            return;
        }

        _ = _window.Dispatcher.InvokeAsync(Refresh);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_window.Dispatcher.CheckAccess())
        {
            Refresh();
            return;
        }

        _ = _window.Dispatcher.InvokeAsync(Refresh);
    }

    private void Refresh()
    {
        _statusText = _service.StatusText;
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(ReconnectText));
        OnPropertyChanged(nameof(HideText));
        OnPropertyChanged(nameof(LogoffText));
        OnPropertyChanged(nameof(MuteText));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.StateChanged -= OnStateChanged;
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }
}
