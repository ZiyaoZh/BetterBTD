using System;
using BetterBTD.Models.GameElements;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterBTD.Models.ScriptEditor;

public enum ScriptCommandType
{
    PlaceMonkey,
    UpgradeMonkey,
    SwitchMonkeyTarget,
    SetMonkeyAbility,
    SellMonkey,
    PlaceHeroInventory,
    ActivateAbility,
    MouseClick,
    NextRound,
    Wait,
    ModifyMonkeyCoordinate,
    Comment
}

public static class ScriptCommandTypeExtensions
{
    public static bool IsExecutable(this ScriptCommandType type)
    {
        return type is not ScriptCommandType.Comment;
    }
}

public enum UpgradePathType
{
    Top,
    Middle,
    Bottom
}

public enum SwitchDirectionType
{
    Right,
    Left
}

public enum MonkeyAbilityType
{
    Ability1,
    Ability2
}

public enum WaitModeType
{
    Time,
    Gold,
    Round,
    CoordinateColor
}

public sealed class ScriptInstructionTemplate : ObservableObject
{
    private string _displayName = string.Empty;
    private string _description = string.Empty;

    public required ScriptCommandType Type { get; init; }

    public required string NameKey { get; init; }

    public required string DescriptionKey { get; init; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsExecutable => Type.IsExecutable();
}

public sealed class ScriptInstructionInstance : ObservableObject
{
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private string _selectedMonkeyTower = "Tower:DartMonkey";
    private string _monkeyBindingId = string.Empty;
    private string _monkeyObjectId = string.Empty;
    private string _targetMonkeyBindingId = string.Empty;
    private string _targetMonkeyObjectId = string.Empty;
    private string _selectedInventoryItem = string.Empty;
    private string _selectedActivatedAbility = string.Empty;
    private string _nextRoundAction = "PlayFastForward";
    private string _waitMode = WaitModeType.Time.ToString();
    private int _clickCount = 1;
    private int _clickIntervalMilliseconds = 80;
    private int _nextRoundSendCount = 1;
    private int _nextRoundOperationIntervalMilliseconds = 200;
    private int _waitTimeMilliseconds = 1000;
    private bool _placementDetectionEnabled = true;
    private bool _placementFailureAdjustmentEnabled = true;
    private int _placementAttemptIntervalMilliseconds = 200;
    private int _placementAdjustmentAttemptIntervalMilliseconds = 200;
    private bool _upgradeDetectionEnabled = true;
    private int _upgradeOperationIntervalMilliseconds = 200;
    private bool _monkeyPanelDetectionEnabled = true;
    private int _monkeyPanelOperationIntervalMilliseconds = 200;
    private bool _sellDetectionEnabled = true;
    private int _waitGoldAmount;
    private int _waitRoundCount = 1;
    private double _positionX;
    private double _positionY;
    private double _waitColorCoordinateX;
    private double _waitColorCoordinateY;
    private UpgradePathType _upgradePath = UpgradePathType.Top;
    private int _upgradeCount = 1;
    private SwitchDirectionType _switchDirection = SwitchDirectionType.Left;
    private int _switchCount = 1;
    private MonkeyAbilityType _selectedAbility = MonkeyAbilityType.Ability1;
    private bool _requiresAbilityCoordinate;
    private double _abilityCoordinateX;
    private double _abilityCoordinateY;
    private string _waitColorHex = "#FFFFFF";
    private int _waitColorTolerance;
    private string _commentContent = string.Empty;
    private int _intervalToNextInstructionMs = 100;
    private string _notes = string.Empty;

    public ScriptInstructionInstance(ScriptCommandType type, string nameKey, string descriptionKey)
    {
        Type = type;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
    }

    public ScriptCommandType Type { get; }

    public string NameKey { get; }

