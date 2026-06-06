using Fischless.GameCapture.Graphics.Helpers;
using SharpDX.Direct3D11;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SharpDX;
using Vanara.PInvoke;
using Device = SharpDX.Direct3D11.Device;

namespace Fischless.GameCapture.DwmSharedSurface;

public partial class SharedSurfaceCapture : IGameCapture, IGameCaptureFrameMetadataProvider
{
    // 窗口句柄
    private nint _hWnd;

    private static readonly object LockObject = new();

    // D3D 设备
    private Device? _d3dDevice;

    // 截图区域
    private ResourceRegion? _region;

    // 暂存贴图
    private Texture2D? _stagingTexture;

    // Surface 大小
    private int _surfaceWidth;
    private int _surfaceHeight;
    private ulong? _lastNativeUpdateId;
    private long _sourceFrameSequence;
    private long _lastFrameCapturedAtUnixMs;

    public bool IsCapturing { get; private set; }

    [LibraryImport("user32.dll", EntryPoint = "DwmGetDxSharedSurface", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DwmGetDxSharedSurface(IntPtr hWnd, out IntPtr phSurface, out long pAdapterLuid, out long pFmtWindow, out long pPresentFlags, out long pWin32KUpdateId);

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        _hWnd = hWnd;
        User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
        _region = GetGameScreenRegion(hWnd);
        _d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport); // Software/Hardware

        IsCapturing = true;
    }

    /// <summary>
    /// 从 GetWindowRect 的带窗口阴影面积矩形 截取出 GetClientRect的矩形（游戏区域）
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    private static ResourceRegion? GetGameScreenRegion(nint hWnd)
    {
        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
        {
            return null;
        }

        ResourceRegion region = new();
        User32.GetWindowRect(hWnd, out var windowWithShadowRect);
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        User32.GetClientRect(hWnd, out var clientRect);

        region.Left = windowRect.Left - windowWithShadowRect.Left;
        // 标题栏 windowRect.Height - clientRect.Height 上阴影 windowRect.Top - windowWithShadowRect.Top
        region.Top = windowRect.Height - clientRect.Height + windowRect.Top - windowWithShadowRect.Top;
        region.Right = region.Left + clientRect.Width;
        region.Bottom = region.Top + clientRect.Height;
        region.Front = 0;
        region.Back = 1;

        return region;
    }

    public Mat? Capture()
    {
        lock (LockObject)
        {
            if (_d3dDevice == null)
            {
                Debug.WriteLine("D3Device is null.");
                return null;
            }

            if (!DwmGetDxSharedSurface(_hWnd, out var phSurface, out _, out _, out _, out var updateId))
            {
                return null;
            }
            if (phSurface == 0)
            {
                return null;
            }

            try
            {
                var nativeUpdateId = unchecked((ulong)updateId);
                if (_lastNativeUpdateId == nativeUpdateId && Interlocked.Read(ref _sourceFrameSequence) > 0)
                {
                    return null;
                }

                using var surfaceTexture = _d3dDevice.OpenSharedResource<Texture2D>(phSurface);

                if (_stagingTexture == null || _surfaceWidth != surfaceTexture.Description.Width ||
                    _surfaceHeight != surfaceTexture.Description.Height)
                {
                    if (User32.IsIconic(_hWnd))
                        return null;

                    _stagingTexture?.Dispose();
                    _stagingTexture = null;
                    _surfaceWidth = surfaceTexture.Description.Width;
                    _surfaceHeight = surfaceTexture.Description.Height;
                    _region = GetGameScreenRegion(_hWnd);
                }

                _stagingTexture ??= Direct3D11Helper.CreateStagingTexture(_d3dDevice, _surfaceWidth, _surfaceHeight, _region);
                var mat = _stagingTexture.CreateMat(_d3dDevice, surfaceTexture, _region);
                if (mat is not null && !mat.Empty())
                {
                    _lastNativeUpdateId = nativeUpdateId;
                    Volatile.Write(ref _lastFrameCapturedAtUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    Interlocked.Increment(ref _sourceFrameSequence);
                }

                return mat;
            }
            catch (SharpDXException e)
            {
                Debug.WriteLine($"SharpDXException: {e.Descriptor}");
                _d3dDevice?.Dispose();
                _d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            }

            return null;
        }
    }

    public void Stop()
    {
        lock (LockObject)
        {
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _hWnd = 0;
            IsCapturing = false;
            _lastNativeUpdateId = null;
            Interlocked.Exchange(ref _sourceFrameSequence, 0);
            Volatile.Write(ref _lastFrameCapturedAtUnixMs, 0);
        }
    }

    public bool TryGetFrameMetadata(out GameCaptureFrameMetadata metadata)
    {
        lock (LockObject)
        {
            var sequence = Interlocked.Read(ref _sourceFrameSequence);
            var capturedAtUnixMs = Volatile.Read(ref _lastFrameCapturedAtUnixMs);
            if (sequence <= 0 || capturedAtUnixMs <= 0)
            {
                metadata = default;
                return false;
            }

            metadata = new GameCaptureFrameMetadata(
                sequence,
                DateTimeOffset.FromUnixTimeMilliseconds(capturedAtUnixMs),
                _lastNativeUpdateId);
            return true;
        }
    }
}
