using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterBTD.Helpers;

public static class NativeWindowHelper
{
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
    private static extern uint GetDpiForWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

public readonly record struct NativeWindowBounds(int Left, int Top, int Width, int Height);
