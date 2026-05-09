using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace BetterBTD.Helpers;

public static class NativeWindowHelper
{
    private const int VkRButton = 0x02;
    private const uint DwmwaExtendedFrameBounds = 9;
    private const int SwRestore = 9;
    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;
    private const int SmCxvirtualscreen = 78;
    private const int SmCyvirtualscreen = 79;

    public static nint FindTopLevelWindow(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return nint.Zero;
        }

        nint matchedHandle = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                return true;
            }

            var currentTitle = GetWindowTitle(hWnd);
            if (!string.Equals(currentTitle, title, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            matchedHandle = hWnd;
            return false;
        }, nint.Zero);

        return matchedHandle;
    }

    public static bool TryGetWindowBounds(nint hWnd, out NativeWindowBounds bounds)
    {
        bounds = default;

        if (hWnd == nint.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        if (!TryGetVisibleWindowRect(hWnd, out var rect))
        {
            return false;
        }

        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return false;
        }

        bounds = new NativeWindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    public static bool TryGetRawWindowBounds(nint hWnd, out NativeWindowBounds bounds)
    {
        bounds = default;

        if (hWnd == nint.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        if (!GetWindowRect(hWnd, out var rect))
        {
            return false;
        }

        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return false;
        }

        bounds = new NativeWindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    public static bool TryGetClientBounds(nint hWnd, out NativeWindowBounds bounds)
    {
        bounds = default;

        if (hWnd == nint.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        if (!GetClientRect(hWnd, out var clientRect))
        {
            return false;
        }

        var topLeft = new POINT { X = clientRect.Left, Y = clientRect.Top };
        var bottomRight = new POINT { X = clientRect.Right, Y = clientRect.Bottom };

        if (!ClientToScreen(hWnd, ref topLeft) || !ClientToScreen(hWnd, ref bottomRight))
        {
            return false;
        }

        if (bottomRight.X <= topLeft.X || bottomRight.Y <= topLeft.Y)
        {
            return false;
        }

        bounds = new NativeWindowBounds(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
        return true;
    }

    public static string GetWindowTitle(nint hWnd)
    {
        var titleLength = GetWindowTextLength(hWnd);
        if (titleLength <= 0)
        {
            return string.Empty;
        }

        var titleBuilder = new StringBuilder(titleLength + 1);
        _ = GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
        return titleBuilder.ToString();
    }

    public static double GetWindowScaleFactor(nint hWnd)
    {
        if (hWnd == nint.Zero)
        {
            return 1d;
        }

        try
        {
            var dpi = GetDpiForWindow(hWnd);
            return dpi > 0 ? dpi / 96d : 1d;
        }
        catch
        {
            return 1d;
        }
    }

    public static bool TryGetCursorPosition(out Point point)
    {
        point = default;

        if (!GetCursorPos(out var nativePoint))
        {
            return false;
        }

        point = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static NativeWindowBounds GetVirtualScreenBounds()
    {
        var left = GetSystemMetrics(SmXvirtualscreen);
        var top = GetSystemMetrics(SmYvirtualscreen);
        var width = GetSystemMetrics(SmCxvirtualscreen);
        var height = GetSystemMetrics(SmCyvirtualscreen);

        if (width <= 0 || height <= 0)
        {
            return new NativeWindowBounds(0, 0, 1, 1);
        }

        return new NativeWindowBounds(left, top, width, height);
    }

    public static Point ToVirtualDesktopAbsoluteCoordinate(Point screenCoordinate)
    {
        var virtualScreenBounds = GetVirtualScreenBounds();
        return new Point(
            ScaleToAbsoluteCoordinate(screenCoordinate.X, virtualScreenBounds.Left, virtualScreenBounds.Width),
            ScaleToAbsoluteCoordinate(screenCoordinate.Y, virtualScreenBounds.Top, virtualScreenBounds.Height));
    }

    public static bool TryClientToScreen(nint hWnd, Point clientPoint, out Point screenPoint)
    {
        screenPoint = default;

        if (hWnd == nint.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        var nativePoint = new POINT
        {
            X = (int)Math.Round(clientPoint.X),
            Y = (int)Math.Round(clientPoint.Y)
        };

        if (!ClientToScreen(hWnd, ref nativePoint))
        {
            return false;
        }

        screenPoint = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static bool TryScreenToClient(nint hWnd, Point screenPoint, out Point clientPoint)
    {
        clientPoint = default;

        if (hWnd == nint.Zero || !IsWindowVisible(hWnd) || IsIconic(hWnd))
        {
            return false;
        }

        var nativePoint = new POINT
        {
            X = (int)Math.Round(screenPoint.X),
            Y = (int)Math.Round(screenPoint.Y)
        };

        if (!ScreenToClient(hWnd, ref nativePoint))
        {
            return false;
        }

        clientPoint = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static bool IsRightMouseButtonDown()
    {
        return (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
    }

    public static bool IsForegroundWindow(nint hWnd)
    {
        return hWnd != nint.Zero && GetForegroundWindow() == hWnd;
    }

    public static bool TryActivateWindow(nint hWnd)
    {
        if (hWnd == nint.Zero || !IsWindowVisible(hWnd))
        {
            return false;
        }

        if (IsForegroundWindow(hWnd))
        {
            return true;
        }

        if (IsIconic(hWnd))
        {
            _ = ShowWindow(hWnd, SwRestore);
        }

        var foregroundWindow = GetForegroundWindow();
        var currentThreadId = GetCurrentThreadId();
        var targetThreadId = GetWindowThreadProcessId(hWnd, nint.Zero);
        var foregroundThreadId = foregroundWindow == nint.Zero
            ? 0u
            : GetWindowThreadProcessId(foregroundWindow, nint.Zero);

        var attachedToTarget = false;
        var attachedToForeground = false;

        try
        {
            if (targetThreadId != 0 && targetThreadId != currentThreadId)
            {
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            if (foregroundThreadId != 0 &&
                foregroundThreadId != currentThreadId &&
                foregroundThreadId != targetThreadId)
            {
                attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            _ = BringWindowToTop(hWnd);
            _ = SetActiveWindow(hWnd);
            _ = SetForegroundWindow(hWnd);
            _ = SetFocus(hWnd);
        }
        finally
        {
            if (attachedToForeground)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                _ = AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        return IsForegroundWindow(hWnd);
    }

    private static bool TryGetVisibleWindowRect(nint hWnd, out RECT rect)
    {
        rect = default;

        if (DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<RECT>()) == 0 &&
            rect.Right > rect.Left &&
            rect.Bottom > rect.Top)
        {
            return true;
        }

        return GetWindowRect(hWnd, out rect);
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

    private static double ScaleToAbsoluteCoordinate(double coordinate, double origin, double length)
    {
        if (length <= 1d)
        {
            return 0d;
        }

        var normalized = (coordinate - origin) * 65535d / (length - 1d);
        return Math.Clamp(normalized, 0d, 65535d);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}

public readonly record struct NativeWindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool Contains(Point screenPoint)
    {
        return screenPoint.X >= Left &&
               screenPoint.X < Right &&
               screenPoint.Y >= Top &&
               screenPoint.Y < Bottom;
    }
}
