using System.Text.Json;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services.Tasks.AutoTasks;
using OpenCvSharp;

namespace BetterBTD.Tests.AutoTasks;

public sealed class GameUiDetectionConfigTests
{
    [Fact]
    public void ConfigService_CreatesDefaultConfigFile_WhenMissing()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "game_ui_detection_rules.json");
            var service = new GameUiDetectionConfigService(configFilePath);

            var config = service.Current;

            Assert.True(File.Exists(configFilePath));
            Assert.NotEmpty(config.Rules);
            Assert.Contains(config.Rules, static rule => rule.State == GameUiStateId.MainMenu);
            Assert.Equal(50, config.DefaultTolerance);

            var json = File.ReadAllText(configFilePath);
            Assert.Contains("main_menu", json);
            Assert.Contains(nameof(GameUiStateId.MainMenu), json);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void RuleEvaluator_SupportsEqualsAndNotEqualsConditions()
    {
        using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
        SetPixel(frame, 100, 100, "#112233");
        SetPixel(frame, 200, 200, "#445566");
        SetPixel(frame, 300, 300, "#000000");

        var config = new GameUiDetectionConfig
        {
            ReferenceWidth = 1920,
            ReferenceHeight = 1080,
            DefaultTolerance = 0,
            Rules =
            [
                new GameUiDetectionRule
                {
                    Key = "test",
                    DisplayName = "Test",
                    State = GameUiStateId.MainMenu,
                    Priority = 1,
                    AllOf =
                    [
                        new GameUiColorCondition { X = 100, Y = 100, ColorHex = "#112233", Operator = GameUiColorComparisonOperator.Equals },
                        new GameUiColorCondition { X = 200, Y = 200, ColorHex = "#445566", Operator = GameUiColorComparisonOperator.Equals },
                        new GameUiColorCondition { X = 300, Y = 300, ColorHex = "#121417", Operator = GameUiColorComparisonOperator.NotEquals }
                    ]
                }
            ]
        };

        var isMatch = GameUiDetectionRuleEvaluator.IsMatch(frame, config, config.Rules[0]);

        Assert.True(isMatch);
    }

    [Fact]
    public void ConfigService_ReloadsCustomConfig()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "game_ui_detection_rules.json");
            var customConfig = new GameUiDetectionConfig
            {
                Version = 1,
                ReferenceWidth = 1920,
                ReferenceHeight = 1080,
                DefaultTolerance = 3,
                Rules =
                [
                    new GameUiDetectionRule
                    {
                        Key = "custom_rule",
                        DisplayName = "自定义规则",
                        State = GameUiStateId.Returnable,
                        Priority = 10,
                        AllOf =
                        [
                            new GameUiColorCondition
                            {
                                X = 68,
                                Y = 54,
                                ColorHex = "#FFFFFF",
                                Operator = GameUiColorComparisonOperator.Equals
                            }
                        ]
                    }
                ]
            };

            var json = JsonSerializer.Serialize(
                customConfig,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            File.WriteAllText(configFilePath, json);

            var service = new GameUiDetectionConfigService(configFilePath);
            var reloaded = service.Reload();

            Assert.Single(reloaded.Rules);
            Assert.Equal("custom_rule", reloaded.Rules[0].Key);
            Assert.Equal(GameUiStateId.Returnable, reloaded.Rules[0].State);
            Assert.Equal(3, reloaded.DefaultTolerance);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ConfigService_MigratesLegacyDefaultToleranceTo50()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "game_ui_detection_rules.json");
            var legacyConfig = new GameUiDetectionConfig
            {
                Version = 1,
                ReferenceWidth = 1920,
                ReferenceHeight = 1080,
                DefaultTolerance = 12,
                Rules = []
            };

            var json = JsonSerializer.Serialize(
                legacyConfig,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            File.WriteAllText(configFilePath, json);

            var service = new GameUiDetectionConfigService(configFilePath);
            var reloaded = service.Reload();

            Assert.Equal(50, reloaded.DefaultTolerance);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "BetterBTD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void SetPixel(Mat frame, int x, int y, string hexColor)
    {
        var normalized = hexColor.TrimStart('#');
        var r = Convert.ToByte(normalized[..2], 16);
        var g = Convert.ToByte(normalized.Substring(2, 2), 16);
        var b = Convert.ToByte(normalized.Substring(4, 2), 16);
        frame.Set(y, x, new Vec3b(b, g, r));
    }
}
