using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BetterBTD.Services.ChildSession;

internal static class ChildSessionNativeMethods
{
    private const uint NoChildSessionId = uint.MaxValue;
    private const int ErrorNotFound = 1168;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnableChildSessions([MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSIsChildSessionsEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSGetChildSessionId(out uint sessionId);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(
        nint serverHandle,
        uint sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    public static void EnableChildSessions()
    {
        if (!WTSEnableChildSessions(true))
        {
            throw CreateLastWin32Exception("Unable to enable Windows Child Sessions.");
        }
    }

    public static bool IsChildSessionsEnabled()
    {
        if (!WTSIsChildSessionsEnabled(out var enabled))
        {
            throw CreateLastWin32Exception("Unable to query Windows Child Session support.");
        }

        return enabled;
    }

    public static uint? TryGetChildSessionId()
    {
        return WTSGetChildSessionId(out var sessionId) && sessionId != NoChildSessionId
            ? sessionId
            : null;
    }

    public static uint GetCurrentSessionId()
    {
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var sessionId))
        {
            throw CreateLastWin32Exception("Unable to query the current Windows session.");
        }

        return sessionId;
    }

    public static uint? LogoffChildSession(bool wait = true)
    {
        if (!WTSGetChildSessionId(out var sessionId))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Unable to query the Windows Child Session.");
        }

        if (sessionId == NoChildSessionId)
        {
            return null;
        }

        if (!WTSLogoffSession(0, sessionId, wait))
        {
            throw CreateLastWin32Exception($"Unable to log off Child Session {sessionId}.");
        }

        return sessionId;
    }

    private static Win32Exception CreateLastWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastPInvokeError(), message);
    }
}
