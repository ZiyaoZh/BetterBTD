using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BetterBTD.Services.Updates;

public sealed class ApplicationUpdateService
{
    private const string RepositoryUrl = "https://github.com/ZiyaoZh/BetterBTD";
    private const string ReleasesUrl = $"{RepositoryUrl}/releases/latest";
    private const string UpdaterExecutableName = "BetterBTD.update.exe";
    private static readonly Lazy<ApplicationUpdateService> InstanceHolder = new(() => new ApplicationUpdateService());

    private ApplicationUpdateService()
    {
    }

    public static ApplicationUpdateService Instance => InstanceHolder.Value;

    public string CurrentVersion
    {
        get => GetFallbackVersion();
    }

    public bool TryLaunchUpdater(out string message)
    {
        var updaterPath = Path.Combine(AppContext.BaseDirectory, UpdaterExecutableName);
        if (!File.Exists(updaterPath))
        {
            OpenLatestReleasePage();
            message = "BetterBTD.update.exe was not found. Opened the latest release page instead.";
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        });
        message = "Opened BetterBTD updater.";
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

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
