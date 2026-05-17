using BetterBTD.Models.GameElements;

namespace BetterBTD.Models.ScriptEditor;

public static class ScriptDocumentFormat
{
    public const string Schema = "better-btd/script";
    public const int CurrentFormatVersion = 1;
    public const string DefaultScriptVersion = "1.0.0";
}

public static class ScriptDocumentCategories
{
    public const string Collection = "Collection";
    public const string BlackBorder = "BlackBorder";
    public const string Race = "Race";
    public const string Custom = "Custom";

    public static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Collection : value.Trim();
    }
}

public sealed class ScriptDocument
{
    public string Schema { get; set; } = ScriptDocumentFormat.Schema;

    public int FormatVersion { get; set; } = ScriptDocumentFormat.CurrentFormatVersion;

    public ScriptMetadataDocument Metadata { get; set; } = new();

    public List<ScriptMonkeyObjectDocument> MonkeyObjects { get; set; } = [];

    public List<ScriptInstructionDocument> Instructions { get; set; } = [];
}

public sealed class ScriptMetadataDocument
{
    public string ScriptVersion { get; set; } = ScriptDocumentFormat.DefaultScriptVersion;

    public string Category { get; set; } = ScriptDocumentCategories.Collection;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Map { get; set; } = GameMapType.MonkeyMeadow.ToString();

    public string Difficulty { get; set; } = StageDifficulty.Medium.ToString();

    public string Mode { get; set; } = StageMode.Standard.ToString();

    public string Hero { get; set; } = HeroType.Quincy.ToString();
}

public sealed class ScriptMonkeyObjectDocument
{
    public string BindingId { get; set; } = string.Empty;

    public string ObjectId { get; set; } = string.Empty;

    public string SelectionCode { get; set; } = string.Empty;

    public int PlacementOrder { get; set; }
}

public sealed class ScriptInstructionDocument
{
    public string CommandType { get; set; } = string.Empty;

    public string SelectedMonkeyTower { get; set; } = string.Empty;

    public string MonkeyBindingId { get; set; } = string.Empty;

    public string MonkeyObjectId { get; set; } = string.Empty;

    public string TargetMonkeyBindingId { get; set; } = string.Empty;

    public string TargetMonkeyObjectId { get; set; } = string.Empty;

    public string SelectedInventoryItem { get; set; } = string.Empty;

    public string SelectedActivatedAbility { get; set; } = string.Empty;

    public string NextRoundAction { get; set; } = string.Empty;

    public string WaitMode { get; set; } = string.Empty;

    public int ClickCount { get; set; }

    public int ClickIntervalMilliseconds { get; set; }

    public int NextRoundSendCount { get; set; }

    public int? NextRoundOperationIntervalMilliseconds { get; set; }

    public int WaitTimeMilliseconds { get; set; }

    public bool? PlacementDetectionEnabled { get; set; }

    public bool? PlacementFailureAdjustmentEnabled { get; set; }

    public int? PlacementAttemptIntervalMilliseconds { get; set; }

    public int? PlacementAdjustmentAttemptIntervalMilliseconds { get; set; }

    public bool? UpgradeDetectionEnabled { get; set; }

    public int? UpgradeOperationIntervalMilliseconds { get; set; }

    public bool? MonkeyPanelDetectionEnabled { get; set; }

    public int? MonkeyPanelOperationIntervalMilliseconds { get; set; }

    public bool? SellDetectionEnabled { get; set; }

    public int WaitGoldAmount { get; set; }

    public int WaitRoundCount { get; set; }

    public double PositionX { get; set; }

    public double PositionY { get; set; }

    public double WaitColorCoordinateX { get; set; }

    public double WaitColorCoordinateY { get; set; }

    public string UpgradePath { get; set; } = string.Empty;

    public int UpgradeCount { get; set; }

    public string SwitchDirection { get; set; } = string.Empty;

    public int SwitchCount { get; set; }

    public string SelectedAbility { get; set; } = string.Empty;

    public bool RequiresAbilityCoordinate { get; set; }

    public double AbilityCoordinateX { get; set; }

    public double AbilityCoordinateY { get; set; }

    public string WaitColorHex { get; set; } = string.Empty;

    public int WaitColorTolerance { get; set; }

    public string CommentContent { get; set; } = string.Empty;

    public int IntervalToNextInstructionMs { get; set; }

    public string Notes { get; set; } = string.Empty;
}
