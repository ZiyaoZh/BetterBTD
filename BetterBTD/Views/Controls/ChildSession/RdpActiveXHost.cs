using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using BetterBTD.Services.ChildSession;

namespace BetterBTD.Views.Controls.ChildSession;

internal sealed class RdpActiveXHost : AxHost
{
    private const string RdpClientClsid = "A0C63C30-F08D-4AB4-907C-34905D770C7D";
    private const short VariantFalse = 0;
    private const short VariantTrue = -1;
    private const int RedirectAudioToClient = 0;
    private const int DisableRemoteAudio = 2;

    private RdpConnectionPointCookie? _eventCookie;
    private RdpEventSink? _eventSink;
    private bool _connectionAttemptInProgress;
    private bool _disconnectRequested;
    private bool _connectionFailureReported;
    private bool _smartSizingEnabled = true;
    private bool _sendSystemShortcutsToRemote = true;
    private bool _audioMuted;
    private ChildSessionConnectionFailedEventArgs? _lastConnectionDiagnostic;

    public RdpActiveXHost()
        : base(RdpClientClsid)
    {
        Dock = DockStyle.Fill;
    }

    public event EventHandler? LoginCompleted;

    public event EventHandler<ChildSessionConnectionFailedEventArgs>? ConnectionFailed;

    public ChildSessionConnectionFailedEventArgs? LastConnectionDiagnostic => _lastConnectionDiagnostic;

    public bool IsAudioMuted => _audioMuted;

    public int ConnectedState
    {
        get
        {
            if (!IsHandleCreated)
            {
                return 0;
            }

            return Convert.ToInt32(GetComProperty(GetRequiredOcx(), "Connected"), CultureInfo.InvariantCulture);
        }
    }

    public void ConnectToChildSession(int width, int height)
    {
        if (ConnectedState != 0)
        {
            return;
        }

        var client = GetRequiredOcx();
        SetComProperty(client, "Server", "localhost");
        SetComProperty(client, "DesktopWidth", Math.Clamp(width, 200, 8192));
        SetComProperty(client, "DesktopHeight", Math.Clamp(height, 200, 8192));
        SetComProperty(client, "ColorDepth", 32);

        var securedSettings = GetComProperty(client, "SecuredSettings2")
            ?? throw new COMException("RDP ActiveX did not return SecuredSettings2.");
        SetComProperty(securedSettings, "KeyboardHookMode", _sendSystemShortcutsToRemote ? 1 : 0);
        ApplyAudioSettings(securedSettings);

        var advancedSettings = GetComProperty(client, "AdvancedSettings7")
            ?? throw new COMException("RDP ActiveX did not return AdvancedSettings7.");
        SetComProperty(advancedSettings, "RDPPort", 3389);
        SetComProperty(advancedSettings, "EnableCredSspSupport", true);
        SetComProperty(advancedSettings, "EnableWindowsKey", 1);
        SetComProperty(advancedSettings, "SmartSizing", _smartSizingEnabled);

        var extendedSettings = (IMsRdpExtendedSettings)client;
        object connectToChildSession = true;
        extendedSettings.SetProperty("ConnectToChildSession", ref connectToChildSession);

        _connectionAttemptInProgress = true;
        _connectionFailureReported = false;
        _lastConnectionDiagnostic = null;
        _disconnectRequested = false;
        InvokeComMethod(client, "Connect");
    }

    public void DisconnectSession()
    {
        if (ConnectedState == 0)
        {
            return;
        }

        _disconnectRequested = true;
        try
        {
            InvokeComMethod(GetRequiredOcx(), "Disconnect");
        }
        catch
        {
            _disconnectRequested = false;
            throw;
        }
    }

    public void SetSmartSizing(bool enabled)
    {
        _smartSizingEnabled = enabled;
        if (IsHandleCreated)
        {
            var advancedSettings = GetComProperty(GetRequiredOcx(), "AdvancedSettings7")
                ?? throw new COMException("RDP ActiveX did not return AdvancedSettings7.");
            SetComProperty(advancedSettings, "SmartSizing", enabled);
        }
    }

    public void SetSendSystemShortcutsToRemote(bool enabled)
    {
        _sendSystemShortcutsToRemote = enabled;
    }

