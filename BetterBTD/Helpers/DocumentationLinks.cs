namespace BetterBTD.Helpers;

public static class DocumentationLinks
{
    public const string Home = "https://ziyaozh.github.io/BetterBTD/";

    public static string ForAutoTask(string taskKey)
    {
        var section = taskKey.ToLowerInvariant() switch
        {
            "collection" => "collection",
            "goldballoon" => "golden-bloon",
            "blackborder" => "black-border",
            "loopstage" => "farming",
            "race" => "race",
            "odyssey" => "odyssey",
            "robot" => "robot-control",
            _ => "auto-tasks"
        };

        return $"{Home}#{section}";
    }
}
