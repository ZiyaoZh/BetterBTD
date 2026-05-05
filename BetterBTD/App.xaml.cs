using System.Windows;
using BetterBTD.Services;

namespace BetterBTD
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var config = ConfigurationService.Instance.Current;
            ThemeService.Instance.ApplyTheme(config.ThemeMode);

            Activated += (_, _) => ThemeService.Instance.ApplyTheme(ThemeService.Instance.CurrentTheme);
            Deactivated += (_, _) => ThemeService.Instance.ApplyTheme(ThemeService.Instance.CurrentTheme);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            MaskWindowService.Instance.Shutdown();
            base.OnExit(e);
        }
    }
}
