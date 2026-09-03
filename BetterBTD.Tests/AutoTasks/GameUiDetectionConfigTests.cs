using System.Text.Json;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Services.ChildSession;
using BetterBTD.Services.Tasks.AutoTasks;
using OpenCvSharp;

namespace BetterBTD.Tests.AutoTasks;

public sealed class GameUiDetectionConfigTests
{
    [Fact]
    public void GameUiStateId_DoesNotRetainMapSearchResultsValue()
    {
        Assert.False(Enum.IsDefined(typeof(GameUiStateId), 5));
        Assert.Equal(6, (int)GameUiStateId.MapGrid);
    }

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
            var mapSearchRule = Assert.Single(config.Rules, static rule => rule.State == GameUiStateId.MapSearch);
            Assert.Equal("map_search", mapSearchRule.Key);
            Assert.Equal(680, mapSearchRule.Priority);
            Assert.Collection(
                mapSearchRule.AllOf,
                condition => AssertCondition(condition, 983, 48, "#385373"),
                condition => AssertCondition(condition, 778, 43, "#578CD4"));
            Assert.DoesNotContain(config.Rules, static rule => rule.Key == "map_search_results");
            var freeplayPromptRule = Assert.Single(config.Rules, static rule => rule.State == GameUiStateId.FreeplayPrompt);
            var inLevelRule = Assert.Single(config.Rules, static rule => rule.State == GameUiStateId.InLevel);
            Assert.True(freeplayPromptRule.Priority > inLevelRule.Priority);
            var networkUnavailableRule = Assert.Single(
                config.Rules,
                static rule => rule.State == GameUiStateId.NetworkUnavailableDialog);
            Assert.Equal("network_unavailable_dialog", networkUnavailableRule.Key);
            Assert.True(networkUnavailableRule.Priority > config.Rules
                .Where(static rule => rule.State != GameUiStateId.NetworkUnavailableDialog)
                .Max(static rule => rule.Priority));
            Assert.Collection(
                networkUnavailableRule.AllOf,
                condition => AssertCondition(condition, 610, 405, "#71E800"),
                condition => AssertCondition(condition, 1370, 405, "#71E800"),
                condition => AssertCondition(condition, 690, 730, "#FFD600"),
                condition => AssertCondition(condition, 1040, 730, "#69E500"));

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
    public void RuleEvaluator_ReadPixelAtReference_ScalesCoordinatesAndUsesConfigTolerance()
    {
        using var frame = new Mat(540, 960, MatType.CV_8UC3, Scalar.All(0));
        SetPixel(frame, 481, 418, "#123456");
        var config = new GameUiDetectionConfig
        {
            ReferenceWidth = 1920,
            ReferenceHeight = 1080,
            DefaultTolerance = 37
        };

        var sample = GameUiDetectionRuleEvaluator.ReadPixelAtReference(frame, config, 962, 837);

        Assert.Equal(new GameUiPixelSample(0x12, 0x34, 0x56, 37), sample);
    }

