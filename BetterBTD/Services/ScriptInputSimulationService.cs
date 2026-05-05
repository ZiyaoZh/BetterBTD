using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using BetterBTD.Core.Config;
using BetterBTD.Core.Simulator;
using BetterBTD.Core.Simulator.Extensions;
using BetterBTD.Models;
using Fischless.WindowsInput;
using InputMouseButton = Fischless.WindowsInput.MouseButton;

namespace BetterBTD.Services;

public sealed class ScriptInputSimulationService
{
    private static readonly Lazy<ScriptInputSimulationService> InstanceHolder = new(() => new ScriptInputSimulationService());
    private const int DefaultClickHoldMilliseconds = 50;
    private const int DefaultDoubleClickIntervalMilliseconds = 80;

    private readonly CoordinateTransformService _coordinateTransformService;
    private readonly GameWindowInfoService _gameWindowInfoService;

    private ScriptInputSimulationService()
    {
        _coordinateTransformService = CoordinateTransformService.Instance;
        _gameWindowInfoService = GameWindowInfoService.Instance;
    }

    public static ScriptInputSimulationService Instance => InstanceHolder.Value;

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        return _gameWindowInfoService.TryGetTargetWindowInfo(out windowInfo);
    }

    public bool TryConvertScriptToScreenCoordinate(Point scriptCoordinate, out Point screenCoordinate)
    {
        screenCoordinate = default;
        if (!TryGetTargetWindowInfo(out var windowInfo))
        {
            return false;
        }

        screenCoordinate = ConvertScriptToScreenCoordinate(scriptCoordinate, windowInfo);
        return true;
    }

    public Point ConvertScriptToScreenCoordinate(Point scriptCoordinate)
    {
        return ConvertScriptToScreenCoordinate(scriptCoordinate, GetRequiredTargetWindowInfo());
    }

    public Point ConvertScriptToScreenCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo)
    {
        return _coordinateTransformService.ToScreenCoordinate(scriptCoordinate, windowInfo);
    }

    public void MoveMouseToScriptCoordinate(double x, double y)
    {
        MoveMouseToScriptCoordinate(new Point(x, y));
    }

    public void MoveMouseToScriptCoordinate(Point scriptCoordinate)
    {
        MoveMouseToScreenCoordinate(ConvertScriptToScreenCoordinate(scriptCoordinate));
    }

    public void MoveMouseToScreenCoordinate(double x, double y)
    {
        MoveMouseToScreenCoordinate(new Point(x, y));
    }

    public void MoveMouseToScreenCoordinate(Point screenCoordinate)
    {
        var absolutePoint = ToVirtualDesktopAbsoluteCoordinate(screenCoordinate);
        Simulation.SendInput.Mouse.MoveMouseToPositionOnVirtualDesktop(absolutePoint.X, absolutePoint.Y);
    }

    public void ClickMouseAtScriptCoordinate(
        double x,
        double y,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = DefaultClickHoldMilliseconds)
    {
        ClickMouseAtScriptCoordinate(new Point(x, y), button, clickCount, holdMilliseconds);
    }

    public void ClickMouseAtScriptCoordinate(
        Point scriptCoordinate,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = DefaultClickHoldMilliseconds)
    {
        ClickMouseAtScreenCoordinate(ConvertScriptToScreenCoordinate(scriptCoordinate), button, clickCount, holdMilliseconds);
    }

    public void ClickMouseAtScreenCoordinate(
        double x,
        double y,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = DefaultClickHoldMilliseconds)
    {
        ClickMouseAtScreenCoordinate(new Point(x, y), button, clickCount, holdMilliseconds);
    }

    public void ClickMouseAtScreenCoordinate(
        Point screenCoordinate,
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = DefaultClickHoldMilliseconds)
    {
        MoveMouseToScreenCoordinate(screenCoordinate);
        ClickMouse(button, clickCount, holdMilliseconds);
    }

    public void ClickMouse(
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = DefaultClickHoldMilliseconds)
    {
        var effectiveClickCount = Math.Max(1, clickCount);
        var effectiveHoldMilliseconds = Math.Max(0, holdMilliseconds);

        for (var index = 0; index < effectiveClickCount; index++)
        {
            MouseDown(button);

            if (effectiveHoldMilliseconds > 0)
            {
                Thread.Sleep(effectiveHoldMilliseconds);
            }

            MouseUp(button);

            if (index < effectiveClickCount - 1)
            {
                Thread.Sleep(DefaultDoubleClickIntervalMilliseconds);
            }
        }
    }

    public void MouseDown(InputMouseButton button = InputMouseButton.LeftButton)
    {
        switch (button)
        {
            case InputMouseButton.LeftButton:
                Simulation.SendInput.Mouse.LeftButtonDown();
                break;
            case InputMouseButton.MiddleButton:
                Simulation.SendInput.Mouse.MiddleButtonDown();
                break;
            case InputMouseButton.RightButton:
                Simulation.SendInput.Mouse.RightButtonDown();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.");
        }
    }

    public void MouseUp(InputMouseButton button = InputMouseButton.LeftButton)
    {
        switch (button)
        {
            case InputMouseButton.LeftButton:
                Simulation.SendInput.Mouse.LeftButtonUp();
                break;
            case InputMouseButton.MiddleButton:
                Simulation.SendInput.Mouse.MiddleButtonUp();
                break;
            case InputMouseButton.RightButton:
                Simulation.SendInput.Mouse.RightButtonUp();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.");
        }
    }

    public void PressKey(KeyId key)
    {
        Simulation.SendInput.SimulateKey(key);
    }

    public void KeyDown(KeyId key)
    {
        Simulation.SendInput.SimulateKey(key, KeyType.KeyDown);
    }

    public void KeyUp(KeyId key)
    {
        Simulation.SendInput.SimulateKey(key, KeyType.KeyUp);
    }

    public void HoldKey(KeyId key)
    {
        Simulation.SendInput.SimulateKey(key, KeyType.Hold);
    }

    public void PressHotkey(HotkeyBinding hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        Simulation.SendInput.SimulateHotkey(hotkey);
    }

    public void PressCombination(ModifierKeys modifiers, params KeyId[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Simulation.SendInput.SimulateCombination(modifiers, keys);
    }

    public void PressCombination(IEnumerable<KeyId> modifierKeys, IEnumerable<KeyId> keys)
    {
        ArgumentNullException.ThrowIfNull(modifierKeys);
        ArgumentNullException.ThrowIfNull(keys);
        Simulation.SendInput.SimulateCombination(modifierKeys, keys);
    }

    private GameWindowInfo GetRequiredTargetWindowInfo()
    {
        if (_gameWindowInfoService.TryGetTargetWindowInfo(out var windowInfo))
        {
            return windowInfo;
        }

        throw new InvalidOperationException(
            $"Target game window '{_gameWindowInfoService.TargetWindowTitle}' was not found or is not available.");
    }

    private static Point ToVirtualDesktopAbsoluteCoordinate(Point screenCoordinate)
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var width = Math.Max(1d, SystemParameters.VirtualScreenWidth);
        var height = Math.Max(1d, SystemParameters.VirtualScreenHeight);

        return new Point(
            ScaleToAbsoluteCoordinate(screenCoordinate.X, left, width),
            ScaleToAbsoluteCoordinate(screenCoordinate.Y, top, height));
    }

    private static double ScaleToAbsoluteCoordinate(double coordinate, double origin, double length)
    {
        if (length <= 1d)
        {
            return 0d;
        }

        var normalized = (coordinate - origin) * 65535d / (length - 1d);
        return Math.Clamp(normalized, 0d, 65535d);
    }
}
