using System.Text.Json.Serialization;

namespace BetterBTD.Models.AutoTasks;

public enum GameUiColorComparisonOperator
{
    Equals,
    NotEquals
}

public sealed class GameUiDetectionConfig
{
    public int Version { get; set; } = 1;

    public int ReferenceWidth { get; set; } = 1920;

    public int ReferenceHeight { get; set; } = 1080;

    public int DefaultTolerance { get; set; } = 50;

    public List<GameUiDetectionRule> Rules { get; set; } = [];
}

public sealed class GameUiDetectionRule
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameUiStateId State { get; set; } = GameUiStateId.Unknown;

    public int Priority { get; set; }

    public bool IsEnabled { get; set; } = true;

    public List<GameUiColorCondition> AllOf { get; set; } = [];
}

public sealed class GameUiColorCondition
{
    public int X { get; set; }

    public int Y { get; set; }

    public string ColorHex { get; set; } = "#000000";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GameUiColorComparisonOperator Operator { get; set; } = GameUiColorComparisonOperator.Equals;

    public int? Tolerance { get; set; }
}
