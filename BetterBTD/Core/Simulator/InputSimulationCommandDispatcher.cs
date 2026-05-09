using BetterBTD.Core.Config;
using BetterBTD.Models;
using BetterBTD.Services;
using Fischless.WindowsInput;
using Vanara.PInvoke;
using InputMouseButton = Fischless.WindowsInput.MouseButton;

namespace BetterBTD.Core.Simulator;

internal interface IInputSimulationCommandDispatcher
{
    void Dispatch(IEnumerable<InputSimulationCommand> commands);
}

internal sealed class InputSimulationCommandDispatcher : IInputSimulationCommandDispatcher
{
    private readonly HardwareInputSimulationService _hardwareInputSimulationService;

    public InputSimulationCommandDispatcher()
        : this(HardwareInputSimulationService.Instance)
    {
    }

    internal InputSimulationCommandDispatcher(HardwareInputSimulationService hardwareInputSimulationService)
    {
        _hardwareInputSimulationService = hardwareInputSimulationService ?? throw new ArgumentNullException(nameof(hardwareInputSimulationService));
    }

    public void Dispatch(IEnumerable<InputSimulationCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var effectiveHardwareMode =
            KeyboardMouseSimulationModeExtensions.Parse(ConfigurationService.Instance.Current.KeyboardMouseSimulationModeName) == KeyboardMouseSimulationMode.Hardware &&
            _hardwareInputSimulationService.TryEnsureInitialized();

        foreach (var command in commands)
        {
            DispatchCommand(command, effectiveHardwareMode);
        }
    }

    private void DispatchCommand(InputSimulationCommand command, bool hardwareMode)
    {
        switch (command.Type)
        {
            case InputSimulationCommandType.MoveMouseToVirtualDesktop:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MoveMouseToVirtualDesktop(command.X, command.Y);
                }
                else
                {
                    Simulation.SendInput.Mouse.MoveMouseToPositionOnVirtualDesktop(command.X, command.Y);
                }

                break;
            case InputSimulationCommandType.MouseButtonDown:
                MouseButtonDown(command.MouseButton, hardwareMode);
                break;
            case InputSimulationCommandType.MouseButtonUp:
                MouseButtonUp(command.MouseButton, hardwareMode);
                break;
            case InputSimulationCommandType.KeyPress:
                KeyPress(command.Key, hardwareMode);
                break;
            case InputSimulationCommandType.KeyDown:
                KeyDown(command.Key, hardwareMode);
                break;
            case InputSimulationCommandType.KeyUp:
                KeyUp(command.Key, hardwareMode);
                break;
            case InputSimulationCommandType.Delay:
                Thread.Sleep(Math.Max(0, command.Milliseconds));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void MouseButtonDown(InputMouseButton button, bool hardwareMode)
    {
        if (hardwareMode)
        {
            _hardwareInputSimulationService.MouseButtonDown(button);
            return;
        }

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

    private void MouseButtonUp(InputMouseButton button, bool hardwareMode)
    {
        if (hardwareMode)
        {
            _hardwareInputSimulationService.MouseButtonUp(button);
            return;
        }

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

    private void KeyPress(KeyId key, bool hardwareMode)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonDown(InputMouseButton.LeftButton);
                    _hardwareInputSimulationService.MouseButtonUp(InputMouseButton.LeftButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.LeftButtonClick();
                }

                break;
            case KeyId.MouseRightButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonDown(InputMouseButton.RightButton);
                    _hardwareInputSimulationService.MouseButtonUp(InputMouseButton.RightButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.RightButtonClick();
                }

                break;
            case KeyId.MouseMiddleButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonDown(InputMouseButton.MiddleButton);
                    _hardwareInputSimulationService.MouseButtonUp(InputMouseButton.MiddleButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.MiddleButtonClick();
                }

                break;
            case KeyId.MouseSideButton1:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseXButtonDown(0x0001);
                    _hardwareInputSimulationService.MouseXButtonUp(0x0001);
                }
                else
                {
                    Simulation.SendInput.Mouse.XButtonClick(0x0001);
                }

                break;
            case KeyId.MouseSideButton2:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseXButtonDown(0x0002);
                    _hardwareInputSimulationService.MouseXButtonUp(0x0002);
                }
                else
                {
                    Simulation.SendInput.Mouse.XButtonClick(0x0002);
                }

                break;
            default:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.KeyPress(key);
                }
                else
                {
                    var vk = key.ToVK();
                    if (InputBuilder.IsExtendedKey(vk))
                    {
                        Simulation.SendInput.Keyboard.KeyPress(false, vk);
                    }
                    else
                    {
                        Simulation.SendInput.Keyboard.KeyPress(vk);
                    }
                }

                break;
        }
    }

    private void KeyDown(KeyId key, bool hardwareMode)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonDown(InputMouseButton.LeftButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.LeftButtonDown();
                }

                break;
            case KeyId.MouseRightButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonDown(InputMouseButton.RightButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.RightButtonDown();
                }

                break;
            case KeyId.MouseMiddleButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonDown(InputMouseButton.MiddleButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.MiddleButtonDown();
                }

                break;
            case KeyId.MouseSideButton1:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseXButtonDown(0x0001);
                }
                else
                {
                    Simulation.SendInput.Mouse.XButtonDown(0x0001);
                }

                break;
            case KeyId.MouseSideButton2:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseXButtonDown(0x0002);
                }
                else
                {
                    Simulation.SendInput.Mouse.XButtonDown(0x0002);
                }

                break;
            default:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.KeyDown(key);
                }
                else
                {
                    var vk = key.ToVK();
                    if (InputBuilder.IsExtendedKey(vk))
                    {
                        Simulation.SendInput.Keyboard.KeyDown(false, vk);
                    }
                    else
                    {
                        Simulation.SendInput.Keyboard.KeyDown(vk);
                    }
                }

                break;
        }
    }

    private void KeyUp(KeyId key, bool hardwareMode)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonUp(InputMouseButton.LeftButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                }

                break;
            case KeyId.MouseRightButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonUp(InputMouseButton.RightButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.RightButtonUp();
                }

                break;
            case KeyId.MouseMiddleButton:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseButtonUp(InputMouseButton.MiddleButton);
                }
                else
                {
                    Simulation.SendInput.Mouse.MiddleButtonUp();
                }

                break;
            case KeyId.MouseSideButton1:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseXButtonUp(0x0001);
                }
                else
                {
                    Simulation.SendInput.Mouse.XButtonUp(0x0001);
                }

                break;
            case KeyId.MouseSideButton2:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.MouseXButtonUp(0x0002);
                }
                else
                {
                    Simulation.SendInput.Mouse.XButtonUp(0x0002);
                }

                break;
            default:
                if (hardwareMode)
                {
                    _hardwareInputSimulationService.KeyUp(key);
                }
                else
                {
                    var vk = key.ToVK();
                    if (InputBuilder.IsExtendedKey(vk))
                    {
                        Simulation.SendInput.Keyboard.KeyUp(false, vk);
                    }
                    else
                    {
                        Simulation.SendInput.Keyboard.KeyUp(vk);
                    }
                }

                break;
        }
    }
}
