using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Input;
using BetterBTD.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using static Vanara.PInvoke.User32;

namespace BetterBTD.Core.Config;

/// <summary>
/// BTD按键绑定配置
/// </summary>
[Serializable]
public sealed partial class KeyBindingsConfig : ObservableObject
{
    /// <summary>
    /// 是否启用全局按键映射
    /// </summary>
    [ObservableProperty]
    private bool _globalKeyMappingEnabled;

    /// <summary>
    /// 默认放置猴子按键绑定
    /// </summary>
    public TowerPlacementBindings TowerPlacement { get; set; } = new();

    /// <summary>
    /// 默认技能按键绑定
    /// </summary>
    public AbilityBindings Abilities { get; set; } = new();

    /// <summary>
    /// 默认英雄物品按键绑定
    /// </summary>
    public HeroInventoryBindings HeroInventory { get; set; } = new();

    /// <summary>
    /// 默认通用操作按键绑定
    /// </summary>
    public GeneralActionBindings General { get; set; } = new();
}

[Serializable]
public sealed class HotkeyBinding : ObservableObject
{
    private ModifierKeys _modifiers;
    private KeyId _key = KeyId.None;

    public ModifierKeys Modifiers
    {
        get => _modifiers;
        set
        {
            if (SetProperty(ref _modifiers, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public KeyId Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => KeyBindingTextFormatter.Format(Modifiers, Key);
}

[Serializable]
public sealed partial class TowerPlacementBindings : ObservableObject
{
    [ObservableProperty] private HotkeyBinding _dartMonkey = new() { Key = KeyId.Q };
    [ObservableProperty] private HotkeyBinding _boomerangMonkey = new() { Key = KeyId.W };
    [ObservableProperty] private HotkeyBinding _bombShooter = new() { Key = KeyId.E };
    [ObservableProperty] private HotkeyBinding _tackShooter = new() { Key = KeyId.R };
    [ObservableProperty] private HotkeyBinding _iceMonkey = new() { Key = KeyId.T };
    [ObservableProperty] private HotkeyBinding _glueGunner = new() { Key = KeyId.Y };
    [ObservableProperty] private HotkeyBinding _desperado = new() { Key = KeyId.None };
    [ObservableProperty] private HotkeyBinding _sniperMonkey = new() { Key = KeyId.Z };
    [ObservableProperty] private HotkeyBinding _monkeySub = new() { Key = KeyId.X };
    [ObservableProperty] private HotkeyBinding _monkeyBuccaneer = new() { Key = KeyId.C };
    [ObservableProperty] private HotkeyBinding _monkeyAce = new() { Key = KeyId.V };
    [ObservableProperty] private HotkeyBinding _heliPilot = new() { Key = KeyId.B };
    [ObservableProperty] private HotkeyBinding _mortarMonkey = new() { Key = KeyId.N };
    [ObservableProperty] private HotkeyBinding _dartlingGunner = new() { Key = KeyId.M };
    [ObservableProperty] private HotkeyBinding _wizardMonkey = new() { Key = KeyId.A };
    [ObservableProperty] private HotkeyBinding _superMonkey = new() { Key = KeyId.S };
    [ObservableProperty] private HotkeyBinding _ninjaMonkey = new() { Key = KeyId.D };
    [ObservableProperty] private HotkeyBinding _alchemist = new() { Key = KeyId.F };
    [ObservableProperty] private HotkeyBinding _druid = new() { Key = KeyId.G };
    [ObservableProperty] private HotkeyBinding _merMonkey = new() { Key = KeyId.O };
    [ObservableProperty] private HotkeyBinding _skywarden = new() { Key = KeyId.None };
    [ObservableProperty] private HotkeyBinding _bananaFarm = new() { Key = KeyId.H };
    [ObservableProperty] private HotkeyBinding _spikeFactory = new() { Key = KeyId.J };
    [ObservableProperty] private HotkeyBinding _monkeyVillage = new() { Key = KeyId.K };
    [ObservableProperty] private HotkeyBinding _engineerMonkey = new() { Key = KeyId.L };
    [ObservableProperty] private HotkeyBinding _beastHandler = new() { Key = KeyId.I };
}

[Serializable]
public sealed partial class AbilityBindings : ObservableObject
{
    [ObservableProperty] private HotkeyBinding _activatedAbility1 = new() { Key = KeyId.D1 };
    [ObservableProperty] private HotkeyBinding _activatedAbility2 = new() { Key = KeyId.D2 };
    [ObservableProperty] private HotkeyBinding _activatedAbility3 = new() { Key = KeyId.D3 };
    [ObservableProperty] private HotkeyBinding _activatedAbility4 = new() { Key = KeyId.D4 };
    [ObservableProperty] private HotkeyBinding _activatedAbility5 = new() { Key = KeyId.D5 };
    [ObservableProperty] private HotkeyBinding _activatedAbility6 = new() { Key = KeyId.D6 };
    [ObservableProperty] private HotkeyBinding _activatedAbility7 = new() { Key = KeyId.D7 };
    [ObservableProperty] private HotkeyBinding _activatedAbility8 = new() { Key = KeyId.D8 };
    [ObservableProperty] private HotkeyBinding _activatedAbility9 = new() { Key = KeyId.D9 };
    [ObservableProperty] private HotkeyBinding _activatedAbility10 = new() { Key = KeyId.D0 };
    [ObservableProperty] private HotkeyBinding _activatedAbility11 = new() { Key = KeyId.Minus };
    [ObservableProperty] private HotkeyBinding _activatedAbility12 = new() { Key = KeyId.Equal };
}

[Serializable]
public sealed partial class HeroInventoryBindings : ObservableObject
{
    [ObservableProperty] private HotkeyBinding _inventory1 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.Q };
    [ObservableProperty] private HotkeyBinding _inventory2 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.W };
    [ObservableProperty] private HotkeyBinding _inventory3 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.E };
    [ObservableProperty] private HotkeyBinding _inventory4 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.R };
    [ObservableProperty] private HotkeyBinding _inventory5 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.T };
    [ObservableProperty] private HotkeyBinding _inventory6 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.A };
    [ObservableProperty] private HotkeyBinding _inventory7 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.S };
    [ObservableProperty] private HotkeyBinding _inventory8 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.D };
    [ObservableProperty] private HotkeyBinding _inventory9 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.F };
    [ObservableProperty] private HotkeyBinding _inventory10 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.G };
    [ObservableProperty] private HotkeyBinding _inventory11 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.H };
    [ObservableProperty] private HotkeyBinding _inventory12 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.Z };
    [ObservableProperty] private HotkeyBinding _inventory13 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.X };
    [ObservableProperty] private HotkeyBinding _inventory14 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.C };
    [ObservableProperty] private HotkeyBinding _inventory15 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.V };
    [ObservableProperty] private HotkeyBinding _inventory16 = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.B };
}

