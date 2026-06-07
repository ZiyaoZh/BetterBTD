using BetterBTD.ViewModels;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public partial class Btd6SaveViewerWindow : FluentWindow
{
    public Btd6SaveViewerWindow(Btd6SaveViewerWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnJsonTreeSelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is Btd6SaveViewerWindowViewModel viewModel)
        {
            viewModel.SetSelectedNode(e.NewValue as Btd6SaveJsonNodeViewModel);
        }
    }
}
