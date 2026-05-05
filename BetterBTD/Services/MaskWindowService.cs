using System;
using System.Windows;
using System.Windows.Threading;
using BetterBTD.Helpers;
using BetterBTD.Views.Windows;
using BetterBTD.Views.Windows.Overlay;
using System.Windows.Media;

namespace BetterBTD.Services;

public sealed class MaskWindowService
{
    private static readonly Lazy<MaskWindowService> InstanceHolder = new(() => new MaskWindowService());
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);

    private DispatcherTimer? _pollingTimer;
    private MaskWindow? _maskWindow;
    private bool _isRunning;

    private MaskWindowService()
    {
    }

    public static MaskWindowService Instance => InstanceHolder.Value;

    public event EventHandler<bool>? RunningStateChanged;

    public bool IsRunning => _isRunning;

    public string TargetWindowTitle => ConfigurationService.Instance.Current.MaskWindowTargetTitle;

    public void Start()
    {
        ExecuteOnUiThread(() =>
        {
            if (_isRunning)
            {
                RefreshNow();
                return;
            }

            _maskWindow ??= new MaskWindow();
            _pollingTimer ??= CreatePollingTimer();
            _pollingTimer.Start();

            _isRunning = true;
            RunningStateChanged?.Invoke(this, true);

            RefreshNow();
        });
    }

    public void Stop()
    {
        ExecuteOnUiThread(() =>
        {
            if (!_isRunning)
            {
                return;
            }

            _pollingTimer?.Stop();
            _maskWindow?.HideOverlay();

            _isRunning = false;
            RunningStateChanged?.Invoke(this, false);
        });
    }

    public void Shutdown()
    {
        ExecuteOnUiThread(() =>
        {
            if (_isRunning)
            {
                _pollingTimer?.Stop();
                _isRunning = false;
                RunningStateChanged?.Invoke(this, false);
            }

            if (_pollingTimer is not null)
            {
                _pollingTimer.Tick -= OnPollingTimerTick;
                _pollingTimer = null;
            }

            if (_maskWindow is null)
            {
                return;
            }

            _maskWindow.ClearElements();

            if (_maskWindow.IsLoaded)
            {
                _maskWindow.Close();
            }

            _maskWindow = null;
        });
    }

    public void RefreshNow()
    {
        ExecuteOnUiThread(UpdateOverlayWindow);
    }

    public Guid RegisterRectangle(Rect bounds, Color strokeColor, double strokeThickness = 2, Color? fillColor = null, double cornerRadius = 0)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.RegisterRectangle(bounds, strokeColor, strokeThickness, fillColor, cornerRadius);
        });
    }

    public Guid RegisterText(Point location, string text, Color foregroundColor, double fontSize = 16, Color? backgroundColor = null, Thickness? padding = null, FontFamily? fontFamily = null, FontWeight? fontWeight = null)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.RegisterText(location, text, foregroundColor, fontSize, backgroundColor, padding, fontFamily, fontWeight);
        });
    }

    public bool UpdateRectangle(Guid id, Action<MaskOverlayRectangleElement> updateAction)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.UpdateRectangle(id, updateAction);
        });
    }

    public bool UpdateText(Guid id, Action<MaskOverlayTextElement> updateAction)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.UpdateText(id, updateAction);
        });
    }

    public Guid RegisterLine(Point startPoint, Point endPoint, Color strokeColor, double strokeThickness = 2, bool showArrowHead = false, double arrowHeadLength = 12, double arrowHeadAngle = 30)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.RegisterLine(startPoint, endPoint, strokeColor, strokeThickness, showArrowHead, arrowHeadLength, arrowHeadAngle);
        });
    }

    public bool UpdateLine(Guid id, Action<MaskOverlayLineElement> updateAction)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.UpdateLine(id, updateAction);
        });
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
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.RegisterAnchor(
                center,
                strokeColor,
                strokeThickness,
                crosshairLength,
                gapRadius,
                ringRadius,
                label,
                labelForegroundColor,
                labelBackgroundColor);
        });
    }

    public bool UpdateAnchor(Guid id, Action<MaskOverlayAnchorElement> updateAction)
    {
        return ExecuteOnUiThread(() =>
        {
            var window = EnsureMaskWindow();
            return window.UpdateAnchor(id, updateAction);
        });
    }

    public bool RemoveElement(Guid id)
    {
        return ExecuteOnUiThread(() =>
        {
            if (_maskWindow is null)
            {
                return false;
            }

            return _maskWindow.RemoveElement(id);
        });
    }

    public void ClearElements()
    {
        ExecuteOnUiThread(() => _maskWindow?.ClearElements());
    }

    private DispatcherTimer CreatePollingTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _pollInterval
        };

        timer.Tick += OnPollingTimerTick;
        return timer;
    }

    private void OnPollingTimerTick(object? sender, EventArgs e)
    {
        UpdateOverlayWindow();
    }

    private void UpdateOverlayWindow()
    {
        if (!_isRunning)
        {
            return;
        }

        var targetHandle = NativeWindowHelper.FindTopLevelWindow(TargetWindowTitle);
        if (targetHandle == nint.Zero || !NativeWindowHelper.TryGetWindowBounds(targetHandle, out var bounds))
        {
            _maskWindow?.HideOverlay();
            return;
        }

        _maskWindow ??= new MaskWindow();
        var scaleFactor = NativeWindowHelper.GetWindowScaleFactor(targetHandle);
        _maskWindow.ShowOverlay(bounds, scaleFactor, TargetWindowTitle);
    }

    private MaskWindow EnsureMaskWindow()
    {
        _maskWindow ??= new MaskWindow();
        return _maskWindow;
    }

    private static void ExecuteOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static T ExecuteOnUiThread<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.Invoke(action);
    }
}
