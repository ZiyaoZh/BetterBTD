using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BetterBTD.Models;

namespace BetterBTD.Services;

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
        Current.LanguageCode = configuration.LanguageCode;
        Current.ThemeMode = configuration.ThemeMode;
        Current.GameLanguageCode = configuration.GameLanguageCode;
        Current.StartHotkey = configuration.StartHotkey;
        Current.StopHotkey = configuration.StopHotkey;
        Current.GameStartHotkey = configuration.GameStartHotkey;
        Current.GameStopHotkey = configuration.GameStopHotkey;
        Current.KeyBindings = configuration.KeyBindings ?? Current.KeyBindings;
        Current.KeyBindings ??= new BetterBTD.Core.Config.KeyBindingsConfig();

        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_configFilePath, json);
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
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Debug.WriteLine($"Load configuration failed: {ex.Message}");
            return new AppConfiguration();
        }
    }
}
