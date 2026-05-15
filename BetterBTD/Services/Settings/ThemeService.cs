using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace BetterBTD.Services.Settings;

public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> InstanceHolder = new(() => new ThemeService());

    private ThemeService()
    {
    }

    public static ThemeService Instance => InstanceHolder.Value;

    public string CurrentTheme { get; private set; } = "Dark";

    public void ApplyTheme(string? themeCode)
    {
        var theme = string.Equals(themeCode, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        CurrentTheme = theme;

        ApplicationThemeManager.Apply(theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark);

        if (Application.Current?.Resources is null)
        {
            return;
        }

        if (theme == "Light")
        {
            SetSolidColor("PanelBrush", "#F7F9FC");
            SetSolidColor("CardBrush", "#FFFFFF");
            SetSolidColor("SubtleSurfaceBrush", "#EEF2F8");
            SetSolidColor("TextPrimaryBrush", "#111827");
            SetSolidColor("TextSecondaryBrush", "#4B5563");
            SetGradient("WindowBgBrush", "#EFF3FA", "#E6ECF7", "#F7FAFF");
        }
        else
        {
            SetSolidColor("PanelBrush", "#151D2B");
            SetSolidColor("CardBrush", "#1B2536");
            SetSolidColor("SubtleSurfaceBrush", "#243247");
            SetSolidColor("TextPrimaryBrush", "#F3F6FB");
            SetSolidColor("TextSecondaryBrush", "#A7B3C5");
            SetGradient("WindowBgBrush", "#0D111B", "#101725", "#0B111D");
        }
    }

    private static void SetSolidColor(string key, string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static void SetGradient(string key, string c1, string c2, string c3)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };

        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c1), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c2), 0.55));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c3), 1));

        Application.Current.Resources[key] = brush;
    }
}