[Serializable]
public sealed partial class GeneralActionBindings : ObservableObject
{
    [ObservableProperty] private HotkeyBinding _hero = new() { Key = KeyId.U };
    [ObservableProperty] private HotkeyBinding _sell = new() { Key = KeyId.Backspace };
    [ObservableProperty] private HotkeyBinding _upgradePath1 = new() { Key = KeyId.Comma };
    [ObservableProperty] private HotkeyBinding _upgradePath2 = new() { Key = KeyId.Period };
    [ObservableProperty] private HotkeyBinding _upgradePath3 = new() { Key = KeyId.Slash };
    [ObservableProperty] private HotkeyBinding _changeTargeting = new() { Key = KeyId.Tab };
    [ObservableProperty] private HotkeyBinding _reverseChangeTargeting = new() { Modifiers = ModifierKeys.Control, Key = KeyId.Tab };
    [ObservableProperty] private HotkeyBinding _monkeySpecial = new() { Key = KeyId.PageDown };
    [ObservableProperty] private HotkeyBinding _monkeySpecial2 = new() { Key = KeyId.PageUp };
    [ObservableProperty] private HotkeyBinding _playFastForward = new() { Key = KeyId.Space };
    [ObservableProperty] private HotkeyBinding _sendNextRound = new() { Modifiers = ModifierKeys.Shift, Key = KeyId.Space };
    [ObservableProperty] private HotkeyBinding _mergeBeast = new() { Key = KeyId.None };
    [ObservableProperty] private HotkeyBinding _quickRestart = new() { Key = KeyId.None };
    [ObservableProperty] private HotkeyBinding _activateSelectedTowerAbility1 = new() { Key = KeyId.None };
    [ObservableProperty] private HotkeyBinding _activateSelectedTowerAbility2 = new() { Key = KeyId.None };
    [ObservableProperty] private HotkeyBinding _activateSelectedTowerAbility3 = new() { Key = KeyId.None };
}

