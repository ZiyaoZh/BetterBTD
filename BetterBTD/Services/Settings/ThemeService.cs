using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BetterBTD.Services.Settings;

public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> InstanceHolder = new(() => new ThemeService());
    private bool _hasAppliedTheme;

    private ThemeService()
    {
        EventManager.RegisterClassHandler(typeof(FluentWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    public static ThemeService Instance => InstanceHolder.Value;

    public string CurrentTheme { get; private set; } = "Dark";

    public void ApplyTheme(string? themeCode)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => ApplyTheme(themeCode));
            return;
        }

        var theme = string.Equals(themeCode, "Light", StringComparison.OrdinalIgnoreCase)
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;
        var themeName = theme.ToString();
        if (_hasAppliedTheme && CurrentTheme == themeName && ApplicationThemeManager.GetAppTheme() == theme)
        {
            return;
        }

        CurrentTheme = themeName;
        // WPF UI owns the only palette; window creation must not replace it.
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None);
        _hasAppliedTheme = true;

        foreach (var window in application.Windows.OfType<FluentWindow>().Where(window => window.IsLoaded))
        {
            ApplyWindowAppearance(window);
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FluentWindow window && ReferenceEquals(sender, e.OriginalSource))
        {
            ApplyWindowAppearance(window);
        }
    }

    private static void ApplyWindowAppearance(FluentWindow window)
    {
        // Synchronize native title-bar colors without reloading application resources.
        WindowBackgroundManager.UpdateBackground(window, ApplicationThemeManager.GetAppTheme(), WindowBackdropType.None);
    }
}
