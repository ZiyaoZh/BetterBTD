using System.ComponentModel;

namespace BetterBTD.Services.ChildSession;

internal static class ChildSessionRuntimeState
{
    private static readonly object SyncRoot = new();
    private static BetterBtdInstanceRole _role = BetterBtdInstanceRole.Primary;
    private static uint? _rootSessionId;
    private static bool _primaryControlBlocked;

    public static BetterBtdInstanceRole Role
    {
        get
        {
            lock (SyncRoot)
            {
                return _role;
            }
        }
    }

    public static bool IsChildSession => Role == BetterBtdInstanceRole.ChildSession;

    public static string LogSessionDirectoryName
    {
        get
        {
            BetterBtdInstanceRole role;
            uint? rootSessionId;
            lock (SyncRoot)
            {
                role = _role;
                rootSessionId = _rootSessionId;
            }

            uint? sessionId = null;
            try
            {
                sessionId = ChildSessionNativeMethods.GetCurrentSessionId();
            }
            catch (Exception ex) when (ex is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
            {
                sessionId = rootSessionId;
            }

            return $"{(role == BetterBtdInstanceRole.ChildSession ? "Child" : "Primary")}-Session-{sessionId?.ToString() ?? "Unknown"}";
        }
    }

    public static bool PrimaryControlBlocked
    {
        get
        {
            lock (SyncRoot)
            {
                return _primaryControlBlocked;
            }
        }
    }

    public static bool CanPersistSharedData =>
        !IsChildSession && !PrimaryControlBlocked;

    public static void Initialize(InstanceLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (SyncRoot)
        {
            _role = options.Role;
            _rootSessionId = options.RootSessionId;
            _primaryControlBlocked = false;
        }
    }

    public static void SetPrimaryControlBlocked(bool blocked)
    {
        lock (SyncRoot)
        {
            _primaryControlBlocked = Role == BetterBtdInstanceRole.Primary && blocked;
        }
    }

    public static void EnsurePrimaryCanControl()
    {
        if (Role == BetterBtdInstanceRole.Primary && PrimaryControlBlocked)
        {
            throw new InvalidOperationException(
                "The primary BetterBTD instance is read-only while a Child Session is active.");
        }
    }

    public static void EnsureSharedDataWritable()
    {
        if (IsChildSession)
        {
            throw new InvalidOperationException(
                "Shared BetterBTD data is read-only in a Child Session instance.");
        }

        EnsurePrimaryCanControl();
    }
}
