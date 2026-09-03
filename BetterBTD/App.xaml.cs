using System.Windows;
using BetterBTD.Helpers;
using BetterBTD.Services;
using BetterBTD.Core.AutoTasks;
using BetterBTD.Core.GameControl;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Services.Tasks.RobotControl;
using BetterBTD.Services.Tasks.TestApi;
using BetterBTD.Services.Tools;
using BetterBTD.Services.ChildSession;
using Fischless.GameCapture.BitBlt;
using System.ComponentModel;
using System.Globalization;
using BetterBTD.Services.Start;
using BetterBTD.Models;
using BetterBTD.Services.Tasks.AutoTasks;

namespace BetterBTD
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TestApiRuntime? _testApiRuntime;
        private ChildSessionService? _childSessionService;

        public string[] LaunchArguments { get; set; } = [];

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var launchOptions = InstanceLaunchOptions.Parse(
                LaunchArguments.Length == 0 ? e.Args : LaunchArguments);
            ChildSessionRuntimeState.Initialize(launchOptions);
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            var config = ConfigurationService.Instance.Current;
            if (launchOptions.IsPrimary)
            {
                _ = GameUiDetectionConfigService.Instance.Current;
            }

            if (config.AutoFixWin11BitBlt && OsVersionHelper.IsWindows11_OrGreater)
            {
                BitBltRegistryHelper.SetDirectXUserGlobalSettings();
            }

            ThemeService.Instance.ApplyTheme(config.ThemeMode);

            if (!launchOptions.IsPrimary)
            {
                config.KeyboardMouseSimulationModeName =
                    KeyboardMouseSimulationModeExtensions.StandardConfigurationValue;
            }

            _childSessionService = new ChildSessionService(launchOptions);
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            if (!launchOptions.IsPrimary)
            {
                _ = StartChildSessionRuntimeAsync(launchOptions);
            }

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
                _childSessionService?.Dispose();
                base.OnExit(e);
            }
        }

        private async Task StartChildSessionRuntimeAsync(InstanceLaunchOptions launchOptions)
        {
            if (_childSessionService is null || launchOptions.RootSessionId is null)
            {
                return;
            }

            try
            {
                ConfigurationService.Instance.Current.KeyboardMouseSimulationModeName =
                    KeyboardMouseSimulationModeExtensions.StandardConfigurationValue;

                var controlConnected = await _childSessionService.ConnectChildControlAsync(
                    launchOptions.ControlPipeName,
                    CancellationToken.None);
                if (!controlConnected)
                {
                    Application.Current.Shutdown();
                    return;
                }

                var launchResult = await GameLaunchService.Instance.EnsureGameStartedAsync(
                    ConfigurationService.Instance.Current.GameInstallPath,
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromMilliseconds(500));
                if (!launchResult.Success)
                {
                    _childSessionService.RefreshState(string.Format(
                        CultureInfo.InvariantCulture,
                        LocalizationService.Instance.T("ChildSession.Status.Btd6LaunchFailed"),
                        launchResult.Message));
                    return;
                }

                var captureService = GameCaptureService.Instance;
                captureService.Configure(new GameCaptureOptions
                {
                    CaptureModeName = ConfigurationService.Instance.Current.CaptureModeName,
                    CaptureIntervalMs = ConfigurationService.Instance.Current.CaptureIntervalMs,
                    AutoFixWin11BitBlt = ConfigurationService.Instance.Current.AutoFixWin11BitBlt
                });
                if (!captureService.TryStart(out _))
                {
                    _childSessionService.RefreshState(
                        LocalizationService.Instance.T("ChildSession.Status.CaptureUnavailable"));
                    return;
                }

                _childSessionService.RefreshState(
                    LocalizationService.Instance.T("ChildSession.Status.ChildReady"));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception)
            {
                _childSessionService.RefreshState(string.Format(
                    CultureInfo.InvariantCulture,
                    LocalizationService.Instance.T("ChildSession.Status.StartupFailed"),
                    ex.GetBaseException().Message));
            }
        }
    }
}
