using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterBTD.Services.Updates;

public sealed class ApplicationUpdateService
{
    private const string RepositoryUrl = "https://github.com/ZiyaoZh/BetterBTD";
    private const string ReleasesUrl = $"{RepositoryUrl}/releases/latest";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/ZiyaoZh/BetterBTD/releases/latest";
    private const string UpdaterExecutableName = "BetterBTD.update.exe";
    private static readonly Lazy<ApplicationUpdateService> InstanceHolder = new(() => new ApplicationUpdateService());
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    private ApplicationUpdateService()
    {
        _httpClient = Helpers.Http.HttpClientFactory.GetClient(
            "application-update",
            static () =>
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterBTD", GetFallbackVersion()));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                return client;
            });
    }

    public static ApplicationUpdateService Instance => InstanceHolder.Value;

    public string CurrentVersion => GetFallbackVersion();

    public async Task<string> CheckAndPromptForUpdateAsync(bool silentIfUpToDate, CancellationToken cancellationToken = default)
    {
        ApplicationUpdateCheckResult checkResult;

        try
        {
            checkResult = await CheckForUpdatesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            var message = BuildCheckFailedMessage(ex.Message);
            if (!silentIfUpToDate)
            {
                ShowInfoDialog(GetLocalizedText("Update.CheckFailed.Title"), message);
            }

            return message;
        }

        if (!checkResult.IsSuccessful)
        {
            var message = checkResult.StatusMessage;
            if (!silentIfUpToDate)
            {
                ShowInfoDialog(GetLocalizedText("Update.CheckFailed.Title"), message);
            }

            return message;
        }

        if (!checkResult.IsUpdateAvailable || checkResult.ReleaseInfo is null)
        {
            var message = checkResult.StatusMessage;
            if (!silentIfUpToDate)
            {
                ShowInfoDialog(GetLocalizedText("Update.UpToDate.Title"), message);
            }

            return message;
        }

        var dialogResult = AppDialogService.Instance.Show(new AppDialogRequest
        {
            Title = GetLocalizedText("Update.Available.Title"),
            Message = BuildUpdateDialogMessage(checkResult.ReleaseInfo),
            PrimaryButtonText = GetLocalizedText("Update.Action.UpdateNow"),
            CloseButtonText = GetLocalizedText("Update.Action.Cancel")
        });

        if (dialogResult != AppDialogResult.Primary)
        {
            return GetLocalizedText("Update.Status.Cancelled");
        }

        _ = TryLaunchUpdater(out var launchMessage);
        return launchMessage;
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ApplicationUpdateCheckResult
            {
                IsSuccessful = false,
                StatusMessage = BuildCheckFailedMessage(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "HTTP {(int)0}",
                        response.StatusCode))
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, JsonOptions, cancellationToken);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return new ApplicationUpdateCheckResult
            {
                IsSuccessful = false,
                StatusMessage = BuildCheckFailedMessage(GetLocalizedText("Update.Error.InvalidPayload"))
            };
        }

        var releaseInfo = new ApplicationReleaseInfo
        {
            VersionTag = release.TagName.Trim(),
            ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? release.TagName.Trim() : release.Name.Trim(),
            PublishedAt = release.PublishedAt,
            ReleaseNotes = NormalizeReleaseNotes(release.Body),
            HtmlUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesUrl : release.HtmlUrl.Trim()
        };

        var hasUpdate = IsNewerVersion(CurrentVersion, releaseInfo.VersionTag);
        return new ApplicationUpdateCheckResult
        {
            IsSuccessful = true,
            IsUpdateAvailable = hasUpdate,
            ReleaseInfo = releaseInfo,
            StatusMessage = hasUpdate
                ? BuildUpdateAvailableStatusMessage(releaseInfo)
                : BuildUpToDateMessage(CurrentVersion, releaseInfo.VersionTag)
        };
    }

    public bool TryLaunchUpdater(out string message)
    {
        var updaterPath = Path.Combine(AppContext.BaseDirectory, UpdaterExecutableName);
        if (!File.Exists(updaterPath))
        {
            OpenLatestReleasePage();
            message = GetLocalizedText("Update.Status.UpdaterMissing");
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        });

        message = GetLocalizedText("Update.Status.UpdaterStarted");
        return true;
    }

    public void OpenProjectHomePage()
    {
        OpenUrl(RepositoryUrl);
    }

    public void OpenLatestReleasePage()
    {
        OpenUrl(ReleasesUrl);
    }

    internal static bool IsNewerVersion(string currentVersion, string latestVersion)
    {
        var normalizedCurrent = NormalizeVersionString(currentVersion);
        var normalizedLatest = NormalizeVersionString(latestVersion);

        if (string.Equals(normalizedCurrent, normalizedLatest, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Version.TryParse(normalizedCurrent, out var currentParsed) &&
               Version.TryParse(normalizedLatest, out var latestParsed) &&
               latestParsed > currentParsed;
    }

    private static string GetFallbackVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
    }

    private static string NormalizeVersionString(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var normalized = version.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var metadataIndex = normalized.IndexOf('+');
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            normalized = normalized[..prereleaseIndex];
        }

        return normalized.Trim();
    }

    private static string NormalizeReleaseNotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GetLocalizedText("Update.ReleaseNotes.Empty");
        }

        return value.Replace("\r\n", "\n").Trim();
    }

    private static string BuildUpdateDialogMessage(ApplicationReleaseInfo releaseInfo)
    {
        var builder = new StringBuilder();
        if (LocalizationService.Instance.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"当前版本：{Instance.CurrentVersion}");
            builder.AppendLine($"最新版本：{releaseInfo.VersionTag}");
            builder.AppendLine($"发布时间：{releaseInfo.PublishedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
            builder.AppendLine($"发布标题：{releaseInfo.ReleaseName}");
            builder.AppendLine();
            builder.AppendLine("更新说明：");
            builder.AppendLine(releaseInfo.ReleaseNotes);
        }
        else
        {
            builder.AppendLine($"Current version: {Instance.CurrentVersion}");
            builder.AppendLine($"Latest version: {releaseInfo.VersionTag}");
            builder.AppendLine($"Published at: {releaseInfo.PublishedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
            builder.AppendLine($"Release title: {releaseInfo.ReleaseName}");
            builder.AppendLine();
            builder.AppendLine("Release notes:");
            builder.AppendLine(releaseInfo.ReleaseNotes);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildUpdateAvailableStatusMessage(ApplicationReleaseInfo releaseInfo)
    {
        return LocalizationService.Instance.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            ? $"发现新版本 {releaseInfo.VersionTag}。"
            : $"Update {releaseInfo.VersionTag} is available.";
    }

    private static string BuildUpToDateMessage(string currentVersion, string latestVersion)
    {
        return LocalizationService.Instance.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            ? $"当前已是最新版本（当前 {currentVersion}，最新 {latestVersion}）。"
            : $"You're up to date (current {currentVersion}, latest {latestVersion}).";
    }

    private static string BuildCheckFailedMessage(string detail)
    {
        return LocalizationService.Instance.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            ? $"检查更新失败：{detail}"
            : $"Failed to check for updates: {detail}";
    }

    private static string GetLocalizedText(string key)
    {
        var isChinese = LocalizationService.Instance.LanguageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);
        return key switch
        {
            "Update.Available.Title" => isChinese ? "发现新版本" : "Update Available",
            "Update.UpToDate.Title" => isChinese ? "已经是最新版本" : "Up to Date",
            "Update.CheckFailed.Title" => isChinese ? "检查更新失败" : "Update Check Failed",
            "Update.Action.UpdateNow" => isChinese ? "立即更新" : "Update Now",
            "Update.Action.Cancel" => isChinese ? "取消" : "Cancel",
            "Update.Action.Close" => isChinese ? "关闭" : "Close",
            "Update.Status.Cancelled" => isChinese ? "已取消更新。" : "Update was cancelled.",
            "Update.Status.UpdaterMissing" => isChinese
                ? "未找到 BetterBTD.update.exe，已改为打开最新发布页。"
                : "BetterBTD.update.exe was not found. Opened the latest release page instead.",
            "Update.Status.UpdaterStarted" => isChinese
                ? "已打开 BetterBTD 更新器。"
                : "Opened BetterBTD updater.",
            "Update.Error.InvalidPayload" => isChinese
                ? "远端发布信息格式无效。"
                : "The latest release payload is invalid.",
            "Update.ReleaseNotes.Empty" => isChinese
                ? "此版本未提供更新说明。"
                : "This release does not include release notes.",
            _ => key
        };
    }

    private static void ShowInfoDialog(string title, string message)
    {
        _ = AppDialogService.Instance.Show(new AppDialogRequest
        {
            Title = title,
            Message = message,
            PrimaryButtonText = GetLocalizedText("Update.Action.Close")
        });
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public sealed class ApplicationReleaseInfo
    {
        public string VersionTag { get; init; } = string.Empty;

        public string ReleaseName { get; init; } = string.Empty;

        public DateTimeOffset PublishedAt { get; init; }

        public string ReleaseNotes { get; init; } = string.Empty;

        public string HtmlUrl { get; init; } = string.Empty;
    }

    public sealed class ApplicationUpdateCheckResult
    {
        public bool IsSuccessful { get; init; }

        public bool IsUpdateAvailable { get; init; }

        public string StatusMessage { get; init; } = string.Empty;

        public ApplicationReleaseInfo? ReleaseInfo { get; init; }
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTimeOffset PublishedAt { get; init; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;
    }
}
