using System.Windows;
using BetterBTD.Helpers;
using BetterBTD.Services;
using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.GameControl;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Services.Tasks.RobotControl;
using BetterBTD.Services.Tasks.TestApi;
using BetterBTD.Services.Tools;
using Fischless.GameCapture.BitBlt;

namespace BetterBTD
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TestApiRuntime? _testApiRuntime;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var config = ConfigurationService.Instance.Current;
            if (config.AutoFixWin11BitBlt && OsVersionHelper.IsWindows11_OrGreater)
            {
                BitBltRegistryHelper.SetDirectXUserGlobalSettings();
            }

            ThemeService.Instance.ApplyTheme(config.ThemeMode);

            var testApiOptions = TestApiLaunchOptions.Parse(e.Args);
            if (testApiOptions.Enabled)
            {
                _testApiRuntime = new TestApiRuntime();
                _testApiRuntime.StartAsync(testApiOptions).GetAwaiter().GetResult();
            }

            Activated += (_, _) => ThemeService.Instance.ApplyTheme(ThemeService.Instance.CurrentTheme);
            Deactivated += (_, _) => ThemeService.Instance.ApplyTheme(ThemeService.Instance.CurrentTheme);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AutoTaskCoordinator.Instance.RequestStop();
            ScriptTaskFlowExecutor.Instance.RequestStop();
            try
            {
                try
                {
                    _testApiRuntime?.StopAsync(stopOwnedCapture: false).GetAwaiter().GetResult();
                }
                finally
                {
                    try
                    {
                        RobotTaskRuntime.Instance.StopAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        try
                        {
                            GameControlLeaseCoordinator.Instance.WaitForIdleAsync().GetAwaiter().GetResult();
                        }
                        finally
                        {
                            _testApiRuntime?.StopOwnedCapture();
                        }
                    }
                }
            }
            finally
            {
                GameCaptureService.Instance.Shutdown();
                MaskWindowService.Instance.Shutdown();
                PlacementAssistService.Instance.Shutdown();
                HardwareInputSimulationService.Instance.Shutdown();
                base.OnExit(e);
            }
        }
    }
}