public static class KeyBindingTextFormatter
{
    public static string Format(ModifierKeys modifiers, KeyId key)
    {
        var parts = new List<string>(4);

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToName());
        return string.Join("+", parts);
    }
}

public static class KeyIdConverter
{
    /// <summary>
    /// 将KeyId转换为字符串（可在后续支持多语言），按键名称的显示尽量与BTDUI一致
    /// </summary>
    public static string ToName(this KeyId value)
    {
        return LocalizationService.Instance.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            ? ToChineseName(value)
            : ToEnglishName(value);
    }

    private static string ToChineseName(KeyId value)
    {
        return value switch
        {
            KeyId.None => LocalizationService.Instance.T("Settings.KeyBindings.Editor.Unset"),
            KeyId.Unknown => LocalizationService.Instance.T("Settings.KeyBindings.Editor.Unknown"),
            KeyId.MouseLeftButton => "鼠标左键",
            KeyId.MouseRightButton => "鼠标右键",
            KeyId.MouseMiddleButton => "鼠标中键",
            KeyId.MouseSideButton1 => "鼠标侧键1",
            KeyId.MouseSideButton2 => "鼠标侧键2",
            KeyId.Apps => "菜单键",
            _ => EnglishKeyNameToChinese(value),
        };
    }

    private static string EnglishKeyNameToChinese(KeyId value)
    {
        var engName = ToEnglishName(value);
        if (engName.StartsWith("Left ", StringComparison.OrdinalIgnoreCase) ||
            engName.StartsWith("Right ", StringComparison.OrdinalIgnoreCase))
        {
            return engName.Replace("Left ", "左", StringComparison.OrdinalIgnoreCase).Replace("Right ", "右", StringComparison.OrdinalIgnoreCase);
        }

        return engName;
    }

