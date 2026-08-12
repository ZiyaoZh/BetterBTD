using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BetterBTD.Services.Start;
using BetterBTD.Views.Controls.ChildSession;
using BetterBTD.Views.Windows;

namespace BetterBTD.Services.ChildSession;

internal sealed class ChildSessionService : IDisposable
{
    private const int DefaultDesktopWidth = 1920;
    private const int DefaultDesktopHeight = 1080;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _statusTimer;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly InstanceLaunchOptions _launchOptions;
    private ChildSessionControlServer? _controlServer;
    private ChildSessionControlClient? _controlClient;
    private ChildSessionWindow? _window;
    private TaskCompletionSource<bool>? _connectionCompletionSource;
    private bool _disposed;
    private string _lastOperation = string.Empty;

    public static ChildSessionService? Current { get; private set; }

    public ChildSessionService(InstanceLaunchOptions launchOptions)
    {
        if (Current is not null)
        {
            throw new InvalidOperationException("Only one Child Session service can exist per BetterBTD process.");
        }

        _launchOptions = launchOptions ?? throw new ArgumentNullException(nameof(launchOptions));
        Current = this;
        RdpHost = new RdpActiveXHost();
        RdpHost.LoginCompleted += OnLoginCompleted;
        RdpHost.ConnectionFailed += OnConnectionFailed;

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        RefreshState();
        if (IsPrimary && ChildSessionNativeMethods.TryGetChildSessionId() is not null)
        {
            ChildSessionRuntimeState.SetPrimaryControlBlocked(true);
        }
    }

    public event EventHandler? StateChanged;

    public event EventHandler<ChildSessionConnectionFailedEventArgs>? ConnectionFailed;

    internal RdpActiveXHost RdpHost { get; }

    public string StatusText { get; private set; } = Translate("ChildSession.Status.NotConnected");

    public bool IsPrimary => _launchOptions.IsPrimary;

    public bool IsChildSession => !IsPrimary;

    public bool IsConnected => RdpHost.ConnectedState == 1;

    public bool IsVisible => _window?.IsVisible == true;

    public bool IsAudioMuted => RdpHost.IsAudioMuted;

