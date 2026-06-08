using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BetterBTD.Models;
using BetterBTD.Models.ScriptExecution;

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
    {
        var appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterBTD");
        Directory.CreateDirectory(appDataDirectory);
        _configFilePath = Path.Combine(appDataDirectory, "appsettings.json");

        Current = Load();
    }

    public static ConfigurationService Instance => InstanceHolder.Value;

    public AppConfiguration Current { get; }

    public void Save(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

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

    private static string NormalizeGameInstallPath(string? installPath)
    {
        return installPath?.Trim() ?? string.Empty;
    }
}

