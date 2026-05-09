using System.Windows;
using System.Windows.Input;
using BetterBTD.Core.Config;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.Simulator;
using BetterBTD.Helpers;
using BetterBTD.Models;
using BetterBTD.Services;
using BetterBTD.Tests.TestDoubles;
using InputMouseButton = Fischless.WindowsInput.MouseButton;

namespace BetterBTD.Tests.Services;

public sealed class ScriptInputSimulationServiceTests
{
    [Fact]
    public void PressHotkey_RecordsExpandedKeySequence()
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var service = new ScriptInputSimulationService(
            CreateEnvironment(),
            dispatcher);

        service.PressHotkey(new HotkeyBinding
        {
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Key = KeyId.U
        });

        Assert.Collection(
            dispatcher.Commands,
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyDown, command.Type);
                Assert.Equal(KeyId.LeftCtrl, command.Key);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyDown, command.Type);
                Assert.Equal(KeyId.LeftShift, command.Key);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(16, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyDown, command.Type);
                Assert.Equal(KeyId.U, command.Key);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(32, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyUp, command.Type);
                Assert.Equal(KeyId.U, command.Key);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(16, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyUp, command.Type);
                Assert.Equal(KeyId.LeftShift, command.Key);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyUp, command.Type);
                Assert.Equal(KeyId.LeftCtrl, command.Key);
            });
    }

    [Fact]
    public void HeroPlacementHotkey_WhenConfiguredAsU_RecordsKeyPressU()
    {
        using var _ = new KeyBindingOverrideScope(new HotkeyBinding
        {
            Key = KeyId.U
        });

        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var service = new ScriptInputSimulationService(
            CreateEnvironment(),
            dispatcher);

        var heroHotkey = ScriptExecutionKeyBindingResolver.ResolvePlacementHotkey("Hero:Geraldo");
        service.PressHotkey(heroHotkey);

        Assert.Collection(
            dispatcher.Commands,
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyDown, command.Type);
                Assert.Equal(KeyId.U, command.Key);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(32, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.KeyUp, command.Type);
                Assert.Equal(KeyId.U, command.Key);
            });
    }

    [Fact]
    public void ClickMouse_RightDoubleClick_RecordsMouseAndDelayCommands()
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var service = new ScriptInputSimulationService(
            CreateEnvironment(),
            dispatcher);

        service.ClickMouse(InputMouseButton.RightButton, clickCount: 2, holdMilliseconds: 30);

        Assert.Collection(
            dispatcher.Commands,
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MouseButtonDown, command.Type);
                Assert.Equal(InputMouseButton.RightButton, command.MouseButton);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(30, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MouseButtonUp, command.Type);
                Assert.Equal(InputMouseButton.RightButton, command.MouseButton);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(80, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MouseButtonDown, command.Type);
                Assert.Equal(InputMouseButton.RightButton, command.MouseButton);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(30, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MouseButtonUp, command.Type);
                Assert.Equal(InputMouseButton.RightButton, command.MouseButton);
            });
    }

    [Fact]
    public void MoveMouseToScriptCoordinate_RecordsVirtualDesktopMoveCommand()
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var service = new ScriptInputSimulationService(
            CreateEnvironment(
                convertScriptToScreenCoordinate: (scriptPoint, _) => new Point(scriptPoint.X + 10, scriptPoint.Y + 20),
                toVirtualDesktopAbsoluteCoordinate: screenPoint => new Point(screenPoint.X + 1000, screenPoint.Y + 2000)),
            dispatcher);

        service.MoveMouseToScriptCoordinate(new Point(120, 240));

        var command = Assert.Single(dispatcher.Commands);
        Assert.Equal(InputSimulationCommandType.MoveMouseToVirtualDesktop, command.Type);
        Assert.Equal(1130, command.X);
        Assert.Equal(2260, command.Y);
    }

    [Fact]
    public void ClickMouseAtScreenCoordinate_RecordsMoveSettleAndClickSequence()
    {
        var dispatcher = new RecordingInputSimulationCommandDispatcher();
        var service = new ScriptInputSimulationService(
            CreateEnvironment(
                toVirtualDesktopAbsoluteCoordinate: screenPoint => new Point(screenPoint.X + 500, screenPoint.Y + 700)),
            dispatcher);

        service.ClickMouseAtScreenCoordinate(new Point(100, 200), holdMilliseconds: 30);

        Assert.Collection(
            dispatcher.Commands,
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MoveMouseToVirtualDesktop, command.Type);
                Assert.Equal(600, command.X);
                Assert.Equal(900, command.Y);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(24, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MouseButtonDown, command.Type);
                Assert.Equal(InputMouseButton.LeftButton, command.MouseButton);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.Delay, command.Type);
                Assert.Equal(30, command.Milliseconds);
            },
            command =>
            {
                Assert.Equal(InputSimulationCommandType.MouseButtonUp, command.Type);
                Assert.Equal(InputMouseButton.LeftButton, command.MouseButton);
            });
    }

    private static FakeScriptInputSimulationEnvironment CreateEnvironment(
        Func<Point, GameWindowInfo, Point>? convertScriptToScreenCoordinate = null,
        Func<Point, Point>? toVirtualDesktopAbsoluteCoordinate = null)
    {
        var windowInfo = new GameWindowInfo(
            (nint)123,
            "Test Window",
            new NativeWindowBounds(0, 0, 1920, 1080),
            new NativeWindowBounds(0, 0, 1920, 1080),
            1d);

        return new FakeScriptInputSimulationEnvironment(
            windowInfo,
            convertScriptToScreenCoordinate,
            toVirtualDesktopAbsoluteCoordinate);
    }
}
