using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using BetterBTD.Core.Config;
using BetterBTD.Core.Simulator;
using BetterBTD.Models;
using Microsoft.Win32.SafeHandles;
using Vanara.PInvoke;
using InputMouseButton = Fischless.WindowsInput.MouseButton;

namespace BetterBTD.Services.Tasks.Input;

public sealed class HardwareInputSimulationService
{
    private const int InterceptionMaxKeyboard = 10;
    private const int InterceptionMaxMouse = 10;
    private const int InterceptionMaxDevice = InterceptionMaxKeyboard + InterceptionMaxMouse;

    private const uint FileDeviceUnknown = 0x00000022;
    private const uint MethodBuffered = 0;
    private const uint FileAnyAccess = 0;

    private const uint GenericRead = 0x80000000;
    private const uint OpenExisting = 3;

    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = 0xFFFFFFFF;

    private const ushort InterceptionMouseMoveAbsolute = 0x001;
    private const ushort InterceptionMouseVirtualDesktop = 0x002;

    private static readonly Lazy<HardwareInputSimulationService> InstanceHolder =
        new(() => new HardwareInputSimulationService());

    private readonly object _syncRoot = new();

    private DeviceEntry[]? _devices;
    private IntPtr[]? _waitHandles;
    private IntPtr _stopEvent;
    private Thread? _captureThread;
    private bool _isInitialized;
    private int _keyboardDevice = KeyboardDevice(0);
    private int _mouseDevice = MouseDevice(0);

    private HardwareInputSimulationService()
    {
    }

    public static HardwareInputSimulationService Instance => InstanceHolder.Value;

    public bool IsDriverInstalled => ProbeDriverInstallation();

