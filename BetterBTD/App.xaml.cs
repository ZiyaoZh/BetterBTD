using System.Windows;
using BetterBTD.Helpers;
using BetterBTD.Services;
using Fischless.GameCapture.BitBlt;

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
            if (config.AutoFixWin11BitBlt && OsVersionHelper.IsWindows11_OrGreater)
            {
                BitBltRegistryHelper.SetDirectXUserGlobalSettings();
            }

            ThemeService.Instance.ApplyTheme(config.ThemeMode);

            Activated += (_, _) => ThemeService.Instance.ApplyTheme(ThemeService.Instance.CurrentTheme);
            Deactivated += (_, _) => ThemeService.Instance.ApplyTheme(ThemeService.Instance.CurrentTheme);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            GameCaptureService.Instance.Shutdown();
            MaskWindowService.Instance.Shutdown();
            base.OnExit(e);
        }
    }
}
