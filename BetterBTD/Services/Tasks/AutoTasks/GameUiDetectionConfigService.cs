using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBTD.Models.AutoTasks;

namespace BetterBTD.Services.Tasks.AutoTasks;

public sealed class GameUiDetectionConfigService
{
    private static readonly Lazy<GameUiDetectionConfigService> InstanceHolder = new(() => new GameUiDetectionConfigService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _syncRoot = new();
    private readonly string _configFilePath;

    private GameUiDetectionConfig? _current;

    private GameUiDetectionConfigService()
        : this(CreateDefaultConfigFilePath())
    {
    }

    internal GameUiDetectionConfigService(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        _configFilePath = configFilePath;
    }

    public static GameUiDetectionConfigService Instance => InstanceHolder.Value;

    public string ConfigFilePath => _configFilePath;

    public GameUiDetectionConfig Current
    {
        get
        {
            lock (_syncRoot)
            {
                _current ??= LoadOrCreate();
                return Clone(_current);
            }
        }
    }

    public GameUiDetectionConfig Reload()
    {
        lock (_syncRoot)
        {
            _current = LoadOrCreate();
            return Clone(_current);
        }
    }

    public void Save(GameUiDetectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var normalized = Normalize(config);
        var directoryPath = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(_configFilePath, json);

        lock (_syncRoot)
        {
            _current = Clone(normalized);
        }
    }

    private GameUiDetectionConfig LoadOrCreate()
    {
        var directoryPath = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (!File.Exists(_configFilePath))
        {
            var defaultConfig = CreateDefaultConfig();
            Persist(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<GameUiDetectionConfig>(json, JsonOptions);
            if (config is null)
            {
                throw new JsonException("Deserialized config is null.");
            }

            var normalized = Normalize(config);
            Persist(normalized);
            return normalized;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Debug.WriteLine($"Load game UI detection config failed: {ex.Message}");
            var fallbackConfig = CreateDefaultConfig();
            Persist(fallbackConfig);
            return fallbackConfig;
        }
    }

    private void Persist(GameUiDetectionConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configFilePath, json);
    }

    private static string CreateDefaultConfigFilePath()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterBTD",
            "AutoTasks");
        return Path.Combine(appDataDirectory, "game_ui_detection_rules.json");
    }

    private static GameUiDetectionConfig Normalize(GameUiDetectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var normalized = new GameUiDetectionConfig
        {
            Version = Math.Max(1, config.Version),
            ReferenceWidth = config.ReferenceWidth > 0 ? config.ReferenceWidth : 1920,
            ReferenceHeight = config.ReferenceHeight > 0 ? config.ReferenceHeight : 1080,
            DefaultTolerance = NormalizeDefaultTolerance(config),
            Rules = config.Rules
                .Where(static rule => rule is not null)
                .Select(NormalizeRule)
                .ToList()
        };

        var defaults = CreateDefaultConfig();
        MergeMissingDefaultRules(normalized, defaults);
        normalized.Version = Math.Max(normalized.Version, defaults.Version);
        return normalized;
    }

    private static GameUiDetectionRule NormalizeRule(GameUiDetectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new GameUiDetectionRule
        {
            Key = rule.Key?.Trim() ?? string.Empty,
            DisplayName = rule.DisplayName?.Trim() ?? string.Empty,
            State = rule.State,
            Priority = rule.Priority,
            IsEnabled = rule.IsEnabled,
            AllOf = rule.AllOf
                .Where(static condition => condition is not null)
                .Select(NormalizeCondition)
                .ToList()
        };
    }

    private static GameUiColorCondition NormalizeCondition(GameUiColorCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return new GameUiColorCondition
        {
            X = Math.Max(0, condition.X),
            Y = Math.Max(0, condition.Y),
            ColorHex = NormalizeHex(condition.ColorHex),
            Operator = condition.Operator,
            Tolerance = condition.Tolerance is null ? null : Math.Max(0, condition.Tolerance.Value)
        };
    }

    private static GameUiDetectionConfig Clone(GameUiDetectionConfig config)
    {
        return Normalize(config);
    }

    private static string NormalizeHex(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "#000000";
        }

        return trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : $"#{trimmed.ToUpperInvariant()}";
    }

    private static int NormalizeDefaultTolerance(GameUiDetectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.DefaultTolerance < 0)
        {
            return 50;
        }

        // Migrate the original placeholder default to the new baseline tolerance.
        if (config.Version <= 1 && config.DefaultTolerance == 12)
        {
            return 50;
        }

        return config.DefaultTolerance;
    }

    private static GameUiDetectionConfig CreateDefaultConfig()
    {
        return new GameUiDetectionConfig
        {
            Version = 2,
            ReferenceWidth = 1920,
            ReferenceHeight = 1080,
            DefaultTolerance = 50,
            Rules =
            [
                CreateRule("stage_challenge_with_hint_a", "带提示的关卡挑战界面", GameUiStateId.StageChallengeWithHint, 970,
                    Eq(780, 380, "#F34A12"), Eq(780, 760, "#5388D2"), Eq(900, 760, "#62E200")),
                CreateRule("stage_challenge_with_hint_b", "带提示的关卡挑战界面", GameUiStateId.StageChallengeWithHint, 970,
                    Eq(820, 330, "#F24710"), Eq(820, 760, "#D2D2D2"), Eq(960, 760, "#FFFFFF")),
                CreateRule("in_level", "关卡挑战界面", GameUiStateId.InLevel, 960,
                    Eq(1910, 40, "#B1814A"), Eq(13, 40, "#B1814A")),
                CreateRule("stage_settings", "关卡设置界面", GameUiStateId.StageSettings, 950,
                    Eq(1658, 40, "#6A573C"), Eq(13, 40, "#62482E"), Eq(580, 240, "#BE945B"), Eq(550, 380, "#9F7843")),
                CreateRule("stage_defeat", "关卡失败界面", GameUiStateId.Defeat, 945,
                    Eq(722, 383, "#6397D8"), Eq(1213, 383, "#6397D8"), Eq(988, 118, "#FFFFFF")),
                CreateRule("chest_opened", "宝箱已开启界面", GameUiStateId.ChestOpened, 940,
                    Eq(960, 180, "#121417"), Eq(750, 60, "#121417"), Eq(1130, 200, "#121417"), Eq(1725, 500, "#121417"), Eq(884, 1000, "#67E400"), Eq(1037, 1000, "#67E400")),
                CreateRule("two_chests", "两个宝箱界面", GameUiStateId.TwoChests, 939,
                    Eq(960, 180, "#121417"), Eq(750, 60, "#121417"), Eq(1130, 200, "#121417"), Eq(1725, 500, "#121417"), Ne(828, 600, "#121417"), Ne(1130, 600, "#121417")),
                CreateRule("three_chests", "三个宝箱界面", GameUiStateId.ThreeChests, 939,
                    Eq(960, 180, "#121417"), Eq(750, 60, "#121417"), Eq(1130, 200, "#121417"), Eq(1725, 500, "#121417"), Ne(660, 600, "#121417"), Ne(1260, 600, "#121417")),
                CreateRule("main_menu", "游戏主界面", GameUiStateId.MainMenu, 700,
                    Eq(966, 945, "#FFFFFF"), Eq(1382, 942, "#FFFFFF"), Eq(80, 186, "#C4E8EB")),
                CreateRule("map_search_results", "关卡已搜索界面", GameUiStateId.MapSearchResults, 690,
                    Eq(983, 48, "#385373"), Eq(778, 43, "#578CD4"), Eq(962, 837, "#409FFF")),
                CreateRule("map_search", "关卡搜索界面", GameUiStateId.MapSearch, 680,
                    Eq(983, 48, "#385373"), Eq(778, 43, "#578CD4")),
                CreateRule("map_grid", "关卡选择界面", GameUiStateId.MapGrid, 670,
                    Eq(68, 54, "#FFFFFF"), Eq(273, 432, "#FFD500"), Eq(1649, 432, "#FFD500"), Eq(1313, 960, "#FF3400")),
                CreateRule("difficulty_select", "关卡难度选择界面", GameUiStateId.DifficultySelect, 660,
                    Eq(68, 54, "#FFFFFF"), Eq(716, 627, "#9F7842"), Eq(1049, 627, "#9F7842"), Eq(1388, 627, "#9F7842")),
                CreateRule("easy_mode_select", "简单模式选择界面", GameUiStateId.EasyModeSelect, 650,
                    Eq(68, 54, "#FFFFFF"), Eq(820, 40, "#804A24"), Eq(1200, 40, "#804A24"), Eq(720, 50, "#F2C776")),
                CreateRule("medium_mode_select", "普通模式选择界面", GameUiStateId.MediumModeSelect, 650,
                    Eq(68, 54, "#FFFFFF"), Eq(820, 40, "#804A24"), Eq(1200, 40, "#804A24"), Eq(720, 50, "#C6DFEB")),
                CreateRule("hard_mode_select", "困难模式选择界面", GameUiStateId.HardModeSelect, 650,
                    Eq(68, 54, "#FFFFFF"), Eq(820, 40, "#804A24"), Eq(1200, 40, "#804A24"), Eq(720, 50, "#FFED00")),
                CreateRule("hero_select", "英雄选择界面", GameUiStateId.HeroSelect, 640,
                    Eq(68, 54, "#FFFFFF"), Eq(1601, 1005, "#FFFFFF"), Eq(636, 51, "#FFFFFF"), Eq(1900, 1060, "#050505")),
                CreateRule("event_menu", "活动界面", GameUiStateId.EventMenu, 630,
                    Eq(68, 54, "#FFFFFF"), Eq(750, 60, "#996633"), Eq(1190, 60, "#996633"), Eq(600, 320, "#E2E2E2")),
                CreateRule("event_details", "活动详情界面", GameUiStateId.EventDetails, 625,
                    Eq(68, 54, "#FFFFFF"), Eq(750, 60, "#996633"), Eq(1190, 60, "#996633")),
                CreateRule("collection_event_claimable_red", "收集活动可领取界面", GameUiStateId.CollectionEventClaimable, 620,
                    Eq(750, 60, "#BF330B"), Eq(1100, 60, "#BF330B"), Eq(885, 681, "#67E200")),
                CreateRule("collection_event_claimable_pink", "收集活动可领取界面", GameUiStateId.CollectionEventClaimable, 620,
                    Eq(750, 60, "#CD78FF"), Eq(1100, 60, "#CD78FF"), Eq(885, 681, "#67E200")),
                CreateRule("collection_event_claimable_purple", "收集活动可领取界面", GameUiStateId.CollectionEventClaimable, 620,
                    Eq(750, 60, "#912DC9"), Eq(1100, 60, "#912DC9"), Eq(885, 681, "#67E200")),
                CreateRule("collection_event_claimable_gold", "收集活动可领取界面", GameUiStateId.CollectionEventClaimable, 620,
                    Eq(750, 60, "#FFD400"), Eq(1100, 60, "#FFD400"), Eq(885, 681, "#67E200")),
                CreateRule("collection_event_red", "收集活动界面", GameUiStateId.CollectionEvent, 615,
                    Eq(750, 60, "#BF330B"), Eq(1100, 60, "#BF330B")),
                CreateRule("collection_event_pink", "收集活动界面", GameUiStateId.CollectionEvent, 615,
                    Eq(750, 60, "#CD78FF"), Eq(1100, 60, "#CD78FF")),
                CreateRule("collection_event_purple", "收集活动界面", GameUiStateId.CollectionEvent, 615,
                    Eq(750, 60, "#912DC9"), Eq(1100, 60, "#912DC9")),
                CreateRule("collection_event_gold", "收集活动界面", GameUiStateId.CollectionEvent, 615,
                    Eq(750, 60, "#FFD400"), Eq(1100, 60, "#FFD400")),
                CreateRule("odyssey_start", "Odyssey start", GameUiStateId.OdysseyStart, 614,
                    Eq(1760, 960, "#FFFFFF"), Eq(600, 70, "#AB927C"), Eq(1320, 70, "#AB927C"), Eq(1900, 80, "#1190FF")),
                CreateRule("odyssey_crew", "Odyssey crew", GameUiStateId.OdysseyCrew, 613,
                    Eq(330, 500, "#CBA774"), Eq(1360, 350, "#B4874F"), Eq(1360, 650, "#B4874F")),
                CreateRule("odyssey_loading", "Odyssey loading", GameUiStateId.OdysseyLoading, 612,
                    Eq(1760, 960, "#A4B9D2"), Eq(600, 70, "#AB927C"), Eq(1320, 70, "#AB927C"), Eq(1000, 940, "#1190FF")),
                CreateRule("odyssey_settlement", "Odyssey settlement", GameUiStateId.OdysseySettlement, 947,
                    Eq(330, 500, "#CBA774"), Eq(1360, 350, "#B4874F"), Eq(1360, 650, "#B4874F"), Eq(1070, 300, "#CF2C0C"), Eq(1450, 300, "#CF2C0C")),
                CreateRule("odyssey_stage_victory", "Odyssey stage victory", GameUiStateId.OdysseyStageVictory, 946,
                    Eq(560, 190, "#FFFFFF"), Eq(1220, 145, "#FFFFFF"), Eq(950, 780, "#DCC3A8")),
                CreateRule("odyssey_reward", "Odyssey reward", GameUiStateId.OdysseyReward, 944,
                    Eq(600, 680, "#A58B71"), Eq(1300, 680, "#A58B71"), Eq(870, 840, "#64E300")),
                CreateRule("stage_settlement", "关卡结算界面", GameUiStateId.StageSettlement, 930,
                    Eq(555, 197, "#FFFFFF"), Eq(1365, 324, "#FFFFFF"), Eq(791, 193, "#F34A12")),
                CreateRule("stage_victory", "关卡通关界面", GameUiStateId.Victory, 935,
                    Eq(555, 197, "#FFFFFF"), Eq(1221, 152, "#FFFFFF"), Eq(585, 579, "#5F93D7")),
                CreateRule("stage_level_up", "关卡升级界面", GameUiStateId.LevelUp, 928,
                    Eq(1910, 40, "#543E2A"), Eq(13, 40, "#543E2A")),
                CreateRule("stage_hint", "关卡提示界面", GameUiStateId.StageHint, 927,
                    Eq(1080, 400, "#71E800"), Eq(1080, 500, "#6095D7"), Eq(1080, 725, "#69E500")),
                CreateRule("insta_monkey_reward", "Insta猴获取界面", GameUiStateId.InstaMonkeyReward, 926,
                    Eq(50, 50, "#010001"), Eq(1870, 50, "#010001"), Eq(750, 250, "#991112"), Eq(1150, 250, "#991112")),
                CreateRule("race_result", "竞速结果界面", GameUiStateId.RaceResult, 925,
                    Eq(880, 830, "#61E200"), Eq(700, 350, "#F34A12"), Eq(500, 390, "#6599D9"), Eq(830, 570, "#FFD200")),
                CreateRule("boss_result", "BOSS战结果界面", GameUiStateId.BossResult, 924,
                    Eq(500, 140, "#6B1000"), Eq(800, 205, "#118FB3"), Eq(1400, 140, "#6B1000"), Eq(1150, 205, "#118FB3")),
                CreateRule("returnable", "可返回界面", GameUiStateId.Returnable, 100,
                    Eq(68, 54, "#FFFFFF"))
            ]
        };
    }

    private static void MergeMissingDefaultRules(GameUiDetectionConfig target, GameUiDetectionConfig defaults)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(defaults);

        var existingKeys = new HashSet<string>(
            target.Rules.Select(static rule => rule.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var rule in defaults.Rules.Select(NormalizeRule))
        {
            if (string.IsNullOrWhiteSpace(rule.Key) || existingKeys.Contains(rule.Key))
            {
                continue;
            }

            target.Rules.Add(rule);
            existingKeys.Add(rule.Key);
        }
    }

    private static GameUiDetectionRule CreateRule(
        string key,
        string displayName,
        GameUiStateId state,
        int priority,
        params GameUiColorCondition[] allOf)
    {
        return new GameUiDetectionRule
        {
            Key = key,
            DisplayName = displayName,
            State = state,
            Priority = priority,
            IsEnabled = true,
            AllOf = [.. allOf]
        };
    }

    private static GameUiColorCondition Eq(int x, int y, string colorHex, int? tolerance = null)
    {
        return new GameUiColorCondition
        {
            X = x,
            Y = y,
            ColorHex = colorHex,
            Operator = GameUiColorComparisonOperator.Equals,
            Tolerance = tolerance
        };
    }

    private static GameUiColorCondition Ne(int x, int y, string colorHex, int? tolerance = null)
    {
        return new GameUiColorCondition
        {
            X = x,
            Y = y,
            ColorHex = colorHex,
            Operator = GameUiColorComparisonOperator.NotEquals,
            Tolerance = tolerance
        };
    }
}
