using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BetterBTD.Models;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Services.ChildSession;

namespace BetterBTD.Services.Settings;

public sealed class ConfigurationService
{
    private static readonly Lazy<ConfigurationService> InstanceHolder = new(() => new ConfigurationService());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configFilePath;

    private ConfigurationService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterBTD",
            "appsettings.json"))
    {
    }

    internal ConfigurationService(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        _configFilePath = Path.GetFullPath(configFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath)!);

        Current = Load();
    }

    public static ConfigurationService Instance => InstanceHolder.Value;

    public AppConfiguration Current { get; }

    public void Save(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ChildSessionRuntimeState.EnsurePrimaryCanControl();

        if (!ChildSessionRuntimeState.CanPersistSharedData)
        {
            return;
        }

        Current.MaskWindowTargetTitle = configuration.MaskWindowTargetTitle;
        Current.CaptureModeName = configuration.CaptureModeName;
        Current.CaptureIntervalMs = Math.Clamp(configuration.CaptureIntervalMs, 10, 2000);
        Current.AutoFixWin11BitBlt = configuration.AutoFixWin11BitBlt;
        Current.LaunchGameWithCapturer = configuration.LaunchGameWithCapturer;
        Current.GameInstallPath = NormalizeGameInstallPath(configuration.GameInstallPath);
        Current.LanguageCode = configuration.LanguageCode;
        Current.ThemeMode = configuration.ThemeMode;
        Current.GameLanguageCode = configuration.GameLanguageCode;
        Current.KeyboardMouseSimulationModeName = KeyboardMouseSimulationModeExtensions.Parse(configuration.KeyboardMouseSimulationModeName).ToConfigurationValue();
        Current.StartHotkey = configuration.StartHotkey;
        Current.StopHotkey = configuration.StopHotkey;
        Current.GameStartHotkey = configuration.GameStartHotkey;
        Current.GameStopHotkey = configuration.GameStopHotkey;
        Current.ScriptExecutionIntervalStrategyName = NormalizeScriptExecutionIntervalStrategyName(
            configuration.ScriptExecutionIntervalStrategyName);
        Current.ScriptExecutionCommonOperationIntervalMs = NormalizeScriptExecutionCommonOperationInterval(
            configuration.ScriptExecutionCommonOperationIntervalMs);
        Current.AutoTaskMaxConsecutiveNavigationFailures = NormalizeAutoTaskNavigationFailureLimit(
            configuration.AutoTaskMaxConsecutiveNavigationFailures);
        Current.AutoTaskStuckUiTimeoutSeconds = NormalizeAutoTaskStuckTimeoutSeconds(
            configuration.AutoTaskStuckUiTimeoutSeconds);
        Current.AutoTaskVisualFingerprintDistanceTolerance = NormalizeAutoTaskVisualFingerprintDistanceTolerance(
            configuration.AutoTaskVisualFingerprintDistanceTolerance);
        Current.AutoTaskStuckRecoveryDelayMs = NormalizeAutoTaskRecoveryDelay(
            configuration.AutoTaskStuckRecoveryDelayMs);
        Current.AutoTaskStuckRecoveryPoints = NormalizeAutoTaskRecoveryPoints(
            configuration.AutoTaskStuckRecoveryPoints);
        Current.KeyBindings = configuration.KeyBindings ?? Current.KeyBindings;
        Current.KeyBindings ??= new BetterBTD.Core.Config.KeyBindingsConfig();

        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_configFilePath, json);
    }

    public ScriptExecutionWindowSettings GetScriptExecutionWindowSettings()
    {
        return new ScriptExecutionWindowSettings
        {
            IntervalStrategy = ResolveScriptExecutionIntervalStrategy(Current.ScriptExecutionIntervalStrategyName),
            CommonOperationIntervalMs = NormalizeScriptExecutionCommonOperationInterval(
                Current.ScriptExecutionCommonOperationIntervalMs)
        };
    }

    public void SaveScriptExecutionWindowSettings(ScriptExecutionWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Current.ScriptExecutionIntervalStrategyName = settings.IntervalStrategy.ToString();
        Current.ScriptExecutionCommonOperationIntervalMs = NormalizeScriptExecutionCommonOperationInterval(
            settings.CommonOperationIntervalMs);

        Save(Current);
    }

    public AutoTaskExecutionOptions GetAutoTaskExecutionOptions()
    {
        return new AutoTaskExecutionOptions
        {
            MaxConsecutiveNavigationFailures = NormalizeAutoTaskNavigationFailureLimit(
                Current.AutoTaskMaxConsecutiveNavigationFailures),
            StuckUiTimeout = TimeSpan.FromSeconds(NormalizeAutoTaskStuckTimeoutSeconds(
                Current.AutoTaskStuckUiTimeoutSeconds)),
            VisualFingerprintDistanceTolerance = NormalizeAutoTaskVisualFingerprintDistanceTolerance(
                Current.AutoTaskVisualFingerprintDistanceTolerance),
            StuckRecoveryDelayMs = NormalizeAutoTaskRecoveryDelay(Current.AutoTaskStuckRecoveryDelayMs),
            StuckRecoveryPoints = NormalizeAutoTaskRecoveryPoints(Current.AutoTaskStuckRecoveryPoints).AsReadOnly()
        };
    }

    private AppConfiguration Load()
    {
        if (!File.Exists(_configFilePath))
        {
            return new AppConfiguration();
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
            config.KeyBindings ??= new BetterBTD.Core.Config.KeyBindingsConfig();
            config.CaptureModeName = string.IsNullOrWhiteSpace(config.CaptureModeName)
                ? nameof(Fischless.GameCapture.CaptureModes.WindowsGraphicsCapture)
                : config.CaptureModeName;
            config.CaptureIntervalMs = Math.Clamp(config.CaptureIntervalMs <= 0 ? 50 : config.CaptureIntervalMs, 10, 2000);
            config.GameInstallPath = NormalizeGameInstallPath(config.GameInstallPath);
            config.KeyboardMouseSimulationModeName =
                KeyboardMouseSimulationModeExtensions.Parse(config.KeyboardMouseSimulationModeName).ToConfigurationValue();
            config.ScriptExecutionIntervalStrategyName = NormalizeScriptExecutionIntervalStrategyName(
                config.ScriptExecutionIntervalStrategyName);
            config.ScriptExecutionCommonOperationIntervalMs = NormalizeScriptExecutionCommonOperationInterval(
                config.ScriptExecutionCommonOperationIntervalMs);
            config.AutoTaskMaxConsecutiveNavigationFailures = NormalizeAutoTaskNavigationFailureLimit(
                config.AutoTaskMaxConsecutiveNavigationFailures);
            config.AutoTaskStuckUiTimeoutSeconds = NormalizeAutoTaskStuckTimeoutSeconds(
                config.AutoTaskStuckUiTimeoutSeconds);
            config.AutoTaskVisualFingerprintDistanceTolerance = NormalizeAutoTaskVisualFingerprintDistanceTolerance(
                config.AutoTaskVisualFingerprintDistanceTolerance);
            config.AutoTaskStuckRecoveryDelayMs = NormalizeAutoTaskRecoveryDelay(
                config.AutoTaskStuckRecoveryDelayMs);
            config.AutoTaskStuckRecoveryPoints = NormalizeAutoTaskRecoveryPoints(
                config.AutoTaskStuckRecoveryPoints);
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Debug.WriteLine($"Load configuration failed: {ex.Message}");
            return new AppConfiguration();
        }
    }

    private static string NormalizeScriptExecutionIntervalStrategyName(string? strategyName)
    {
        return ResolveScriptExecutionIntervalStrategy(strategyName).ToString();
    }

    private static ScriptExecutionOperationIntervalStrategy ResolveScriptExecutionIntervalStrategy(string? strategyName)
    {
        return Enum.TryParse<ScriptExecutionOperationIntervalStrategy>(
                strategyName,
                ignoreCase: true,
                out var strategy) &&
            Enum.IsDefined(strategy)
                ? strategy
                : ScriptExecutionOperationIntervalStrategy.InstructionCustom;
    }

    private static int NormalizeScriptExecutionCommonOperationInterval(int intervalMs)
    {
        return Math.Clamp(intervalMs <= 0 ? 200 : intervalMs, 50, 1000);
    }

    internal static int NormalizeAutoTaskNavigationFailureLimit(int value)
    {
        return Math.Clamp(value <= 0 ? 5 : value, 1, 20);
    }

    internal static int NormalizeAutoTaskStuckTimeoutSeconds(int value)
    {
        return Math.Clamp(value <= 0 ? 10 : value, 1, 300);
    }

    internal static int NormalizeAutoTaskRecoveryDelay(int value)
    {
        return Math.Clamp(value, 0, 10000);
    }

    internal static int NormalizeAutoTaskVisualFingerprintDistanceTolerance(int value)
    {
        return Math.Clamp(value, 0, 64);
    }

    internal static List<GameUiRecoveryPoint> NormalizeAutoTaskRecoveryPoints(
        IEnumerable<GameUiRecoveryPoint>? points)
    {
        return points?
            .Select(point => new GameUiRecoveryPoint(
                Math.Clamp(point.X, 0, 1919),
                Math.Clamp(point.Y, 0, 1079)))
            .Take(20)
            .ToList() ?? AutoTaskExecutionOptions.CreateDefaultStuckRecoveryPoints();
    }

    private static string NormalizeGameInstallPath(string? installPath)
    {
        return installPath?.Trim() ?? string.Empty;
    }
}

