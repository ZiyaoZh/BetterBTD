using System.ComponentModel;
using BetterBTD.Services.Shared;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public partial class ImportProgressWindow : FluentWindow
{
    private bool _allowClose;

    public ImportProgressWindow(ImportProgressDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InitializeComponent();

        Title = request.Title;
        DialogTitleBar.Title = request.Title;
        MessageTextBlock.Text = request.Message;
        UpdateProgress(request.ProgressValue, request.ProgressMaximum, request.IsIndeterminate);
    }

    public void UpdateMessage(string message)
    {
        MessageTextBlock.Text = message ?? string.Empty;
    }

    public void UpdateProgress(double value, double maximum, bool isIndeterminate)
    {
        DialogProgressBar.IsIndeterminate = isIndeterminate;
        DialogProgressBar.Maximum = maximum <= 0d ? 1d : maximum;
        DialogProgressBar.Value = Math.Clamp(value, 0d, DialogProgressBar.Maximum);
    }

    public void CloseDialog()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
