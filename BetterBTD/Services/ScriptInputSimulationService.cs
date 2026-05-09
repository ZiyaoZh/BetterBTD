using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using BetterBTD.Core.Config;
using BetterBTD.Core.Simulator;
using BetterBTD.Helpers;
using BetterBTD.Models;
using Fischless.WindowsInput;
using InputMouseButton = Fischless.WindowsInput.MouseButton;

namespace BetterBTD.Services;

public sealed class ScriptInputSimulationService
{
    private static readonly Lazy<ScriptInputSimulationService> InstanceHolder = new(() => new ScriptInputSimulationService());
    private const int DefaultClickHoldMilliseconds = 50;
    private const int DefaultDoubleClickIntervalMilliseconds = 80;
    private const int DefaultMouseMoveSettleMilliseconds = 24;
    private const int DefaultTargetWindowActivationSettleMilliseconds = 50;

    private readonly IScriptInputSimulationEnvironment _environment;
    private readonly IInputSimulationCommandDispatcher _dispatcher;

    private ScriptInputSimulationService()
        : this(new ScriptInputSimulationEnvironment(), new InputSimulationCommandDispatcher())
    {
    }

    internal ScriptInputSimulationService(
        IScriptInputSimulationEnvironment environment,
        IInputSimulationCommandDispatcher dispatcher)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public static ScriptInputSimulationService Instance => InstanceHolder.Value;

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        return _environment.TryGetTargetWindowInfo(out windowInfo);
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
        return _environment.ConvertScriptToScreenCoordinate(scriptCoordinate, windowInfo);
    }

    public void MoveMouseToScriptCoordinate(double x, double y)
    {
        MoveMouseToScriptCoordinate(new Point(x, y));
    }

    public void MoveMouseToScriptCoordinate(Point scriptCoordinate)
    {
        var windowInfo = GetRequiredTargetWindowInfo();
        MoveMouseToScreenCoordinateCore(ConvertScriptToScreenCoordinate(scriptCoordinate, windowInfo));
    }

    public void MoveMouseToScreenCoordinate(double x, double y)
    {
        MoveMouseToScreenCoordinate(new Point(x, y));
    }

    public void MoveMouseToScreenCoordinate(Point screenCoordinate)
    {
        MoveMouseToScreenCoordinateCore(screenCoordinate);
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
        var windowInfo = GetRequiredTargetWindowInfo();
        ClickMouseAtScreenCoordinate(ConvertScriptToScreenCoordinate(scriptCoordinate, windowInfo), button, clickCount, holdMilliseconds);
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
        MoveMouseToScreenCoordinateCore(screenCoordinate);

        if (DefaultMouseMoveSettleMilliseconds > 0)
        {
            _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildDelay(DefaultMouseMoveSettleMilliseconds));
        }

        ClickMouseCore(button, clickCount, holdMilliseconds);
    }

    public void ClickMouse(
        InputMouseButton button = InputMouseButton.LeftButton,
        int clickCount = 1,
        int holdMilliseconds = DefaultClickHoldMilliseconds)
    {
        ClickMouseCore(button, clickCount, holdMilliseconds);
    }

    public void MouseDown(InputMouseButton button = InputMouseButton.LeftButton)
    {
        _dispatcher.Dispatch(
        [
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.MouseButtonDown,
                MouseButton = button
            }
        ]);
    }

    public void MouseUp(InputMouseButton button = InputMouseButton.LeftButton)
    {
        _dispatcher.Dispatch(
        [
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.MouseButtonUp,
                MouseButton = button
            }
        ]);
    }

    public void PressKey(KeyId key)
    {
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateKey(key));
    }

    public void KeyDown(KeyId key)
    {
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateKey(key, Core.Simulator.Extensions.KeyType.KeyDown));
    }

    public void KeyUp(KeyId key)
    {
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateKey(key, Core.Simulator.Extensions.KeyType.KeyUp));
    }

    public void HoldKey(KeyId key)
    {
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateKey(key, Core.Simulator.Extensions.KeyType.Hold));
    }

    public void PressHotkey(HotkeyBinding hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateHotkey(hotkey));
    }

    public void PressCombination(ModifierKeys modifiers, params KeyId[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateCombination(modifiers, keys));
    }

    public void PressCombination(IEnumerable<KeyId> modifierKeys, IEnumerable<KeyId> keys)
    {
        ArgumentNullException.ThrowIfNull(modifierKeys);
        ArgumentNullException.ThrowIfNull(keys);
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildSimulateCombination(modifierKeys, keys));
    }

    public void PrepareTargetWindowForInput()
    {
        var windowInfo = GetRequiredTargetWindowInfo();
        if (!NativeWindowHelper.IsForegroundWindow(windowInfo.Handle) &&
            NativeWindowHelper.TryActivateWindow(windowInfo.Handle))
        {
            Thread.Sleep(DefaultTargetWindowActivationSettleMilliseconds);
        }
    }

    private void MoveMouseToScreenCoordinateCore(Point screenCoordinate)
    {
        var absolutePoint = _environment.ToVirtualDesktopAbsoluteCoordinate(screenCoordinate);
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildMoveMouseToVirtualDesktop(absolutePoint.X, absolutePoint.Y));
    }

    private void ClickMouseCore(
        InputMouseButton button,
        int clickCount,
        int holdMilliseconds)
    {
        _dispatcher.Dispatch(InputSimulationCommandBuilder.BuildClickMouse(
            button,
            clickCount,
            holdMilliseconds,
            DefaultDoubleClickIntervalMilliseconds));
    }

    private GameWindowInfo GetRequiredTargetWindowInfo()
    {
        if (_environment.TryGetTargetWindowInfo(out var windowInfo))
        {
            return windowInfo;
        }

        throw new InvalidOperationException(
            $"Target game window '{_environment.TargetWindowTitle}' was not found or is not available.");
    }
}

internal interface IScriptInputSimulationEnvironment
{
    string TargetWindowTitle { get; }

    bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo);

    Point ConvertScriptToScreenCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo);

    Point ToVirtualDesktopAbsoluteCoordinate(Point screenCoordinate);
}

internal sealed class ScriptInputSimulationEnvironment : IScriptInputSimulationEnvironment
{
    private readonly CoordinateTransformService _coordinateTransformService = CoordinateTransformService.Instance;
    private readonly GameWindowInfoService _gameWindowInfoService = GameWindowInfoService.Instance;

    public string TargetWindowTitle => _gameWindowInfoService.TargetWindowTitle;

    public bool TryGetTargetWindowInfo(out GameWindowInfo windowInfo)
    {
        return _gameWindowInfoService.TryGetTargetWindowInfo(out windowInfo);
    }

    public Point ConvertScriptToScreenCoordinate(Point scriptCoordinate, GameWindowInfo windowInfo)
    {
        return _coordinateTransformService.ToScreenCoordinate(scriptCoordinate, windowInfo);
    }

    public Point ToVirtualDesktopAbsoluteCoordinate(Point screenCoordinate)
    {
        return NativeWindowHelper.ToVirtualDesktopAbsoluteCoordinate(screenCoordinate);
    }
}
