using System.Windows;
using BetterBTD.Views.Windows;

namespace BetterBTD.Services;

public enum AppDialogResult
{
    Primary,
    Secondary,
    Close
}

public sealed class AppDialogRequest
{
    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string PrimaryButtonText { get; init; } = string.Empty;

    public string? SecondaryButtonText { get; init; }

    public string? CloseButtonText { get; init; }
}

public sealed class AppDialogService
{
    private static readonly Lazy<AppDialogService> InstanceHolder = new(() => new AppDialogService());

    private AppDialogService()
    {
    }

    public static AppDialogService Instance => InstanceHolder.Value;

    public AppDialogResult Show(AppDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new AppDialogWindow(request);
        var owner = GetActiveWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        _ = dialog.ShowDialog();
        return dialog.Result;
    }

    private static Window? GetActiveWindow()
    {
        var activeWindow = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);
        return activeWindow ?? Application.Current?.MainWindow;
    }
}