    public string DescriptionKey { get; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsExecutable => Type.IsExecutable();

    public string SelectedMonkeyTower
    {
        get => _selectedMonkeyTower;
        set => SetProperty(ref _selectedMonkeyTower, value);
    }

    public string MonkeyBindingId
    {
        get => _monkeyBindingId;
        set => SetProperty(ref _monkeyBindingId, value);
    }

    public string MonkeyObjectId
    {
        get => _monkeyObjectId;
        set => SetProperty(ref _monkeyObjectId, value);
    }

    public string TargetMonkeyBindingId
    {
        get => _targetMonkeyBindingId;
        set => SetProperty(ref _targetMonkeyBindingId, value);
    }

    public string TargetMonkeyObjectId
    {
        get => _targetMonkeyObjectId;
        set
        {
            if (!SetProperty(ref _targetMonkeyObjectId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowUpgradePathSelector));
        }
    }

    public string SelectedInventoryItem
    {
        get => _selectedInventoryItem;
        set => SetProperty(ref _selectedInventoryItem, value);
    }

    public string SelectedActivatedAbility
    {
        get => _selectedActivatedAbility;
        set => SetProperty(ref _selectedActivatedAbility, value);
    }

    public string NextRoundAction
    {
        get => _nextRoundAction;
        set
        {
            if (!SetProperty(ref _nextRoundAction, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowNextRoundSendCount));
        }
    }

    public int ClickCount
    {
        get => _clickCount;
        set => SetProperty(ref _clickCount, value < 1 ? 1 : (value > 1000 ? 1000 : value));
    }

    public int ClickIntervalMilliseconds
    {
        get => _clickIntervalMilliseconds;
        set => SetProperty(ref _clickIntervalMilliseconds, value < 0 ? 0 : value);
    }

    public bool ShowNextRoundSendCount =>
        Type == ScriptCommandType.NextRound && string.Equals(NextRoundAction, "SendNextRound", StringComparison.OrdinalIgnoreCase);

    public int NextRoundSendCount
    {
        get => _nextRoundSendCount;
        set => SetProperty(ref _nextRoundSendCount, value < 1 ? 1 : (value > 500 ? 500 : value));
    }

    public int NextRoundOperationIntervalMilliseconds
    {
        get => _nextRoundOperationIntervalMilliseconds;
        set => SetProperty(ref _nextRoundOperationIntervalMilliseconds, value < 0 ? 0 : value);
    }

    public string WaitMode
    {
        get => _waitMode;
        set
        {
            if (!SetProperty(ref _waitMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowWaitTimeMilliseconds));
            OnPropertyChanged(nameof(ShowWaitGoldAmount));
            OnPropertyChanged(nameof(ShowWaitRoundCount));
            OnPropertyChanged(nameof(ShowWaitCoordinateColor));
        }
    }

    public bool ShowWaitTimeMilliseconds =>
        Type == ScriptCommandType.Wait && string.Equals(WaitMode, WaitModeType.Time.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool ShowWaitGoldAmount =>
        Type == ScriptCommandType.Wait && string.Equals(WaitMode, WaitModeType.Gold.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool ShowWaitRoundCount =>
        Type == ScriptCommandType.Wait && string.Equals(WaitMode, WaitModeType.Round.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool ShowWaitCoordinateColor =>
        Type == ScriptCommandType.Wait && string.Equals(WaitMode, WaitModeType.CoordinateColor.ToString(), StringComparison.OrdinalIgnoreCase);

    public int WaitTimeMilliseconds
    {
        get => _waitTimeMilliseconds;
        set => SetProperty(ref _waitTimeMilliseconds, value < 0 ? 0 : value);
    }

    public bool PlacementDetectionEnabled
    {
        get => _placementDetectionEnabled;
        set => SetProperty(ref _placementDetectionEnabled, value);
    }

    public bool PlacementFailureAdjustmentEnabled
    {
        get => _placementFailureAdjustmentEnabled;
        set => SetProperty(ref _placementFailureAdjustmentEnabled, value);
    }

    public int PlacementAttemptIntervalMilliseconds
    {
        get => _placementAttemptIntervalMilliseconds;
        set => SetProperty(ref _placementAttemptIntervalMilliseconds, value < 0 ? 0 : value);
    }

    public int PlacementAdjustmentAttemptIntervalMilliseconds
    {
        get => _placementAdjustmentAttemptIntervalMilliseconds;
        set => SetProperty(ref _placementAdjustmentAttemptIntervalMilliseconds, value < 0 ? 0 : value);
    }

    public bool UpgradeDetectionEnabled
    {
        get => _upgradeDetectionEnabled;
        set => SetProperty(ref _upgradeDetectionEnabled, value);
    }

    public int UpgradeOperationIntervalMilliseconds
    {
        get => _upgradeOperationIntervalMilliseconds;
        set => SetProperty(ref _upgradeOperationIntervalMilliseconds, value < 0 ? 0 : value);
    }

    public bool MonkeyPanelDetectionEnabled
    {
        get => _monkeyPanelDetectionEnabled;
        set => SetProperty(ref _monkeyPanelDetectionEnabled, value);
    }

    public int MonkeyPanelOperationIntervalMilliseconds
    {
        get => _monkeyPanelOperationIntervalMilliseconds;
        set => SetProperty(ref _monkeyPanelOperationIntervalMilliseconds, value < 0 ? 0 : value);
    }

    public bool SellDetectionEnabled
    {
        get => _sellDetectionEnabled;
        set => SetProperty(ref _sellDetectionEnabled, value);
    }

    public int WaitGoldAmount
    {
        get => _waitGoldAmount;
        set => SetProperty(ref _waitGoldAmount, value < 0 ? 0 : value);
    }

    public int WaitRoundCount
    {
        get => _waitRoundCount;
        set => SetProperty(ref _waitRoundCount, value < 1 ? 1 : value);
    }

    public bool ShowUpgradePathSelector =>
        Type == ScriptCommandType.UpgradeMonkey && !TargetMonkeyObjectId.StartsWith("Hero:", StringComparison.OrdinalIgnoreCase);

    public double PositionX
    {
        get => _positionX;
        set => SetProperty(ref _positionX, value);
    }

    public double PositionY
    {
        get => _positionY;
        set => SetProperty(ref _positionY, value);
    }

    public double WaitColorCoordinateX
    {
        get => _waitColorCoordinateX;
        set => SetProperty(ref _waitColorCoordinateX, value);
    }

    public double WaitColorCoordinateY
    {
        get => _waitColorCoordinateY;
        set => SetProperty(ref _waitColorCoordinateY, value);
    }

    public UpgradePathType UpgradePath
    {
        get => _upgradePath;
        set => SetProperty(ref _upgradePath, value);
    }

    public int UpgradeCount
    {
        get => _upgradeCount;
        set => SetProperty(ref _upgradeCount, value < 1 ? 1 : (value > 5 ? 5 : value));
    }

    public SwitchDirectionType SwitchDirection
    {
        get => _switchDirection;
        set => SetProperty(ref _switchDirection, value);
    }

    public int SwitchCount
    {
        get => _switchCount;
        set => SetProperty(ref _switchCount, value < 1 ? 1 : (value > 3 ? 3 : value));
    }

    public MonkeyAbilityType SelectedAbility
    {
        get => _selectedAbility;
        set => SetProperty(ref _selectedAbility, value);
    }

    public bool RequiresAbilityCoordinate
    {
        get => _requiresAbilityCoordinate;
        set
        {
            if (!SetProperty(ref _requiresAbilityCoordinate, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowAbilityCoordinateInputs));
            OnPropertyChanged(nameof(ShowPlacementCoordinateInputs));
        }
    }

    public bool ShowAbilityCoordinateInputs =>
        RequiresAbilityCoordinate && (Type == ScriptCommandType.SetMonkeyAbility || Type == ScriptCommandType.ActivateAbility);

    public bool ShowPlacementCoordinateInputs =>
        Type == ScriptCommandType.PlaceHeroInventory && RequiresAbilityCoordinate;

    public double AbilityCoordinateX
    {
        get => _abilityCoordinateX;
        set => SetProperty(ref _abilityCoordinateX, value);
    }

    public double AbilityCoordinateY
    {
        get => _abilityCoordinateY;
        set => SetProperty(ref _abilityCoordinateY, value);
    }

    public string WaitColorHex
    {
        get => _waitColorHex;
        set => SetProperty(ref _waitColorHex, NormalizeWaitColorHex(value));
    }

    public int WaitColorTolerance
    {
        get => _waitColorTolerance;
        set => SetProperty(ref _waitColorTolerance, value < 0 ? 0 : (value > 255 ? 255 : value));
    }

    public string CommentContent
    {
        get => _commentContent;
        set => SetProperty(ref _commentContent, value);
    }

    public int IntervalToNextInstructionMs
    {
        get => _intervalToNextInstructionMs;
        set => SetProperty(ref _intervalToNextInstructionMs, value < 0 ? 0 : value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    private static string NormalizeWaitColorHex(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.StartsWith('#') ? text : $"#{text}";
    }
}
