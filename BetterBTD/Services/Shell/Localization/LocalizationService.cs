using System;
using System.Collections.Generic;

namespace BetterBTD.Services.Shell.Localization;

public sealed partial class LocalizationService
{
    private static readonly Lazy<LocalizationService> InstanceHolder = new(() => new LocalizationService());

    private readonly Dictionary<string, Dictionary<string, string>> _resources;
    private string _languageCode = "zh-CN";

    private LocalizationService()
    {
        _resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = BuildZhCnResources(),
            ["en-US"] = BuildEnUsResources()
        };
    }

    public static LocalizationService Instance => InstanceHolder.Value;

    public event EventHandler? LanguageChanged;

    public string LanguageCode => _languageCode;

    public void SetLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        if (string.Equals(_languageCode, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _languageCode = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string T(string key)
    {
        if (key.StartsWith("Settings.", StringComparison.OrdinalIgnoreCase))
        {
            return _languageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                ? GetZhSettingsText(key)
                : GetEnSettingsText(key);
        }

        if (_resources.TryGetValue(_languageCode, out var languageMap) && languageMap.TryGetValue(key, out var localizedText))
        {
            return localizedText;
        }

        if (_resources["en-US"].TryGetValue(key, out var fallbackText))
        {
            return fallbackText;
        }

        return key;
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return "zh-CN";
    }
}
