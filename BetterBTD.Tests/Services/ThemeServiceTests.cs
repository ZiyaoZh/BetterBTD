using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.Views.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Xunit;
using Border = System.Windows.Controls.Border;
using TextBlock = System.Windows.Controls.TextBlock;

namespace BetterBTD.Tests.Services;

public sealed class ThemeServiceTests
{
    [Fact]
    public void SharedTheme_StartupSwitchingAndModalWindowsKeepOnePalette()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                // Match App.xaml's root composition; library dictionaries stay at application scope.
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = ApplicationTheme.Dark });
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                application.Resources.MergedDictionaries.Add(new Wpf.Ui.Violeta.Markup.ControlsDictionary());
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/BetterBTD;component/Views/Theming/ApplicationStyles.xaml")
                });
                var service = ThemeService.Instance;
                service.ApplyTheme("Light");
                var surface = new Border();
                surface.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
                var text = new TextBlock();
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                surface.Child = text;
                var mainWindow = new FluentWindow
                {
                    Content = surface, Width = 920, Height = 591,
                    Opacity = 0, ShowActivated = false, ShowInTaskbar = false
                };
                application.MainWindow = mainWindow;
                mainWindow.Show();

                foreach (var themeCode in new string?[] { "Light", "Dark", "light", null })
                {
                    service.ApplyTheme(themeCode);
                    var theme = themeCode?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true ? "Light" : "Dark";
                    PumpDispatcher();
                    Assert.Equal(theme, service.CurrentTheme);
                    Assert.Equal(theme, ApplicationThemeManager.GetAppTheme().ToString());
                    Assert.Equal(theme == "Light" ? Color.FromRgb(250, 250, 250) : Color.FromRgb(32, 32, 32),
                        ((SolidColorBrush)surface.Background).Color);
                    Assert.Equal(((SolidColorBrush)application.FindResource("TextFillColorPrimaryBrush")).Color,
                        ((SolidColorBrush)text.Foreground).Color);
                    Assert.Equal(((SolidColorBrush)application.FindResource("ApplicationBackgroundBrush")).Color,
                        ((SolidColorBrush)mainWindow.Background).Color);
                    Assert.Equal(byte.MaxValue, ((SolidColorBrush)mainWindow.Background).Color.A);
                    Assert.Equal(WindowBackdropType.None, mainWindow.WindowBackdropType);

                    var themeDictionary = application.Resources.MergedDictionaries[0];
                    service.ApplyTheme(theme);
                    Assert.Same(themeDictionary, application.Resources.MergedDictionaries[0]);

                    foreach (var language in new[] { "zh-CN", "en-US" })
                    {
                        LocalizationService.Instance.SetLanguage(language);
                        mainWindow.Width = language == "zh-CN" ? 760 : 1100;
                        mainWindow.Height = language == "zh-CN" ? 488 : 800;
                        VerifyPopup(() => new AppDialogWindow(new AppDialogRequest
                        {
                            Message = "Theme test", PrimaryButtonText = "Close"
                        }), language == "zh-CN" ? 760 : 860);
                        VerifyPopup(() => new KeyBindingsWindow(), language == "zh-CN" ? 760 : 1100);
                    }
                }

                void VerifyPopup(Func<FluentWindow> createPopup, double width)
                {
                    Assert.NotNull(application.TryFindResource("TreeViewItemChevronSize"));
                    string[] keys = ["ApplicationBackgroundBrush", "TextFillColorPrimaryBrush",
                        "TextFillColorTertiaryBrush", "ControlFillColorSecondaryBrush", "AccentFillColorDefaultBrush"];
                    var palette = keys.ToDictionary(key => key,
                        key => ((SolidColorBrush)application.FindResource(key)).Color);
                    var themeDictionary = application.Resources.MergedDictionaries[0];
                    var mainBackground = mainWindow.Background;
                    var popup = createPopup();
                    try
                    {
                        popup.Width = width;
                        popup.Owner = mainWindow;
                        popup.ShowActivated = false;
                        popup.ShowInTaskbar = false;
                        popup.Opacity = 0;
                        // ShowDialog exercises owner disable/enable and the real Loaded/template lifecycle.
                        Brush? openBackground = null;
                        Brush? ownerBackground = null;
                        popup.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                        {
                            openBackground = popup.Background;
                            ownerBackground = mainWindow.Background;
                            popup.Close();
                        }));
                        popup.ShowDialog();
                        PumpDispatcher();
                        Assert.Equal(palette["ApplicationBackgroundBrush"], Assert.IsType<SolidColorBrush>(openBackground).Color);
                        Assert.Same(mainBackground, ownerBackground);
                        Assert.Equal(WindowBackdropType.None, popup.WindowBackdropType);
                        Assert.Equal(palette["ApplicationBackgroundBrush"], ((SolidColorBrush)popup.Background).Color);
                        Assert.Same(mainBackground, mainWindow.Background);
                        Assert.Same(themeDictionary, application.Resources.MergedDictionaries[0]);
                        foreach (var key in keys)
                        {
                            Assert.Equal(palette[key], ((SolidColorBrush)application.FindResource(key)).Color);
                            Assert.Equal(palette[key], ((SolidColorBrush)popup.FindResource(key)).Color);
                        }
                    }
                    finally
                    {
                        popup.Close();
                    }
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Theme verification timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
