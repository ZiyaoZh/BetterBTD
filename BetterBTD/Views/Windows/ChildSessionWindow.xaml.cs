using System.ComponentModel;
using System.Windows;
using BetterBTD.Services.ChildSession;
using BetterBTD.ViewModels;

namespace BetterBTD.Views.Windows;

public partial class ChildSessionWindow : Window
{
    private readonly ChildSessionWindowViewModel _viewModel;
    private bool _allowClose;

    internal ChildSessionWindow(ChildSessionService service)
    {
        InitializeComponent();
        DataContext = _viewModel = new ChildSessionWindowViewModel(service, this);
        RdpHostElement.Child = service.RdpHost;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || Application.Current?.Dispatcher.HasShutdownStarted == true)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        RdpHostElement.Child = null;
        _viewModel.Dispose();
    }
}
