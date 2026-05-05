using System.Windows;
using BetterBTD.Services;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public partial class AppDialogWindow : FluentWindow
{
    public AppDialogResult Result { get; private set; } = AppDialogResult.Close;

    public AppDialogWindow(AppDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InitializeComponent();

        Title = request.Title;
        DialogTitleBar.Title = request.Title;
        MessageTextBlock.Text = request.Message;

        PrimaryButtonControl.Content = request.PrimaryButtonText;
        SecondaryButtonControl.Content = request.SecondaryButtonText;
        CloseButtonControl.Content = request.CloseButtonText;

        SecondaryButtonControl.Visibility = string.IsNullOrWhiteSpace(request.SecondaryButtonText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        CloseButtonControl.Visibility = string.IsNullOrWhiteSpace(request.CloseButtonText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PrimaryButtonControl_OnClick(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Primary;
        DialogResult = true;
        Close();
    }

    private void SecondaryButtonControl_OnClick(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Secondary;
        DialogResult = false;
        Close();
    }

    private void CloseButtonControl_OnClick(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Close;
        DialogResult = false;
        Close();
    }
}