    public bool TryEnsureInitialized()
    {
        lock (_syncRoot)
        {
            if (_isInitialized)
            {
                return true;
            }

            if (!IsDriverInstalled)
            {
                return false;
            }

            if (!TryCreateContext())
            {
                ShutdownCore();
                return false;
            }

            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "BetterBTD-Interception"
            };
            _captureThread.Start();

            _isInitialized = true;
            return true;
        }
    }

    public void Shutdown()
    {
        lock (_syncRoot)
        {
            ShutdownCore();
        }
    }

    public void MoveMouseToVirtualDesktop(double absoluteX, double absoluteY)
    {
        if (!TryEnsureInitialized())
        {
            throw new InvalidOperationException("Interception driver is not available.");
        }

        var stroke = new InterceptionMouseStroke
        {
            Flags = (ushort)(InterceptionMouseMoveAbsolute | InterceptionMouseVirtualDesktop),
            X = ClampAbsoluteCoordinate(absoluteX),
            Y = ClampAbsoluteCoordinate(absoluteY)
        };

        SendMouseStroke(stroke);
    }

    public void MouseButtonDown(InputMouseButton button)
    {
        SendMouseStroke(new InterceptionMouseStroke
        {
            State = ToMouseDownState(button)
        });
    }

    public void MouseButtonUp(InputMouseButton button)
    {
        SendMouseStroke(new InterceptionMouseStroke
        {
            State = ToMouseUpState(button)
        });
    }

    public void MouseXButtonDown(int buttonId)
    {
        SendMouseStroke(new InterceptionMouseStroke
        {
            State = ToMouseXButtonDownState(buttonId)
        });
    }

    public void MouseXButtonUp(int buttonId)
    {
        SendMouseStroke(new InterceptionMouseStroke
        {
            State = ToMouseXButtonUpState(buttonId)
        });
    }

    public void KeyDown(KeyId key)
    {
        if (!TryEnsureInitialized())
        {
            throw new InvalidOperationException("Interception driver is not available.");
        }

        if (!TryCreateKeyStroke(key, isKeyUp: false, out var stroke))
        {
            return;
        }

        SendKeyboardStroke(stroke);
    }

    public void KeyUp(KeyId key)
    {
        if (!TryEnsureInitialized())
        {
            throw new InvalidOperationException("Interception driver is not available.");
        }

        if (!TryCreateKeyStroke(key, isKeyUp: true, out var stroke))
        {
            return;
        }

        SendKeyboardStroke(stroke);
    }

    public void KeyPress(KeyId key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    private void CaptureLoop()
    {
        while (true)
        {
            var waitResult = WaitForNextHandle();
            if (waitResult == 0)
            {
                return;
            }

            if (waitResult < 0)
            {
                Thread.Sleep(50);
                continue;
            }

            try
            {
                PumpDevice(waitResult);
            }
            catch
            {
                Thread.Sleep(10);
            }
        }
    }

    private int WaitForNextHandle()
    {
        var waitHandles = _waitHandles;
        if (waitHandles is null || waitHandles.Length == 0)
        {
            return 0;
        }

        var result = WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, 250);
        if (result == WaitTimeout)
        {
            return -1;
        }

        if (result == WaitFailed)
        {
            return -2;
        }

        var index = (int)(result - WaitObject0);
        if (index < 0 || index >= waitHandles.Length)
        {
            return -2;
        }

        return index == waitHandles.Length - 1 ? 0 : index + 1;
    }

    private void PumpDevice(int device)
    {
        if (_devices is null)
        {
            return;
        }

        if (IsKeyboard(device))
        {
            unsafe
            {
                KeyboardInputData rawStroke = default;
                if (!ReadDevice(_devices[device - 1].Handle, device, &rawStroke, (uint)sizeof(KeyboardInputData), out var bytesRead) ||
                    bytesRead < sizeof(KeyboardInputData))
                {
                    return;
                }

                Volatile.Write(ref _keyboardDevice, device);

                _ = WriteDevice(_devices[device - 1].Handle, &rawStroke, (uint)sizeof(KeyboardInputData), out _);
            }

            return;
        }

        unsafe
        {
            MouseInputData rawStroke = default;
            if (!ReadDevice(_devices[device - 1].Handle, device, &rawStroke, (uint)sizeof(MouseInputData), out var bytesRead) ||
                bytesRead < sizeof(MouseInputData))
            {
                return;
            }

            Volatile.Write(ref _mouseDevice, device);

            _ = WriteDevice(_devices[device - 1].Handle, &rawStroke, (uint)sizeof(MouseInputData), out _);
        }
    }

    private unsafe void SendKeyboardStroke(InterceptionKeyStroke stroke)
    {
        if (!TryEnsureInitialized())
        {
            throw new InvalidOperationException("Interception driver is not available.");
        }

        var devices = _devices ?? throw new InvalidOperationException("Interception context is not initialized.");
        var device = NormalizeDeviceId(Volatile.Read(ref _keyboardDevice), isKeyboard: true);

        var rawStroke = new KeyboardInputData
        {
            MakeCode = stroke.Code,
            Flags = stroke.State,
            ExtraInformation = stroke.Information
        };

        if (!WriteDevice(devices[device - 1].Handle, &rawStroke, (uint)sizeof(KeyboardInputData), out var bytesWritten) ||
            bytesWritten < sizeof(KeyboardInputData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to send keyboard stroke through Interception.");
        }
    }

    private unsafe void SendMouseStroke(InterceptionMouseStroke stroke)
    {
        if (!TryEnsureInitialized())
        {
            throw new InvalidOperationException("Interception driver is not available.");
        }

        var devices = _devices ?? throw new InvalidOperationException("Interception context is not initialized.");
        var device = NormalizeDeviceId(Volatile.Read(ref _mouseDevice), isKeyboard: false);

        var rawStroke = new MouseInputData
        {
            Flags = stroke.Flags,
            ButtonFlags = stroke.State,
            ButtonData = (ushort)stroke.Rolling,
            LastX = stroke.X,
            LastY = stroke.Y,
            ExtraInformation = stroke.Information
        };

        if (!WriteDevice(devices[device - 1].Handle, &rawStroke, (uint)sizeof(MouseInputData), out var bytesWritten) ||
            bytesWritten < sizeof(MouseInputData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to send mouse stroke through Interception.");
        }
    }

    private bool TryCreateContext()
    {
        _devices = new DeviceEntry[InterceptionMaxDevice];
        _waitHandles = new IntPtr[InterceptionMaxDevice + 1];
        _stopEvent = CreateEvent(IntPtr.Zero, true, false, null);
        if (_stopEvent == IntPtr.Zero)
        {
            return false;
        }

        for (var index = 0; index < InterceptionMaxDevice; index++)
        {
            var handle = CreateFile($@"\\.\interception{index:00}", GenericRead, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return false;
            }

            var unempty = CreateEvent(IntPtr.Zero, true, false, null);
            if (unempty == IntPtr.Zero)
            {
                handle.Dispose();
                return false;
            }

            unsafe
            {
                var eventBuffer = new EventHandleBuffer
                {
                    Handle = unempty,
                    Padding = IntPtr.Zero
                };

                if (!SetEventHandle(handle, &eventBuffer))
                {
                    CloseHandle(unempty);
                    handle.Dispose();
                    return false;
                }
            }

            _devices[index] = new DeviceEntry(handle, unempty);
            _waitHandles[index] = unempty;
        }

        _waitHandles[^1] = _stopEvent;

        for (var device = 1; device <= InterceptionMaxDevice; device++)
        {
            if (!SetFilter(device, IsKeyboard(device)
                    ? (ushort)InterceptionFilterKeyState.All
                    : (ushort)InterceptionFilterMouseState.All))
            {
                return false;
            }
        }

        return true;
    }

    private void ShutdownCore()
    {
        _isInitialized = false;

        if (_stopEvent != IntPtr.Zero)
        {
            _ = SetEvent(_stopEvent);
        }

        if (_captureThread is not null && _captureThread.IsAlive)
        {
            Monitor.Exit(_syncRoot);
            try
            {
                _captureThread.Join(TimeSpan.FromSeconds(1));
            }
            finally
            {
                Monitor.Enter(_syncRoot);
            }
        }

        _captureThread = null;

        if (_devices is not null)
        {
            foreach (var device in _devices)
            {
                device.Dispose();
            }
        }

        _devices = null;
        _waitHandles = null;

        if (_stopEvent != IntPtr.Zero)
        {
            CloseHandle(_stopEvent);
            _stopEvent = IntPtr.Zero;
        }

        _keyboardDevice = KeyboardDevice(0);
        _mouseDevice = MouseDevice(0);
    }

    private static bool ProbeDriverInstallation()
    {
        using var handle = CreateFile(@"\\.\interception00", GenericRead, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        return !handle.IsInvalid;
    }

    private unsafe bool SetEventHandle(SafeFileHandle handle, EventHandleBuffer* eventBuffer)
    {
        return DeviceIoControl(
            handle,
            CtlCode(0x810),
            eventBuffer,
            (uint)sizeof(EventHandleBuffer),
            null,
            0,
            out _,
            IntPtr.Zero);
    }

    private unsafe bool SetFilter(int device, ushort filter)
    {
        if (_devices is null)
        {
            return false;
        }

        ushort* filterPointer = &filter;
        return DeviceIoControl(
            _devices[device - 1].Handle,
            CtlCode(0x804),
            filterPointer,
            sizeof(ushort),
            null,
            0,
            out _,
            IntPtr.Zero);
    }

    private static unsafe bool ReadDevice(SafeFileHandle handle, int device, void* outputBuffer, uint outputSize, out uint bytesRead)
    {
        _ = device;
        return DeviceIoControl(handle, CtlCode(0x840), null, 0, outputBuffer, outputSize, out bytesRead, IntPtr.Zero);
    }

    private static unsafe bool WriteDevice(SafeFileHandle handle, void* inputBuffer, uint inputSize, out uint bytesWritten)
    {
        return DeviceIoControl(handle, CtlCode(0x820), inputBuffer, inputSize, null, 0, out bytesWritten, IntPtr.Zero);
    }

    private static bool TryCreateKeyStroke(KeyId key, bool isKeyUp, out InterceptionKeyStroke stroke)
    {
        stroke = default;

        if (key is KeyId.None or KeyId.Unknown)
        {
            return false;
        }

        if (!TryGetScanCode(key, out var scanCode, out var isE0, out var isE1))
        {
            return false;
        }

        var state = isKeyUp ? (ushort)InterceptionKeyState.KeyUp : (ushort)InterceptionKeyState.KeyDown;
        if (isE0)
        {
            state |= (ushort)InterceptionKeyState.E0;
        }

        if (isE1)
        {
            state |= (ushort)InterceptionKeyState.E1;
        }

        stroke = new InterceptionKeyStroke
        {
            Code = scanCode,
            State = state
        };

        return true;
    }

    internal static bool TryGetScanCode(KeyId key, out ushort scanCode, out bool isE0, out bool isE1)
    {
        scanCode = 0;
        isE0 = false;
        isE1 = false;

        if (key == KeyId.NumEnter)
        {
            scanCode = 0x1C;
            isE0 = true;
            return true;
        }

        if (key is KeyId.None or KeyId.Unknown)
        {
            return false;
        }

        if (key == KeyId.Pause)
        {
            scanCode = 0x45;
            isE1 = true;
            return true;
        }

        var virtualKey = (uint)key.ToVK();
        var mapped = MapVirtualKey(virtualKey, 0);

        if (mapped == 0)
        {
            return false;
        }

        scanCode = (ushort)(mapped & 0xFF);
        isE0 = KeyboardInputUtilities.IsExtendedKey(key);
        return true;
    }

    private static ushort ToMouseDownState(InputMouseButton button)
    {
        return button switch
        {
            InputMouseButton.LeftButton => (ushort)InterceptionMouseState.LeftButtonDown,
            InputMouseButton.MiddleButton => (ushort)InterceptionMouseState.MiddleButtonDown,
            InputMouseButton.RightButton => (ushort)InterceptionMouseState.RightButtonDown,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
    }

    private static ushort ToMouseUpState(InputMouseButton button)
    {
        return button switch
        {
            InputMouseButton.LeftButton => (ushort)InterceptionMouseState.LeftButtonUp,
            InputMouseButton.MiddleButton => (ushort)InterceptionMouseState.MiddleButtonUp,
            InputMouseButton.RightButton => (ushort)InterceptionMouseState.RightButtonUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
    }

    private static ushort ToMouseXButtonDownState(int buttonId)
    {
        return buttonId switch
        {
            0x0001 => (ushort)InterceptionMouseState.Button4Down,
            0x0002 => (ushort)InterceptionMouseState.Button5Down,
            _ => throw new ArgumentOutOfRangeException(nameof(buttonId), buttonId, "Unsupported mouse X button.")
        };
    }

    private static ushort ToMouseXButtonUpState(int buttonId)
    {
        return buttonId switch
        {
            0x0001 => (ushort)InterceptionMouseState.Button4Up,
            0x0002 => (ushort)InterceptionMouseState.Button5Up,
            _ => throw new ArgumentOutOfRangeException(nameof(buttonId), buttonId, "Unsupported mouse X button.")
        };
    }

    private static int ClampAbsoluteCoordinate(double value)
    {
        return (int)Math.Clamp(Math.Round(value), 0d, 65535d);
    }

    private static int NormalizeDeviceId(int device, bool isKeyboard)
    {
        return isKeyboard
            ? (IsKeyboard(device) ? device : KeyboardDevice(0))
            : (IsMouse(device) ? device : MouseDevice(0));
    }

    private static int KeyboardDevice(int index) => index + 1;

    private static int MouseDevice(int index) => InterceptionMaxKeyboard + index + 1;

    private static bool IsKeyboard(int device) => device >= KeyboardDevice(0) && device <= KeyboardDevice(InterceptionMaxKeyboard - 1);

    private static bool IsMouse(int device) => device >= MouseDevice(0) && device <= MouseDevice(InterceptionMaxMouse - 1);

    private static uint CtlCode(uint function)
    {
        return (FileDeviceUnknown << 16) | (FileAnyAccess << 14) | (function << 2) | MethodBuffered;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static unsafe extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        void* lpInBuffer,
        uint nInBufferSize,
        void* lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private sealed class DeviceEntry : IDisposable
    {
        public DeviceEntry(SafeFileHandle handle, IntPtr eventHandle)
        {
            Handle = handle;
            EventHandle = eventHandle;
        }

        public SafeFileHandle Handle { get; }

        public IntPtr EventHandle { get; }

        public void Dispose()
        {
            Handle.Dispose();
            if (EventHandle != IntPtr.Zero)
            {
                CloseHandle(EventHandle);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHandleBuffer
    {
        public IntPtr Handle;
        public IntPtr Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort UnitId;
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public ushort UnitId;
        public ushort Flags;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    private readonly record struct InterceptionMouseStroke
    {
        public ushort State { get; init; }

        public ushort Flags { get; init; }

        public short Rolling { get; init; }

        public int X { get; init; }

        public int Y { get; init; }

        public uint Information { get; init; }
    }

    private readonly record struct InterceptionKeyStroke
    {
        public ushort Code { get; init; }

        public ushort State { get; init; }

        public uint Information { get; init; }
    }

    private enum InterceptionKeyState : ushort
    {
        KeyDown = 0x00,
        KeyUp = 0x01,
        E0 = 0x02,
        E1 = 0x04
    }

    private enum InterceptionFilterKeyState : ushort
    {
        All = 0xFFFF
    }

    private enum InterceptionMouseState : ushort
    {
        LeftButtonDown = 0x001,
        LeftButtonUp = 0x002,
        RightButtonDown = 0x004,
        RightButtonUp = 0x008,
        MiddleButtonDown = 0x010,
        MiddleButtonUp = 0x020,
        Button4Down = 0x040,
        Button4Up = 0x080,
        Button5Down = 0x100,
        Button5Up = 0x200
    }

    private enum InterceptionFilterMouseState : ushort
    {
        All = 0xFFFF
    }
}

