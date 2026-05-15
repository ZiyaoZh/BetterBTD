using BetterBTD.Helpers;
using BetterBTD.Models;

namespace BetterBTD.Services.Start.Capture;

public sealed class GameWindowInfoService
{
    private static readonly Lazy<GameWindowInfoService> InstanceHolder = new(() => new GameWindowInfoService());

    private GameWindowInfoService()
    {
    }

    public static GameWindowInfoService Instance => InstanceHolder.Value;

    public string TargetWindowTitle => ConfigurationService.Instance.Current.MaskWindowTargetTitle;

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        var targetHandle = NativeWindowHelper.FindTopLevelWindow(TargetWindowTitle);
        return TryGetWindowInfo(targetHandle, out windowInfo);
    }

    public bool TryGetWindowInfo(nint handle, out GameWindowInfo windowInfo)
    {
        windowInfo = default;

        if (handle == nint.Zero ||
            !NativeWindowHelper.TryGetWindowBounds(handle, out var windowBounds) ||
            !NativeWindowHelper.TryGetClientBounds(handle, out var clientBounds))
        {
            return false;
        }

        windowInfo = new GameWindowInfo(
            handle,
            NativeWindowHelper.GetWindowTitle(handle),
            windowBounds,
            clientBounds,
            NativeWindowHelper.GetWindowScaleFactor(handle));
        return true;
    }
}

