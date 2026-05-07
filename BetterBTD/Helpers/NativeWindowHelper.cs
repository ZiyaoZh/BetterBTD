using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace BetterBTD.Helpers;

public static class NativeWindowHelper
{
    private const int VkRButton = 0x02;
    private const uint DwmwaExtendedFrameBounds = 9;

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
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

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