    public void SetAudioMuted(bool muted)
    {
        _audioMuted = muted;
        if (IsHandleCreated && ConnectedState != 0)
        {
            var securedSettings = GetComProperty(GetRequiredOcx(), "SecuredSettings2")
                ?? throw new COMException("RDP ActiveX did not return SecuredSettings2.");
            ApplyAudioSettings(securedSettings);
        }
    }

    public void SendShowDesktopShortcut()
    {
        SendShortcut(
            [new RemoteKey(0x5B, true), new RemoteKey(0x20, false), new RemoteKey(0x20, false), new RemoteKey(0x5B, true)],
            [false, false, true, true]);
    }

    public void SendTaskViewShortcut()
    {
        SendShortcut(
            [new RemoteKey(0x5B, true), new RemoteKey(0x0F, false), new RemoteKey(0x0F, false), new RemoteKey(0x5B, true)],
            [false, false, true, true]);
    }

    protected override void CreateSink()
    {
        base.CreateSink();
        _eventSink = new RdpEventSink(this);
        _eventCookie = new RdpConnectionPointCookie(GetOcx(), _eventSink, typeof(IMsTscAxEvents));
    }

    protected override void DetachSink()
    {
        try
        {
            _eventCookie?.Disconnect();
            _eventCookie = null;
            _eventSink = null;
        }
        finally
        {
            base.DetachSink();
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _eventCookie?.Disconnect();
        _eventCookie = null;
        base.OnHandleDestroyed(e);
    }

    private void SendShortcut(IReadOnlyList<RemoteKey> keys, IReadOnlyList<bool> keyUps)
    {
        if (ConnectedState != 1)
        {
            throw new InvalidOperationException("The Child Session RDP connection is not ready.");
        }

        var keyUpStates = keyUps.Select(static isKeyUp => isKeyUp ? VariantTrue : VariantFalse).ToArray();
        var scanCodes = keys.Select(static key => key.ScanCode | (key.IsExtended ? 0x100 : 0)).ToArray();
        var client = (IMsRdpClientNonScriptable)GetRequiredOcx();
        client.SendKeys(keys.Count, ref keyUpStates[0], ref scanCodes[0]);
    }

    private void OnLoginComplete()
    {
        _connectionAttemptInProgress = false;
        _connectionFailureReported = false;
        _lastConnectionDiagnostic = null;
        LoginCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnDisconnected(int reason)
    {
        var wasConnecting = _connectionAttemptInProgress;
        _connectionAttemptInProgress = false;
        if (_disconnectRequested)
        {
            _disconnectRequested = false;
            return;
        }

        if (!wasConnecting && reason is 1 or 2 or 3)
        {
            return;
        }

        ReportFailure($"Child Session RDP disconnected (reason {reason}).", reason);
    }

    private void OnFatalError(int errorCode)
    {
        if (!_disconnectRequested)
        {
            ReportFailure($"Child Session RDP reported a fatal error ({errorCode}).", errorCode);
        }
    }

    private void OnLogonError(int errorCode)
    {
        if (!_disconnectRequested)
        {
            ReportFailure($"Child Session RDP logon failed ({errorCode}).", errorCode);
        }
    }

    private void ReportFailure(string message, int errorCode)
    {
        if (_connectionFailureReported)
        {
            return;
        }

        _connectionFailureReported = true;
        var diagnostic = new ChildSessionConnectionFailedEventArgs(message, errorCode);
        _lastConnectionDiagnostic = diagnostic;
        ConnectionFailed?.Invoke(this, diagnostic);
    }

    private object GetRequiredOcx()
    {
        if (!IsHandleCreated)
        {
            _ = Handle;
        }

        return GetOcx() ?? throw new InvalidOperationException("RDP ActiveX is not initialized.");
    }

    private static object? GetComProperty(object target, string name) => target.GetType().InvokeMember(
        name,
        BindingFlags.GetProperty,
        null,
        target,
        null,
        CultureInfo.InvariantCulture);

    private static void SetComProperty(object target, string name, object value) => target.GetType().InvokeMember(
        name,
        BindingFlags.SetProperty,
        null,
        target,
        [value],
        CultureInfo.InvariantCulture);

    private static object? InvokeComMethod(object target, string name, params object[]? args) => target.GetType().InvokeMember(
        name,
        BindingFlags.InvokeMethod,
        null,
        target,
        args,
        CultureInfo.InvariantCulture);

    private void ApplyAudioSettings(object securedSettings)
    {
        SetComProperty(
            securedSettings,
            "AudioRedirectionMode",
            _audioMuted ? DisableRemoteAudio : RedirectAudioToClient);
    }

    [ComImport]
    [Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    [TypeLibType(TypeLibTypeFlags.FDispatchable)]
    private interface IMsTscAxEvents
    {
        [DispId(1)] void OnConnecting();
        [DispId(2)] void OnConnected();
        [DispId(3)] void OnLoginComplete();
        [DispId(4)] void OnDisconnected([In] int disconnectReason);
        [DispId(5)] void OnEnterFullScreenMode();
        [DispId(6)] void OnLeaveFullScreenMode();
        [DispId(7)] void OnChannelReceivedData(
            [In, MarshalAs(UnmanagedType.BStr)] string channelName,
            [In, MarshalAs(UnmanagedType.BStr)] string data);
        [DispId(8)] void OnRequestGoFullScreen();
        [DispId(9)] void OnRequestLeaveFullScreen();
        [DispId(10)] void OnFatalError([In] int errorCode);
        [DispId(11)] void OnWarning([In] int warningCode);
        [DispId(12)] void OnRemoteDesktopSizeChange([In] int width, [In] int height);
        [DispId(13)] void OnIdleTimeoutNotification();
        [DispId(14)] void OnRequestContainerMinimize();
        [DispId(15)] void OnConfirmClose([Out] out bool allowClose);
        [DispId(16)] void OnReceivedTSPublicKey(
            [In, MarshalAs(UnmanagedType.BStr)] string publicKey,
            [Out] out bool continueLogon);
        [DispId(17)] void OnAutoReconnecting(
            [In] int disconnectReason,
            [In] int attemptCount,
            [Out] out AutoReconnectContinueState continueStatus);
        [DispId(18)] void OnAuthenticationWarningDisplayed();
        [DispId(19)] void OnAuthenticationWarningDismissed();
        [DispId(20)] void OnRemoteProgramResult(
            [In, MarshalAs(UnmanagedType.BStr)] string remoteProgram,
            [In] RemoteProgramResult error,
            [In] bool isExecutable);
        [DispId(21)] void OnRemoteProgramDisplayed(
            [In] bool displayed,
            [In] uint displayInformation);
        [DispId(22)] void OnLogonError([In] int errorCode);
        [DispId(23)] void OnFocusReleased([In] int direction);
        [DispId(24)] void OnUserNameAcquired(
            [In, MarshalAs(UnmanagedType.BStr)] string userName);
        [DispId(26)] void OnMouseInputModeChanged([In] bool isRelativeMouseMode);
        [DispId(28)] void OnServiceMessageReceived(
            [In, MarshalAs(UnmanagedType.BStr)] string serviceMessage);
        [DispId(29)] void OnRemoteWindowDisplayed(
            [In] bool displayed,
            [In] ref RemotableHandle windowHandle,
            [In] RemoteWindowDisplayedAttribute windowAttribute);
        [DispId(30)] void OnConnectionBarPullDown();
        [DispId(32)] void OnNetworkStatusChanged(
            [In] uint qualityLevel,
            [In] int bandwidth,
            [In] int roundTripTime);
        [DispId(33)] void OnAutoReconnected();
        [DispId(34)] void OnAutoReconnecting2(
            [In] int disconnectReason,
            [In] bool networkAvailable,
            [In] int attemptCount,
            [In] int maxAttemptCount);
        [DispId(35)] void OnDevicesButtonPressed();
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class RdpEventSink(RdpActiveXHost owner) : IMsTscAxEvents
    {
        public void OnConnecting()
        {
        }

        public void OnConnected()
        {
        }

        public void OnLoginComplete() => owner.OnLoginComplete();

        public void OnDisconnected(int disconnectReason) => owner.OnDisconnected(disconnectReason);

        public void OnEnterFullScreenMode()
        {
        }

        public void OnLeaveFullScreenMode()
        {
        }

        public void OnChannelReceivedData(string channelName, string data)
        {
        }

        public void OnRequestGoFullScreen()
        {
        }

        public void OnRequestLeaveFullScreen()
        {
        }

        public void OnFatalError(int errorCode) => owner.OnFatalError(errorCode);

        public void OnWarning(int warningCode)
        {
        }

        public void OnRemoteDesktopSizeChange(int width, int height)
        {
        }

        public void OnIdleTimeoutNotification()
        {
        }

        public void OnRequestContainerMinimize()
        {
        }

        public void OnConfirmClose(out bool allowClose) => allowClose = true;

        public void OnReceivedTSPublicKey(string publicKey, out bool continueLogon) => continueLogon = true;

        public void OnAutoReconnecting(
            int disconnectReason,
            int attemptCount,
            out AutoReconnectContinueState continueStatus) =>
            continueStatus = AutoReconnectContinueState.Automatic;

        public void OnAuthenticationWarningDisplayed()
        {
        }

        public void OnAuthenticationWarningDismissed()
        {
        }

        public void OnRemoteProgramResult(
            string remoteProgram,
            RemoteProgramResult error,
            bool isExecutable)
        {
        }

        public void OnRemoteProgramDisplayed(bool displayed, uint displayInformation)
        {
        }

        public void OnLogonError(int errorCode) => owner.OnLogonError(errorCode);

        public void OnFocusReleased(int direction)
        {
        }

        public void OnUserNameAcquired(string userName)
        {
        }

        public void OnMouseInputModeChanged(bool isRelativeMouseMode)
        {
        }

        public void OnServiceMessageReceived(string serviceMessage)
        {
        }

        public void OnRemoteWindowDisplayed(
            bool displayed,
            ref RemotableHandle windowHandle,
            RemoteWindowDisplayedAttribute windowAttribute)
        {
        }

        public void OnConnectionBarPullDown()
        {
        }

        public void OnNetworkStatusChanged(uint qualityLevel, int bandwidth, int roundTripTime)
        {
        }

        public void OnAutoReconnected() => owner.OnLoginComplete();

        public void OnAutoReconnecting2(
            int disconnectReason,
            bool networkAvailable,
            int attemptCount,
            int maxAttemptCount)
        {
        }

        public void OnDevicesButtonPressed()
        {
        }
    }

    private enum AutoReconnectContinueState
    {
        Automatic,
        Stop,
        Manual
    }

    private enum RemoteProgramResult
    {
        Ok,
        Locked,
        ProtocolError,
        NotInWhitelist,
        NetworkPathDenied,
        FileNotFound,
        Failure,
        HookNotLoaded
    }

    private enum RemoteWindowDisplayedAttribute
    {
        None,
        WindowDisplayed,
        ShellIconDisplayed
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RemotableHandle
    {
        internal int Context;
        internal RemotableHandleUnion Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RemotableHandleUnion
    {
        [FieldOffset(0)]
        internal int InProcessHandle;

        [FieldOffset(0)]
        internal int RemoteHandle;
    }

    [ComImport]
    [Guid("302D8188-0052-4807-806A-362B628F9AC5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpExtendedSettings
    {
        void SetProperty([In, MarshalAs(UnmanagedType.BStr)] string propertyName, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
        [return: MarshalAs(UnmanagedType.Struct)] object GetProperty([In, MarshalAs(UnmanagedType.BStr)] string propertyName);
    }

    [ComImport]
    [Guid("2F079C4C-87B2-4AFD-97AB-20CDB43038AE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpClientNonScriptable
    {
        void PutClearTextPassword([In, MarshalAs(UnmanagedType.BStr)] string value);
        void PutPortablePassword([In, MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetPortablePassword();
        void PutPortableSalt([In, MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetPortableSalt();
        void PutBinaryPassword([In, MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetBinaryPassword();
        void PutBinarySalt([In, MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetBinarySalt();
        void ResetPassword();
        void NotifyRedirectDeviceChange(nuint wParam, nint lParam);
        void SendKeys(int numKeys, [In] ref short keyUpStates, [In] ref int keyData);
    }

    private sealed class RdpConnectionPointCookie : IDisposable
    {
        private readonly IConnectionPoint _connectionPoint;
        private readonly int _cookie;
        private bool _disposed;

        public RdpConnectionPointCookie(object source, object sink, Type eventInterface)
        {
            var container = (IConnectionPointContainer)source;
            var guid = eventInterface.GUID;
            IConnectionPoint? connectionPoint = null;
            container.FindConnectionPoint(ref guid, out connectionPoint);
            _connectionPoint = connectionPoint
                ?? throw new COMException("RDP ActiveX event connection point is unavailable.");
            _connectionPoint.Advise(sink, out _cookie);
        }

        public void Disconnect()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _connectionPoint.Unadvise(_cookie);
            Marshal.ReleaseComObject(_connectionPoint);
        }

        public void Dispose() => Disconnect();
    }

    private readonly record struct RemoteKey(int ScanCode, bool IsExtended);
}