    public bool IsDesktopCloneAvailable
    {
        get
        {
            try
            {
                return IsPrimary && ChildSessionNativeMethods.IsChildSessionsEnabled();
            }
            catch (Exception ex) when (ex is Win32Exception or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsPrimary)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            ChildSessionNativeMethods.EnableChildSessions();
            EnsureWindow();
            ShowWindow();
            var childSessionId = ChildSessionNativeMethods.TryGetChildSessionId();
            if (RdpHost.ConnectedState == 1)
            {
                RefreshState(Translate("ChildSession.Status.Connected"));
                _ = LaunchChildInstanceAsync();
                return;
            }

            var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectionCompletionSource = completionSource;
            _lastOperation = childSessionId is null
                ? Translate("ChildSession.Status.Starting")
                : Translate("ChildSession.Status.Reconnecting");
            RdpHost.ConnectToChildSession(DefaultDesktopWidth, DefaultDesktopHeight);
            RefreshState();

            try
            {
                await completionSource.Task.WaitAsync(ConnectionTimeout, cancellationToken).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                CompleteConnectionFailure(new ChildSessionConnectionFailedEventArgs(
                    Translate("ChildSession.Status.ConnectionTimeout"),
                    1460));
            }
            finally
            {
                if (ReferenceEquals(_connectionCompletionSource, completionSource))
                {
                    _connectionCompletionSource = null;
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> ConnectChildControlAsync(
        string? pipeName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsPrimary)
        {
            return false;
        }

        _controlClient = await ChildSessionControlClient.ConnectAsync(pipeName, cancellationToken)
            .ConfigureAwait(true);
        if (_controlClient is null)
        {
            RefreshState(Translate("ChildSession.Status.PrimaryUnavailable"));
            return false;
        }

        await _controlClient.SendAsync("ready", cancellationToken).ConfigureAwait(true);
        RefreshState(Translate("ChildSession.Status.ChildReady"));
        return true;
    }

    public void ShowWindow()
    {
        ThrowIfDisposed();
        EnsureWindow();
        if (!_window!.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        RefreshState();
    }

    public void HideWindow()
    {
        ThrowIfDisposed();
        _window?.Hide();
        RefreshState(Translate("ChildSession.Status.Hidden"));
    }

    public void ToggleAudioMute()
    {
        ThrowIfDisposed();
        RdpHost.SetAudioMuted(!RdpHost.IsAudioMuted);
        RefreshState(Translate(RdpHost.IsAudioMuted
            ? "ChildSession.Status.AudioMuted"
            : "ChildSession.Status.AudioEnabled"));
    }

    public async Task LogoffAndHideAsync()
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync().ConfigureAwait(true);
        try
        {
            try
            {
                RdpHost.DisconnectSession();
            }
            catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException)
            {
                _lastOperation = ex.GetBaseException().Message;
            }

            var sessionId = await Task.Run(() => ChildSessionNativeMethods.LogoffChildSession()).ConfigureAwait(true);
            _window?.Hide();
            RefreshState(sessionId is null
                ? Translate("ChildSession.Status.NoActiveSession")
                : Translate("ChildSession.Status.LoggedOff", sessionId.Value));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void RefreshState(string? operation = null)
    {
        if (_disposed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(operation))
        {
            _lastOperation = operation;
        }

        try
        {
            var enabled = ChildSessionNativeMethods.IsChildSessionsEnabled();
            var sessionId = ChildSessionNativeMethods.TryGetChildSessionId();
            var connection = RdpHost.ConnectedState switch
            {
                0 => "Disconnected",
                1 => "Connected",
                2 => "Connecting",
                _ => $"State {RdpHost.ConnectedState}"
            };
            StatusText = Translate(
                "ChildSession.Status.Summary",
                _lastOperation.Length == 0 ? connection : _lastOperation,
                connection,
                sessionId?.ToString() ?? Translate("ChildSession.Status.None"),
                enabled ? Translate("ChildSession.Status.Yes") : Translate("ChildSession.Status.No"));
        }
        catch (Exception ex) when (ex is Win32Exception or EntryPointNotFoundException or InvalidOperationException)
        {
            StatusText = ex.GetBaseException().Message;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new ChildSessionWindow(this);
        _window.Closed += (_, _) => _window = null;
        RdpHost.SetSmartSizing(true);
    }

    private void OnLoginCompleted(object? sender, EventArgs e)
    {
        _lastOperation = Translate("ChildSession.Status.LoginCompleted");
        _connectionCompletionSource?.TrySetResult(true);
        RefreshState();
        _ = LaunchChildInstanceAsync();
    }

    private async Task LaunchChildInstanceAsync()
    {
        var sessionId = ChildSessionNativeMethods.TryGetChildSessionId();
        if (sessionId is null || _controlServer is not null)
        {
            return;
        }

        _controlServer = ChildSessionControlServer.Create();
        _controlServer.MessageReceived += OnControlMessageReceived;
        _controlServer.ConnectionClosed += OnControlConnectionClosed;
        _ = _controlServer.StartAsync();
        try
        {
            await ChildSessionProcessLauncher.LaunchAsync(
                sessionId.Value,
                ChildSessionNativeMethods.GetCurrentSessionId(),
                _controlServer.PipeName).ConfigureAwait(true);
            _lastOperation = Translate("ChildSession.Status.ChildLaunched", sessionId.Value);
            RefreshState();
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException or IOException)
        {
            _lastOperation = Translate("ChildSession.Status.ChildLaunchFailed", ex.GetBaseException().Message);
            RefreshState();
        }
    }

    private void OnControlMessageReceived(object? sender, string message)
    {
        RunOnUiThread(() => HandleControlMessage(message));
    }

    private void HandleControlMessage(string message)
    {
        if (message.StartsWith("ready", StringComparison.OrdinalIgnoreCase))
        {
            ChildSessionRuntimeState.SetPrimaryControlBlocked(true);
            RefreshState(Translate("ChildSession.Status.PrimaryBlocked"));
        }
        else if (message.StartsWith("exit", StringComparison.OrdinalIgnoreCase))
        {
            ChildSessionRuntimeState.SetPrimaryControlBlocked(
                ChildSessionNativeMethods.TryGetChildSessionId() is not null);
            RefreshState(Translate("ChildSession.Status.ChildExited"));
        }
    }

    private void OnControlConnectionClosed(object? sender, EventArgs e)
    {
        RunOnUiThread(() => HandleControlConnectionClosed(sender));
    }

    private void HandleControlConnectionClosed(object? sender)
    {
        if (ReferenceEquals(sender, _controlServer))
        {
            _controlServer?.Dispose();
            _controlServer = null;
        }

        ChildSessionRuntimeState.SetPrimaryControlBlocked(
            ChildSessionNativeMethods.TryGetChildSessionId() is not null);
        RefreshState(Translate("ChildSession.Status.ChildDisconnected"));
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }

    private void OnConnectionFailed(object? sender, ChildSessionConnectionFailedEventArgs e)
    {
        CompleteConnectionFailure(e);
        ConnectionFailed?.Invoke(this, e);
    }

    private void CompleteConnectionFailure(ChildSessionConnectionFailedEventArgs failure)
    {
        _lastOperation = failure.Message;
        _connectionCompletionSource?.TrySetResult(false);
        RefreshState();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        RefreshState();
        if (IsPrimary)
        {
            ChildSessionRuntimeState.SetPrimaryControlBlocked(
                ChildSessionNativeMethods.TryGetChildSessionId() is not null);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _connectionCompletionSource?.TrySetCanceled();
        if (_controlServer is not null)
        {
            _controlServer.MessageReceived -= OnControlMessageReceived;
            _controlServer.ConnectionClosed -= OnControlConnectionClosed;
        }
        _controlServer?.Dispose();
        _controlServer = null;
        if (IsChildSession && _controlClient is not null)
        {
            try
            {
                _controlClient.SendAsync("exit").GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }
        _controlClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _controlClient = null;
        RdpHost.LoginCompleted -= OnLoginCompleted;
        RdpHost.ConnectionFailed -= OnConnectionFailed;
        try
        {
            RdpHost.DisconnectSession();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }

        if (IsPrimary)
        {
            try
            {
                ChildSessionNativeMethods.LogoffChildSession(wait: false);
            }
            catch (Exception ex) when (ex is Win32Exception or EntryPointNotFoundException)
            {
            }
        }

        if (_window is not null)
        {
            _window.AllowClose();
            _window.Close();
            _window = null;
        }

        RdpHost.Dispose();
        _lifecycleGate.Dispose();
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string Translate(string key, params object[] arguments)
    {
        var text = LocalizationService.Instance.T(key);
        return arguments.Length == 0
            ? text
            : string.Format(CultureInfo.InvariantCulture, text, arguments);
    }
}
