using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace BetterBTD.Tests.Services;

public sealed class ThemeServiceTests
{
    [Fact]
    public void ApplyTheme_InitialPaletteAndExistingControlsFollowThemeWithoutInteraction()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var application = new Application();
            try
            {
                application.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
                // App-level overrides take precedence over WPF UI's merged theme dictionary.
                application.Resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(243, 246, 251));
                application.Resources["ApplicationPageBackgroundThemeBrush"] = new SolidColorBrush(Color.FromRgb(13, 17, 27));
                var service = ThemeService.Instance;
                service.ApplyTheme("Light");

                // Create controls after startup theme selection, just as the main window is created.
                var surface = new Border();
                surface.SetResourceReference(Border.BackgroundProperty, "ApplicationPageBackgroundThemeBrush");
                var text = new TextBlock();
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                surface.Child = text;
                application.MainWindow = new Window { Content = surface };

                AssertPalette("Light", "#F7F9FC", "#111827", "#EEF2F8");
                service.ApplyTheme("Dark");
                AssertPalette("Dark", "#0D111B", "#F3F6FB", "#1B2536");
                service.ApplyTheme("light");
                AssertPalette("Light", "#F7F9FC", "#111827", "#EEF2F8");
                service.ApplyTheme(null);
                AssertPalette("Dark", "#0D111B", "#F3F6FB", "#1B2536");

                void AssertPalette(string theme, string background, string foreground, string controlFill)
                {
                    application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    surface.Measure(new Size(300, 200));
                    surface.Arrange(new Rect(0, 0, 300, 200));
                    surface.UpdateLayout();
                    Assert.Equal(theme, service.CurrentTheme);
                    Assert.Equal(theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark,
                        ApplicationThemeManager.GetAppTheme());
                    Assert.Equal(ColorConverter.ConvertFromString(background), ((SolidColorBrush)surface.Background).Color);
                    Assert.Equal(ColorConverter.ConvertFromString(foreground), ((SolidColorBrush)text.Foreground).Color);
                    Assert.Equal(ColorConverter.ConvertFromString(controlFill),
                        ((SolidColorBrush)application.FindResource("ControlFillColorSecondaryBrush")).Color);
                    Assert.Equal(ColorConverter.ConvertFromString(foreground),
                        ((SolidColorBrush)application.FindResource("TextPrimaryBrush")).Color);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                application.Shutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Theme resource verification timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
