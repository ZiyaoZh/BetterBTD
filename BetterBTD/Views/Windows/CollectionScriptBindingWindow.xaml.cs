using System.ComponentModel;
using BetterBTD.ViewModels;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public partial class CollectionScriptBindingWindow : FluentWindow
{
    public CollectionScriptBindingWindow(CollectionScriptBindingWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        Activated += OnActivated;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is CollectionScriptBindingWindowViewModel viewModel)
        {
            viewModel.RefreshWritableState();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is CollectionScriptBindingWindowViewModel viewModel && !viewModel.ConfirmClose())
        {
            e.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closing -= OnClosing;
        Closed -= OnClosed;
        Activated -= OnActivated;
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