    [Fact]
    public void ConfigService_ChildSessionReadsDefaultsWithoutCreatingOrUpdatingFile()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "nested", "game_ui_detection_rules.json");
            ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
                BetterBtdInstanceRole.ChildSession,
                42,
                "pipe-name"));
            var service = new GameUiDetectionConfigService(configFilePath);

            var config = service.Current;

            Assert.NotEmpty(config.Rules);
            Assert.False(File.Exists(configFilePath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(configFilePath)));
        }
        finally
        {
            ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
                BetterBtdInstanceRole.Primary,
                null,
                null));
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void DefaultFreeplayPromptRule_MatchesObservedPromptPixels()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var service = new GameUiDetectionConfigService(
                Path.Combine(tempDirectory, "game_ui_detection_rules.json"));
            var config = service.Current;
            var rule = Assert.Single(config.Rules, static item => item.State == GameUiStateId.FreeplayPrompt);

            using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
            SetPixel(frame, 1910, 40, "#AA7B45");
            SetPixel(frame, 13, 40, "#AA7C46");
            SetPixel(frame, 910, 203, "#009FDD");
            SetPixel(frame, 1036, 758, "#62E200");
            SetPixel(frame, 743, 384, "#F34F13");

            Assert.True(GameUiDetectionRuleEvaluator.IsMatch(frame, config, rule));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void DefaultStageChallengeWithHintRules_MatchBothObservedLayouts()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var service = new GameUiDetectionConfigService(
                Path.Combine(tempDirectory, "game_ui_detection_rules.json"));
            var config = service.Current;
            var rules = config.Rules
                .Where(static item => item.State == GameUiStateId.StageChallengeWithHint)
                .ToArray();

            Assert.Equal(2, rules.Length);

            using var firstFrame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
            SetPixel(firstFrame, 780, 380, "#F34A12");
            SetPixel(firstFrame, 780, 760, "#5388D2");
            SetPixel(firstFrame, 900, 760, "#62E200");
            Assert.True(GameUiDetectionRuleEvaluator.IsMatch(firstFrame, config, rules[0]));

            using var secondFrame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
            SetPixel(secondFrame, 820, 330, "#F24710");
            SetPixel(secondFrame, 820, 760, "#D2D2D2");
            SetPixel(secondFrame, 960, 760, "#FFFFFF");
            Assert.True(GameUiDetectionRuleEvaluator.IsMatch(secondFrame, config, rules[1]));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void DefaultNetworkUnavailableDialogRule_MatchesConfiguredPixels()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var service = new GameUiDetectionConfigService(
                Path.Combine(tempDirectory, "game_ui_detection_rules.json"));
            var config = service.Current;
            var rule = Assert.Single(
                config.Rules,
                static item => item.State == GameUiStateId.NetworkUnavailableDialog);

            using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
            SetPixel(frame, 610, 405, "#71E800");
            SetPixel(frame, 1370, 405, "#71E800");
            SetPixel(frame, 690, 730, "#FFD600");
            SetPixel(frame, 1040, 730, "#69E500");

            Assert.True(GameUiDetectionRuleEvaluator.IsMatch(frame, config, rule));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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

            var customRule = Assert.Single(reloaded.Rules, rule => rule.Key == "custom_rule");
            Assert.Equal(GameUiStateId.Returnable, customRule.State);
            Assert.Equal(3, reloaded.DefaultTolerance);
            Assert.Contains(reloaded.Rules, rule => rule.State == GameUiStateId.MainMenu);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ConfigService_SyncsBuiltInRules_WhenExistingConfigHasOldRuleDefinition()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "game_ui_detection_rules.json");
            var oldConfig = new GameUiDetectionConfig
            {
                Version = 1,
                ReferenceWidth = 1920,
                ReferenceHeight = 1080,
                DefaultTolerance = 50,
                Rules =
                [
                    new GameUiDetectionRule
                    {
                        Key = "main_menu",
                        DisplayName = "Old Main Menu",
                        State = GameUiStateId.Returnable,
                        Priority = 1,
                        IsEnabled = false,
                        AllOf =
                        [
                            new GameUiColorCondition
                            {
                                X = 1,
                                Y = 2,
                                ColorHex = "#010203",
                                Operator = GameUiColorComparisonOperator.NotEquals
                            }
                        ]
                    }
                ]
            };

            File.WriteAllText(configFilePath, SerializeConfig(oldConfig));

            var service = new GameUiDetectionConfigService(configFilePath);
            var reloaded = service.Reload();

            var mainMenuRule = Assert.Single(reloaded.Rules, rule => rule.Key == "main_menu");
            Assert.Equal(GameUiStateId.MainMenu, mainMenuRule.State);
            Assert.Equal(700, mainMenuRule.Priority);
            Assert.False(mainMenuRule.IsEnabled);
            Assert.Contains(mainMenuRule.AllOf, static condition =>
                condition.X == 966 &&
                condition.Y == 945 &&
                condition.ColorHex == "#FFFFFF" &&
                condition.Operator == GameUiColorComparisonOperator.Equals);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ConfigService_SyncsNewBuiltInRuleKeys_AndPreservesCustomRules()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "game_ui_detection_rules.json");
            var oldConfig = new GameUiDetectionConfig
            {
                Version = 2,
                ReferenceWidth = 1920,
                ReferenceHeight = 1080,
                DefaultTolerance = 50,
                Rules =
                [
                    new GameUiDetectionRule
                    {
                        Key = "custom_rule",
                        DisplayName = "Custom",
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

            File.WriteAllText(configFilePath, SerializeConfig(oldConfig));

            var service = new GameUiDetectionConfigService(configFilePath);
            var reloaded = service.Reload();

            Assert.Contains(reloaded.Rules, static rule => rule.Key == "stage_settings");
            Assert.Contains(reloaded.Rules, static rule => rule.Key == "stage_settings_alt");
            Assert.Single(reloaded.Rules, static rule => rule.Key == "custom_rule");
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

    [Fact]
    public void ConfigService_RemovesRetiredMapSearchResultsRules_AndPreservesOtherRules()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var configFilePath = Path.Combine(tempDirectory, "game_ui_detection_rules.json");
            File.WriteAllText(
                configFilePath,
                """
                {
                  "Version": 4,
                  "ReferenceWidth": 1920,
                  "ReferenceHeight": 1080,
                  "DefaultTolerance": 50,
                  "Rules": [
                    {
                      "Key": "map_search_results",
                      "DisplayName": "Retired",
                      "State": "MapSearchResults",
                      "Priority": 690,
                      "IsEnabled": true,
                      "AllOf": []
                    },
                    {
                      "Key": "retired_by_numeric_state",
                      "DisplayName": "Retired numeric",
                      "State": 5,
                      "Priority": 1,
                      "IsEnabled": true,
                      "AllOf": []
                    },
                    {
                      "Key": "custom_map_grid",
                      "DisplayName": "Custom map grid",
                      "State": 6,
                      "Priority": 10,
                      "IsEnabled": true,
                      "AllOf": [
                        {
                          "X": 1,
                          "Y": 2,
                          "ColorHex": "#010203",
                          "Operator": "Equals"
                        }
                      ]
                    }
                  ]
                }
                """);

            var service = new GameUiDetectionConfigService(configFilePath);
            var reloaded = service.Reload();

            Assert.Equal(6, reloaded.Version);
            Assert.DoesNotContain(reloaded.Rules, static rule => rule.Key == "map_search_results");
            Assert.DoesNotContain(reloaded.Rules, static rule => rule.Key == "retired_by_numeric_state");
            var customRule = Assert.Single(reloaded.Rules, static rule => rule.Key == "custom_map_grid");
            Assert.Equal(GameUiStateId.MapGrid, customRule.State);

            var migratedJson = File.ReadAllText(configFilePath);
            Assert.DoesNotContain("MapSearchResults", migratedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("map_search_results", migratedJson, StringComparison.OrdinalIgnoreCase);
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

    private static void AssertCondition(
        GameUiColorCondition condition,
        int x,
        int y,
        string colorHex)
    {
        Assert.Equal(x, condition.X);
        Assert.Equal(y, condition.Y);
        Assert.Equal(colorHex, condition.ColorHex);
        Assert.Equal(GameUiColorComparisonOperator.Equals, condition.Operator);
    }

    private static string SerializeConfig(GameUiDetectionConfig config)
    {
        return JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
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
