using System;
using System.Windows;
using BetterBTD.Helpers;
using BetterBTD.Models;
using Vanara.PInvoke;
using static Vanara.PInvoke.Gdi32;

namespace BetterBTD.Services.Settings;

public sealed class DeviceInfoService
{
    private static readonly Lazy<DeviceInfoService> InstanceHolder = new(() => new DeviceInfoService());

    private DeviceInfoService()
    {
    }

    public static DeviceInfoService Instance => InstanceHolder.Value;

    public DeviceDisplayInfo GetPrimaryDisplayInfo()
    {
        using User32.SafeReleaseHDC hdc = User32.GetDC(IntPtr.Zero);

        var physicalWidth = Gdi32.GetDeviceCaps(hdc, DeviceCap.HORZRES);
        var physicalHeight = Gdi32.GetDeviceCaps(hdc, DeviceCap.VERTRES);
        var dpiX = Gdi32.GetDeviceCaps(hdc, DeviceCap.LOGPIXELSX);
        var dpiY = Gdi32.GetDeviceCaps(hdc, DeviceCap.LOGPIXELSY);
        var scaleX = dpiX > 0 ? dpiX / 96d : 1d;
        var scaleY = dpiY > 0 ? dpiY / 96d : 1d;
        var logicalWidth = (int)Math.Round(physicalWidth / scaleX);
        var logicalHeight = (int)Math.Round(physicalHeight / scaleY);

        var logicalWorkArea = SystemParameters.WorkArea;
        var logicalWorkAreaBounds = new NativeWindowBounds(
            (int)Math.Round(logicalWorkArea.Left),
            (int)Math.Round(logicalWorkArea.Top),
            (int)Math.Round(logicalWorkArea.Width),
            (int)Math.Round(logicalWorkArea.Height));
        var physicalWorkAreaBounds = new NativeWindowBounds(
            (int)Math.Round(logicalWorkArea.Left * scaleX),
            (int)Math.Round(logicalWorkArea.Top * scaleY),
            (int)Math.Round(logicalWorkArea.Width * scaleX),
            (int)Math.Round(logicalWorkArea.Height * scaleY));

        return new DeviceDisplayInfo(
            physicalWidth,
            physicalHeight,
            logicalWidth,
            logicalHeight,
            physicalWorkAreaBounds,
            logicalWorkAreaBounds,
            scaleX,
            scaleY);
    }
}

