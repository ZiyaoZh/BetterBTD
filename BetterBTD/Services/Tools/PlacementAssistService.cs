using BetterBTD.Core.Simulator;
using Fischless.HotkeyCapture;
using Vanara.PInvoke;
using FormsKeys = System.Windows.Forms.Keys;

namespace BetterBTD.Services.Tools;

public sealed class PlacementAssistService : IDisposable
{
    public static PlacementAssistService Instance { get; } = new();

    private HotkeyHook? _hotkeyHook;
    private bool _isEnabled;

    private PlacementAssistService()
    {
    }

    public bool IsEnabled => _isEnabled;

    public void Enable()
    {
        if (_isEnabled)
        {
            return;
        }

        var hotkeyHook = new HotkeyHook();
        try
        {
            hotkeyHook.KeyPressed += OnKeyPressed;
            hotkeyHook.RegisterHotKey(User32.HotKeyModifiers.MOD_NONE, FormsKeys.Up);
            hotkeyHook.RegisterHotKey(User32.HotKeyModifiers.MOD_NONE, FormsKeys.Down);
            hotkeyHook.RegisterHotKey(User32.HotKeyModifiers.MOD_NONE, FormsKeys.Left);
            hotkeyHook.RegisterHotKey(User32.HotKeyModifiers.MOD_NONE, FormsKeys.Right);

            _hotkeyHook = hotkeyHook;
            _isEnabled = true;
        }
        catch
        {
            hotkeyHook.KeyPressed -= OnKeyPressed;
            hotkeyHook.Dispose();
            throw;
        }
    }

    public void Disable()
    {
        if (!_isEnabled && _hotkeyHook is null)
        {
            return;
        }

        if (_hotkeyHook is not null)
        {
            _hotkeyHook.KeyPressed -= OnKeyPressed;
            _hotkeyHook.Dispose();
            _hotkeyHook = null;
        }

        _isEnabled = false;
    }

    public void Dispose()
    {
        Disable();
    }

    private static void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var (deltaX, deltaY) = e.Key switch
        {
            FormsKeys.Up => (0, -1),
            FormsKeys.Down => (0, 1),
            FormsKeys.Left => (-1, 0),
            FormsKeys.Right => (1, 0),
            _ => (0, 0)
        };

        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        Simulation.SendInput.Mouse.MoveMouseBy(deltaX, deltaY);
    }
}
