using System.Windows;
using BetterBTD.Views.Windows;

namespace BetterBTD.Services.Shared;

public sealed class ImportProgressDialogRequest
{
    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public double ProgressValue { get; init; }

    public double ProgressMaximum { get; init; }

    public bool IsIndeterminate { get; init; } = true;
}

public sealed class ImportProgressDialogHandle : IDisposable
{
    private readonly ImportProgressWindow _window;
    private bool _isClosed;

    internal ImportProgressDialogHandle(ImportProgressWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public void UpdateMessage(string message)
    {
        if (_isClosed)
        {
            return;
        }

        if (_window.Dispatcher.CheckAccess())
        {
            _window.UpdateMessage(message);
            return;
        }

        _ = _window.Dispatcher.InvokeAsync(() => _window.UpdateMessage(message));
    }

    public void UpdateProgress(double value, double maximum, bool isIndeterminate = false)
    {
        if (_isClosed)
        {
            return;
        }

        if (_window.Dispatcher.CheckAccess())
        {
            _window.UpdateProgress(value, maximum, isIndeterminate);
            return;
        }

        _ = _window.Dispatcher.InvokeAsync(() => _window.UpdateProgress(value, maximum, isIndeterminate));
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;

        if (_window.Dispatcher.CheckAccess())
        {
            _window.CloseDialog();
            return;
        }

        _ = _window.Dispatcher.InvokeAsync(_window.CloseDialog);
    }

    public void Dispose()
    {
        Close();
    }
}

public sealed class ImportProgressDialogService
{
    private static readonly Lazy<ImportProgressDialogService> InstanceHolder =
        new(() => new ImportProgressDialogService());

    private ImportProgressDialogService()
    {
    }

    public static ImportProgressDialogService Instance => InstanceHolder.Value;

    public ImportProgressDialogHandle Show(ImportProgressDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new ImportProgressWindow(request);
        var owner = GetActiveWindow();
        if (owner is not null)
        {
            dialog.Owner = owner;
            owner.IsEnabled = false;
            dialog.Closed += (_, _) =>
            {
                owner.IsEnabled = true;
                if (owner.IsVisible)
                {
                    owner.Activate();
                }
            };
        }

        dialog.Show();
        dialog.Activate();
        return new ImportProgressDialogHandle(dialog);
    }

    private static Window? GetActiveWindow()
    {
        var activeWindow = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);
        return activeWindow ?? Application.Current?.MainWindow;
    }
}
