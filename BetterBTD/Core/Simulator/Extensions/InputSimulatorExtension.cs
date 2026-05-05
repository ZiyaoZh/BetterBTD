using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using BetterBTD.Core.Config;
using Fischless.WindowsInput;

namespace BetterBTD.Core.Simulator.Extensions;

/// <summary>
/// 用于扩展<seealso cref="Fischless.WindowsInput.InputSimulator"/>的功能
/// </summary>
public static class InputSimulatorExtension
{
    public static void SimulateKey(this InputSimulator self, KeyId key, KeyType type = KeyType.KeyPress)
    {
        ArgumentNullException.ThrowIfNull(self);

        switch (type)
        {
            case KeyType.KeyPress:
                KeyPress(self, key);
                break;
            case KeyType.KeyDown:
                KeyDown(self, key);
                break;
            case KeyType.KeyUp:
                KeyUp(self, key);
                break;
            case KeyType.Hold:
                HoldKeyPress(self, key);
                break;
            default:
                break;
        }
    }

    public static void SimulateHotkey(this InputSimulator self, HotkeyBinding hotkey)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(hotkey);

        var modifierKeys = ExpandModifierKeys(hotkey.Modifiers);
        if (modifierKeys.Count == 0)
        {
            self.SimulateKey(hotkey.Key);
            return;
        }

        self.SimulateCombination(modifierKeys, [hotkey.Key]);
    }

    public static void SimulateCombination(this InputSimulator self, ModifierKeys modifiers, params KeyId[] keys)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(keys);

        self.SimulateCombination(ExpandModifierKeys(modifiers), keys);
    }

    public static void SimulateCombination(this InputSimulator self, IEnumerable<KeyId> modifierKeys, IEnumerable<KeyId> keys)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(modifierKeys);
        ArgumentNullException.ThrowIfNull(keys);

        var effectiveModifierKeys = modifierKeys
            .Where(IsValidKey)
            .Distinct()
            .ToList();
        var effectiveKeys = keys
            .Where(IsValidKey)
            .ToList();

        foreach (var modifierKey in effectiveModifierKeys)
        {
            KeyDown(self, modifierKey);
        }

        try
        {
            foreach (var key in effectiveKeys)
            {
                KeyPress(self, key);
            }
        }
        finally
        {
            for (var index = effectiveModifierKeys.Count - 1; index >= 0; index--)
            {
                KeyUp(self, effectiveModifierKeys[index]);
            }
        }
    }

    /// <summary>
    /// 模拟玩家操作
    /// </summary>
    /// <param name="action">动作</param>
    /// <param name="type">按键类型</param>
    public static void SimulateAction(this InputSimulator self, BTDActions action, KeyType type = KeyType.KeyPress)
    {
        self.SimulateKey(action.ToActionKey(), type);
    }

    private static void HoldKeyPress(InputSimulator self, KeyId key)
    {
        KeyDown(self, key);
        Thread.Sleep(1000);
        KeyUp(self, key);
    }

    private static void KeyPress(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonClick();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonClick();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonClick();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonClick(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonClick(0x0002);
                break;
            default:
                var vk = key.ToVK();
                if (InputBuilder.IsExtendedKey(vk))
                {
                    self.Keyboard.KeyPress(false, vk);
                }
                else
                {
                    self.Keyboard.KeyPress(vk);
                }

                break;
        }
    }

    private static void KeyDown(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonDown();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonDown();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonDown();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonDown(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonDown(0x0002);
                break;
            default:
                var vk = key.ToVK();
                if (InputBuilder.IsExtendedKey(vk))
                {
                    self.Keyboard.KeyDown(false, vk);
                }
                else
                {
                    self.Keyboard.KeyDown(vk);
                }

                break;
        }
    }

    private static void KeyUp(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonUp();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonUp();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonUp();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonUp(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonUp(0x0002);
                break;
            default:
                var vk = key.ToVK();
                if (InputBuilder.IsExtendedKey(vk))
                {
                    self.Keyboard.KeyUp(false, vk);
                }
                else
                {
                    self.Keyboard.KeyUp(vk);
                }

                break;
        }
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
