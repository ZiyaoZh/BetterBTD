using System.Windows.Input;
using BetterBTD.Core.Config;
using BetterBTD.Core.Simulator.Extensions;
using Fischless.WindowsInput;
using InputMouseButton = Fischless.WindowsInput.MouseButton;

namespace BetterBTD.Core.Simulator;

internal static class InputSimulationCommandBuilder
{
    private const int DefaultKeyPressHoldMilliseconds = 32;
    private const int DefaultCombinationSettleMilliseconds = 16;

    public static IReadOnlyList<InputSimulationCommand> BuildMoveMouseToVirtualDesktop(double x, double y)
    {
        return
        [
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.MoveMouseToVirtualDesktop,
                X = x,
                Y = y
            }
        ];
    }

    public static IReadOnlyList<InputSimulationCommand> BuildDelay(int milliseconds)
    {
        return
        [
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.Delay,
                Milliseconds = Math.Max(0, milliseconds)
            }
        ];
    }

    public static IReadOnlyList<InputSimulationCommand> BuildClickMouse(
        InputMouseButton button,
        int clickCount,
        int holdMilliseconds,
        int doubleClickIntervalMilliseconds)
    {
        var commands = new List<InputSimulationCommand>();
        var effectiveClickCount = Math.Max(1, clickCount);
        var effectiveHoldMilliseconds = Math.Max(0, holdMilliseconds);
        var effectiveDoubleClickIntervalMilliseconds = Math.Max(0, doubleClickIntervalMilliseconds);

        for (var index = 0; index < effectiveClickCount; index++)
        {
            commands.Add(new InputSimulationCommand
            {
                Type = InputSimulationCommandType.MouseButtonDown,
                MouseButton = button
            });

            if (effectiveHoldMilliseconds > 0)
            {
                commands.Add(new InputSimulationCommand
                {
                    Type = InputSimulationCommandType.Delay,
                    Milliseconds = effectiveHoldMilliseconds
                });
            }

            commands.Add(new InputSimulationCommand
            {
                Type = InputSimulationCommandType.MouseButtonUp,
                MouseButton = button
            });

            if (index < effectiveClickCount - 1 && effectiveDoubleClickIntervalMilliseconds > 0)
            {
                commands.Add(new InputSimulationCommand
                {
                    Type = InputSimulationCommandType.Delay,
                    Milliseconds = effectiveDoubleClickIntervalMilliseconds
                });
            }
        }

        return commands;
    }

    public static IReadOnlyList<InputSimulationCommand> BuildSimulateKey(KeyId key, KeyType type = KeyType.KeyPress)
    {
        return type switch
        {
            KeyType.KeyDown => BuildSingleKeyCommand(InputSimulationCommandType.KeyDown, key),
            KeyType.KeyUp => BuildSingleKeyCommand(InputSimulationCommandType.KeyUp, key),
            KeyType.Hold => BuildHoldKey(key),
            _ => BuildTapKey(key)
        };
    }

    public static IReadOnlyList<InputSimulationCommand> BuildSimulateHotkey(HotkeyBinding hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        var modifierKeys = ExpandModifierKeys(hotkey.Modifiers);
        return modifierKeys.Count == 0
            ? BuildSimulateKey(hotkey.Key)
            : BuildSimulateCombination(modifierKeys, [hotkey.Key]);
    }

    public static IReadOnlyList<InputSimulationCommand> BuildSimulateCombination(ModifierKeys modifiers, params KeyId[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return BuildSimulateCombination(ExpandModifierKeys(modifiers), keys);
    }

    public static IReadOnlyList<InputSimulationCommand> BuildSimulateCombination(IEnumerable<KeyId> modifierKeys, IEnumerable<KeyId> keys)
    {
        ArgumentNullException.ThrowIfNull(modifierKeys);
        ArgumentNullException.ThrowIfNull(keys);

        var commands = new List<InputSimulationCommand>();
        var effectiveModifierKeys = modifierKeys
            .Where(IsValidKey)
            .Distinct()
            .ToList();
        var effectiveKeys = keys
            .Where(IsValidKey)
            .ToList();

        foreach (var modifierKey in effectiveModifierKeys)
        {
            commands.Add(new InputSimulationCommand
            {
                Type = InputSimulationCommandType.KeyDown,
                Key = modifierKey
            });
        }

        if (effectiveModifierKeys.Count > 0 && effectiveKeys.Count > 0)
        {
            commands.Add(new InputSimulationCommand
            {
                Type = InputSimulationCommandType.Delay,
                Milliseconds = DefaultCombinationSettleMilliseconds
            });
        }

        try
        {
            foreach (var key in effectiveKeys)
            {
                commands.AddRange(BuildTapKey(key));
            }
        }
        finally
        {
            if (effectiveModifierKeys.Count > 0 && effectiveKeys.Count > 0)
            {
                commands.Add(new InputSimulationCommand
                {
                    Type = InputSimulationCommandType.Delay,
                    Milliseconds = DefaultCombinationSettleMilliseconds
                });
            }

            for (var index = effectiveModifierKeys.Count - 1; index >= 0; index--)
            {
                commands.Add(new InputSimulationCommand
                {
                    Type = InputSimulationCommandType.KeyUp,
                    Key = effectiveModifierKeys[index]
                });
            }
        }

        return commands;
    }

    private static IReadOnlyList<InputSimulationCommand> BuildSingleKeyCommand(InputSimulationCommandType type, KeyId key)
    {
        return
        [
            new InputSimulationCommand
            {
                Type = type,
                Key = key
            }
        ];
    }

    private static IReadOnlyList<InputSimulationCommand> BuildTapKey(KeyId key)
    {
        if (!IsValidKey(key))
        {
            return [];
        }

        return
        [
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.KeyDown,
                Key = key
            },
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.Delay,
                Milliseconds = DefaultKeyPressHoldMilliseconds
            },
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.KeyUp,
                Key = key
            }
        ];
    }

    private static IReadOnlyList<InputSimulationCommand> BuildHoldKey(KeyId key)
    {
        if (!IsValidKey(key))
        {
            return [];
        }

        return
        [
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.KeyDown,
                Key = key
            },
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.Delay,
                Milliseconds = 1000
            },
            new InputSimulationCommand
            {
                Type = InputSimulationCommandType.KeyUp,
                Key = key
            }
        ];
    }

    private static List<KeyId> ExpandModifierKeys(ModifierKeys modifiers)
    {
        var keys = new List<KeyId>(4);

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            keys.Add(KeyId.LeftCtrl);
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            keys.Add(KeyId.LeftShift);
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            keys.Add(KeyId.LeftAlt);
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            keys.Add(KeyId.LeftWin);
        }

        return keys;
    }

    private static bool IsValidKey(KeyId key)
    {
        return key is not KeyId.None and not KeyId.Unknown;
    }
}
