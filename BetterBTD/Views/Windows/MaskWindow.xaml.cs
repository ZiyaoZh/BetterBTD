using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BetterBTD.Helpers;
using BetterBTD.Views.Windows.Overlay;
using System.Windows.Media;

namespace BetterBTD.Views.Windows;

public partial class MaskWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x8000000;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public MaskWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        SourceInitialized += (_, _) => ApplyOverlayStyles();
    }

    public void ShowOverlay(NativeWindowBounds bounds, double scaleFactor, string targetWindowTitle)
    {
        UpdateLayout(bounds, scaleFactor, targetWindowTitle);
        OverlaySurface.ScaleFactor = scaleFactor;

        if (!IsVisible)
        {
            Show();
        }

        BringToTop();
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void UpdateLayout(NativeWindowBounds bounds, double scaleFactor, string targetWindowTitle)
    {
        var safeScaleFactor = scaleFactor <= 0 ? 1d : scaleFactor;

        Left = bounds.Left / safeScaleFactor;
        Top = bounds.Top / safeScaleFactor;
        Width = bounds.Width / safeScaleFactor;
        Height = bounds.Height / safeScaleFactor;
        StatusTextBlock.Text = $"Tracking {targetWindowTitle} ({bounds.Width}x{bounds.Height})";
    }

    public Guid RegisterRectangle(Rect bounds, Color strokeColor, double strokeThickness = 2, Color? fillColor = null, double cornerRadius = 0)
    {
        return OverlaySurface.AddElement(new MaskOverlayRectangleElement(Guid.NewGuid(), bounds, strokeColor, strokeThickness, fillColor, cornerRadius));
    }

    public Guid RegisterText(Point location, string text, Color foregroundColor, double fontSize = 16, Color? backgroundColor = null, Thickness? padding = null, FontFamily? fontFamily = null, FontWeight? fontWeight = null)
    {
        return OverlaySurface.AddElement(new MaskOverlayTextElement(Guid.NewGuid(), location, text, foregroundColor, fontSize, backgroundColor, padding, fontFamily, fontWeight));
    }

    public bool UpdateRectangle(Guid id, Action<MaskOverlayRectangleElement> updateAction)
    {
        return OverlaySurface.UpdateElement(id, updateAction);
    }

    public bool UpdateText(Guid id, Action<MaskOverlayTextElement> updateAction)
    {
        return OverlaySurface.UpdateElement(id, updateAction);
    }

    public Guid RegisterLine(Point startPoint, Point endPoint, Color strokeColor, double strokeThickness = 2, bool showArrowHead = false, double arrowHeadLength = 12, double arrowHeadAngle = 30)
    {
        return OverlaySurface.AddElement(new MaskOverlayLineElement(Guid.NewGuid(), startPoint, endPoint, strokeColor, strokeThickness, showArrowHead, arrowHeadLength, arrowHeadAngle));
    }

    public bool UpdateLine(Guid id, Action<MaskOverlayLineElement> updateAction)
    {
        return OverlaySurface.UpdateElement(id, updateAction);
    }

    public Guid RegisterAnchor(
        Point center,
        Color strokeColor,
        double strokeThickness = 2,
        double crosshairLength = 10,
        double gapRadius = 4,
        double ringRadius = 0,
        string? label = null,
        Color? labelForegroundColor = null,
        Color? labelBackgroundColor = null)
    {
        return OverlaySurface.AddElement(
            new MaskOverlayAnchorElement(
                Guid.NewGuid(),
                center,
                strokeColor,
                strokeThickness,
                crosshairLength,
                gapRadius,
                ringRadius,
                label,
                labelForegroundColor,
                labelBackgroundColor));
    }

    public bool UpdateAnchor(Guid id, Action<MaskOverlayAnchorElement> updateAction)
    {
        return OverlaySurface.UpdateElement(id, updateAction);
    }

    public bool RemoveElement(Guid id)
    {
        return OverlaySurface.RemoveElement(id);
    }

    public void ClearElements()
    {
        OverlaySurface.ClearElements();
    }

    private void ApplyOverlayStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var style = GetWindowLong(handle, GwlExStyle).ToInt64();
        style |= WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate;
        _ = SetWindowLong(handle, GwlExStyle, new nint(style));
    }

    private void BringToTop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        _ = SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    private static nint GetWindowLong(nint hWnd, int nIndex)
    {
        return Environment.Is64BitProcess ? GetWindowLongPtr64(hWnd, nIndex) : new nint(GetWindowLong32(hWnd, nIndex));
    }

    private static nint SetWindowLong(nint hWnd, int nIndex, nint dwNewLong)
    {
        return Environment.Is64BitProcess ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new nint(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