    private static string ToEnglishName(KeyId value)
    {
        return value switch
        {
            KeyId.None => LocalizationService.Instance.T("Settings.KeyBindings.Editor.Unset"),
            KeyId.Unknown => LocalizationService.Instance.T("Settings.KeyBindings.Editor.Unknown"),
            KeyId.MouseLeftButton => "Mouse LButton",
            KeyId.MouseRightButton => "Mouse RButton",
            KeyId.MouseMiddleButton => "Mouse MButton",
            KeyId.MouseSideButton1 => "Mouse XButton1",
            KeyId.MouseSideButton2 => "Mouse XButton2",
            KeyId.Escape => "Esc",
            KeyId.PageUp => "Page Up",
            KeyId.PageDown => "Page Down",
            KeyId.CapsLock => "Caps Lock",
            KeyId.ScrollLock => "Scroll Lock",
            KeyId.LeftShift => "Left Shift",
            KeyId.RightShift => "Right Shift",
            KeyId.LeftCtrl => "Left Ctrl",
            KeyId.RightCtrl => "Right Ctrl",
            KeyId.LeftAlt => "Left Alt",
            KeyId.RightAlt => "Right Alt",
            KeyId.LeftWin => "Left Win",
            KeyId.RightWin => "Right Win",
            KeyId.Apps => "Menu",
            KeyId.Left => "←",
            KeyId.Up => "↑",
            KeyId.Right => "→",
            KeyId.Down => "↓",
            KeyId.D0 => "0",
            KeyId.D1 => "1",
            KeyId.D2 => "2",
            KeyId.D3 => "3",
            KeyId.D4 => "4",
            KeyId.D5 => "5",
            KeyId.D6 => "6",
            KeyId.D7 => "7",
            KeyId.D8 => "8",
            KeyId.D9 => "9",
            KeyId.Apostrophe => "'",
            KeyId.Comma => ",",
            KeyId.Minus => "-",
            KeyId.Equal => "=",
            KeyId.Period => ".",
            KeyId.Slash => "?",
            KeyId.Backslash => @"\\",
            KeyId.Semicolon => ";",
            KeyId.LeftSquareBracket => "[",
            KeyId.RightSquareBracket => "]",
            KeyId.Tilde => "`",
            KeyId.NumLock => "Num Lock",
            KeyId.NumPad0 => "Num 0",
            KeyId.NumPad1 => "Num 1",
            KeyId.NumPad2 => "Num 2",
            KeyId.NumPad3 => "Num 3",
            KeyId.NumPad4 => "Num 4",
            KeyId.NumPad5 => "Num 5",
            KeyId.NumPad6 => "Num 6",
            KeyId.NumPad7 => "Num 7",
            KeyId.NumPad8 => "Num 8",
            KeyId.NumPad9 => "Num 9",
            KeyId.Decimal => "Num .",
            KeyId.Divide => "Num /",
            KeyId.Multiply => "Num *",
            KeyId.Subtract => "Num -",
            KeyId.Add => "Num +",
            KeyId.NumEnter => "Num Enter",
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// 将KeyId转换为VK
    /// </summary>
    public static VK ToVK(this KeyId value)
    {
        return value switch
        {
            KeyId.None => throw new ArgumentOutOfRangeException(nameof(value), "未指定按键，无法转换为VK。"),
            KeyId.Unknown => throw new ArgumentOutOfRangeException(nameof(value), "未知按键，无法转换为VK。"),
            _ => (VK)value,
        };
    }

    /// <summary>
    /// 将KeyId转换为System.Windows.Input.Key
    /// </summary>
    public static Key ToInputKey(this KeyId value)
    {
        try
        {
            return Enum.Parse<Key>(value.ToString());
        }
        catch
        {
            return value switch
            {
                KeyId.LeftWin => Key.LWin,
                KeyId.RightWin => Key.RWin,
                KeyId.Apostrophe => Key.Oem7,
                KeyId.Comma => Key.OemComma,
                KeyId.Minus => Key.OemMinus,
                KeyId.Equal => Key.OemPlus,
                KeyId.Period => Key.OemPeriod,
                KeyId.Slash => Key.OemQuestion,
                KeyId.Semicolon => Key.Oem1,
                KeyId.LeftSquareBracket => Key.Oem4,
                KeyId.Backslash => Key.Oem5,
                KeyId.RightSquareBracket => Key.Oem6,
                KeyId.Tilde => Key.Oem3,
                KeyId.Enter => Key.Enter,
                KeyId.ScrollLock => Key.Scroll,
                KeyId.PageUp => Key.Prior,
                KeyId.PageDown => Key.Next,
                KeyId.Backspace => Key.Back,
                KeyId.CapsLock => Key.Capital,
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            };
        }
    }

    /// <summary>
    /// 将MouseButton转换为KeyId
    /// </summary>
    public static KeyId FromMouseButton(MouseButton value)
    {
        return value switch
        {
            MouseButton.Left => KeyId.MouseLeftButton,
            MouseButton.Right => KeyId.MouseRightButton,
            MouseButton.Middle => KeyId.MouseMiddleButton,
            MouseButton.XButton1 => KeyId.MouseSideButton1,
            MouseButton.XButton2 => KeyId.MouseSideButton2,
            _ => KeyId.Unknown,
        };
    }

    /// <summary>
    /// 将VK转换为KeyId
    /// </summary>
    public static KeyId FromVK(VK value)
    {
        return string.IsNullOrEmpty(Enum.GetName(typeof(KeyId), value)) ? KeyId.Unknown : (KeyId)value;
    }

    /// <summary>
    /// 将System.Windows.Input.Key转换为KeyId
    /// </summary>
    public static KeyId FromInputKey(Key value)
    {
        try
        {
            return Enum.Parse<KeyId>(value.ToString());
        }
        catch
        {
            if (value == Key.OemQuestion)
            {
                return KeyId.Slash;
            }

            return value switch
            {
                Key.LWin => KeyId.LeftWin,
                Key.RWin => KeyId.RightWin,
                Key.Oem7 => KeyId.Apostrophe,
                Key.OemComma => KeyId.Comma,
                Key.OemMinus => KeyId.Minus,
                Key.OemPlus => KeyId.Equal,
                Key.OemPeriod => KeyId.Period,
                Key.Oem2 => KeyId.Slash,
                Key.Oem1 => KeyId.Semicolon,
                Key.Oem4 => KeyId.LeftSquareBracket,
                Key.Oem5 => KeyId.Backslash,
                Key.Oem6 => KeyId.RightSquareBracket,
                Key.Oem3 => KeyId.Tilde,
                Key.Enter => KeyId.Enter,
                Key.Scroll => KeyId.ScrollLock,
                Key.Prior => KeyId.PageUp,
                Key.Next => KeyId.PageDown,
                Key.Back => KeyId.Backspace,
                Key.Capital => KeyId.CapsLock,
                _ => KeyId.Unknown,
            };
        }
    }

    /// <summary>
    /// [实验] 将KeyId转换为WinForm中的Keys（用于兼容按键连发功能）
    /// </summary>
    public static Keys ToWinFormKeys(this KeyId value)
    {
        try
        {
            return Enum.Parse<Keys>(value.ToInputKey().ToString());
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 将KeyId转换为字符串，保留按键显示与实际键值一致。
    /// </summary>
    public static string ToDisplayText(this HotkeyBinding value)
    {
        return value.DisplayName;
    }
}

/// <summary>
/// 用于与VK/Windows.Input.Key解耦，值与VK对应，但是仅包含美式键盘(104键，不包括媒体控制等)和鼠标中的按键
/// </summary>
public enum KeyId
{
    /// <summary>无</summary>
    None = 0x00,

    /// <summary>未知按键</summary>
    Unknown = 0xFF,

    #region 鼠标按键

    /// <summary>鼠标左键</summary>
    MouseLeftButton = 0x01,
    /// <summary>鼠标右键</summary>
    MouseRightButton = 0x02,
    /// <summary>鼠标中键（滚轮）</summary>
    MouseMiddleButton = 0x04,
    /// <summary>鼠标侧键1（后退）</summary>
    MouseSideButton1 = 0x05,
    /// <summary>鼠标侧键2（前进）</summary>
    MouseSideButton2 = 0x06,

    #endregion

    #region F键区

    /// <summary>F1</summary>
    F1 = 0x70,
    /// <summary>F2</summary>
    F2 = 0x71,
    /// <summary>F3</summary>
    F3 = 0x72,
    /// <summary>F4</summary>
    F4 = 0x73,
    /// <summary>F5</summary>
    F5 = 0x74,
    /// <summary>F6</summary>
    F6 = 0x75,
    /// <summary>F7</summary>
    F7 = 0x76,
    /// <summary>F8</summary>
    F8 = 0x77,
    /// <summary>F9</summary>
    F9 = 0x78,
    /// <summary>F10</summary>
    F10 = 0x79,
    /// <summary>F11</summary>
    F11 = 0x7A,
    /// <summary>F12</summary>
    F12 = 0x7B,

    #endregion

    #region 控制&功能键

    /// <summary>Esc</summary>
    Escape = 0x1B,
    /// <summary>PrintScreen</summary>
    PrintScreen = 0x2C,
    /// <summary>ScrollLock</summary>
    ScrollLock = 0x91,
    /// <summary>Pause</summary>
    Pause = 0x13,
    /// <summary>Insert</summary>
    Insert = 0x2D,
    /// <summary>Delete</summary>
    Delete = 0x2E,
    /// <summary>Home</summary>
    Home = 0x24,
    /// <summary>End</summary>
    End = 0x23,
    /// <summary>Page Up</summary>
    PageUp = 0x21,
    /// <summary>Page Down</summary>
    PageDown = 0x22,
    /// <summary>Backspace退格</summary>
    Backspace = 0x08,
    /// <summary>Tab</summary>
    Tab = 0x09,
    /// <summary>Caps Lock大写锁定</summary>
    CapsLock = 0x14,
    /// <summary>Enter回车</summary>
    Enter = 0x0D,
    /// <summary>左Shift</summary>
    LeftShift = 0xA0,
    /// <summary>右Shift</summary>
    RightShift = 0xA1,
    /// <summary>左Ctrl</summary>
    LeftCtrl = 0xA2,
    /// <summary>右Ctrl</summary>
    RightCtrl = 0xA3,
    /// <summary>左Alt</summary>
    LeftAlt = 0xA4,
    /// <summary>右Alt</summary>
    RightAlt = 0xA5,
    /// <summary>左Win键 (Microsoft Natural Keyboard)</summary>
    LeftWin = 0x5B,
    /// <summary>右Win键 (Microsoft Natural Keyboard)</summary>
    RightWin = 0x5C,
    /// <summary>菜单键 (Microsoft Natural Keyboard)</summary>
    Apps = 0x5D,
    /// <summary>Space空格键</summary>
    Space = 0x20,

    #endregion

    #region 方向键

    /// <summary>方向键 ←</summary>
    Left = 0x25,
    /// <summary>方向键 ↑</summary>
    Up = 0x26,
    /// <summary>方向键 →</summary>
    Right = 0x27,
    /// <summary>方向键 ↓</summary>
    Down = 0x28,

    #endregion

    #region 字母区 - 字母

    /// <summary>A</summary>
    A = 0x41,
    /// <summary>B</summary>
    B = 0x42,
    /// <summary>C</summary>
    C = 0x43,
    /// <summary>D</summary>
    D = 0x44,
    /// <summary>E</summary>
    E = 0x45,
    /// <summary>F</summary>
    F = 0x46,
    /// <summary>G</summary>
    G = 0x47,
    /// <summary>H</summary>
    H = 0x48,
    /// <summary>I</summary>
    I = 0x49,
    /// <summary>J</summary>
    J = 0x4A,
    /// <summary>K</summary>
    K = 0x4B,
    /// <summary>L</summary>
    L = 0x4C,
    /// <summary>M</summary>
    M = 0x4D,
    /// <summary>N</summary>
    N = 0x4E,
    /// <summary>O</summary>
    O = 0x4F,
    /// <summary>P</summary>
    P = 0x50,
    /// <summary>Q</summary>
    Q = 0x51,
    /// <summary>R</summary>
    R = 0x52,
    /// <summary>S</summary>
    S = 0x53,
    /// <summary>T</summary>
    T = 0x54,
    /// <summary>U</summary>
    U = 0x55,
    /// <summary>V</summary>
    V = 0x56,
    /// <summary>W</summary>
    W = 0x57,
    /// <summary>X</summary>
    X = 0x58,
    /// <summary>Y</summary>
    Y = 0x59,
    /// <summary>Z</summary>
    Z = 0x5A,

    #endregion

    #region 字母区 - 数字

    /// <summary>0</summary>
    D0 = 0x30,
    /// <summary>1</summary>
    D1 = 0x31,
    /// <summary>2</summary>
    D2 = 0x32,
    /// <summary>3</summary>
    D3 = 0x33,
    /// <summary>4</summary>
    D4 = 0x34,
    /// <summary>5</summary>
    D5 = 0x35,
    /// <summary>6</summary>
    D6 = 0x36,
    /// <summary>7</summary>
    D7 = 0x37,
    /// <summary>8</summary>
    D8 = 0x38,
    /// <summary>9</summary>
    D9 = 0x39,

    #endregion

    #region 字母区 - 符号

    /// <summary>引号 '</summary>
    Apostrophe = 0xDE,
    /// <summary>逗号 ,</summary>
    Comma = 0xBC,
    /// <summary>连接符 -</summary>
    Minus = 0xBD,
    /// <summary>等于号 =</summary>
    Equal = 0xBB,
    /// <summary>句号 .</summary>
    Period = 0xBE,
    /// <summary>斜杠 /</summary>
    Slash = 0xBF,
    /// <summary>反斜杠 \</summary>
    Backslash = 0xE2,
    /// <summary>分号 ;</summary>
    Semicolon = 0xBA,
    /// <summary>左方括号 [</summary>
    LeftSquareBracket = 0xDB,
    /// <summary>右方括号 ]</summary>
    RightSquareBracket = 0xDD,
    /// <summary>波浪号 `</summary>
    Tilde = 0xC0,

    #endregion

    #region 小键盘区

    /// <summary>Num Lock</summary>
    NumLock = 0x90,
    /// <summary>Num 0</summary>
    NumPad0 = 0x60,
    /// <summary>Num 1</summary>
    NumPad1 = 0x61,
    /// <summary>Num 2</summary>
    NumPad2 = 0x62,
    /// <summary>Num 3</summary>
    NumPad3 = 0x63,
    /// <summary>Num 4</summary>
    NumPad4 = 0x64,
    /// <summary>Num 5</summary>
    NumPad5 = 0x65,
    /// <summary>Num 6</summary>
    NumPad6 = 0x66,
    /// <summary>Num 7</summary>
    NumPad7 = 0x67,
    /// <summary>Num 8</summary>
    NumPad8 = 0x68,
    /// <summary>Num 9</summary>
    NumPad9 = 0x69,
    /// <summary>Num .</summary>
    Decimal = 0x6E,
    /// <summary>Num /</summary>
    Divide = 0x6F,
    /// <summary>Num *</summary>
    Multiply = 0x6A,
    /// <summary>Num -</summary>
    Subtract = 0x6D,
    /// <summary>Num +</summary>
    Add = 0x6B,
    /// <summary>Num Enter</summary>
    NumEnter = 0x0E,

    #endregion
}
