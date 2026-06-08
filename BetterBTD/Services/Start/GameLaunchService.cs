using System.Diagnostics;
using System.Globalization;
using System.IO;
using BetterBTD.Services.Start.Capture;
using BetterBTD.Services.Shell.Localization;

namespace BetterBTD.Services.Start;

public sealed record GameLaunchResult(
    bool Success,
    bool GameWasAlreadyRunning,
    bool LaunchAttempted,
    string Message);

public sealed class GameLaunchService
{
    private static readonly Lazy<GameLaunchService> InstanceHolder = new(() => new GameLaunchService());

    private static readonly string[] CandidateExecutableNames =
    [
        "BloonsTD6.exe",
        "BloonsTD6-Epic.exe"
    ];

    private readonly GameWindowInfoService _gameWindowInfoService;
    private readonly LocalizationService _localizationService;

    private GameLaunchService()
        : this(GameWindowInfoService.Instance, LocalizationService.Instance)
    {
    }

    internal GameLaunchService(
        GameWindowInfoService gameWindowInfoService,
        LocalizationService? localizationService = null)
    {
        _gameWindowInfoService = gameWindowInfoService ?? throw new ArgumentNullException(nameof(gameWindowInfoService));
        _localizationService = localizationService ?? LocalizationService.Instance;
    }

    public static GameLaunchService Instance => InstanceHolder.Value;

    public async Task<GameLaunchResult> EnsureGameStartedAsync(
        string installPath,
        TimeSpan windowWaitTimeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        if (_gameWindowInfoService.TryGetTargetWindowInfo(out _))
        {
            return new GameLaunchResult(true, true, false, T("Start.GameLaunch.AlreadyRunning"));
        }

        if (!TryLaunchGame(installPath, out var launchMessage))
        {
            return new GameLaunchResult(false, false, false, launchMessage);
        }

        var waitStartedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - waitStartedAt < windowWaitTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_gameWindowInfoService.TryGetTargetWindowInfo(out _))
            {
                return new GameLaunchResult(true, false, true, T("Start.GameLaunch.Started"));
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return new GameLaunchResult(
            false,
            false,
            true,
            Format("Start.GameLaunch.WindowTimeout", windowWaitTimeout.TotalSeconds));
    }

    public bool TryLaunchGame(string installPath, out string message)
    {
        if (!TryResolveExecutablePath(installPath, out var executablePath, out message))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = true
            });

            message = T("Start.GameLaunch.CommandSent");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            message = Format("Start.GameLaunch.StartFailed", ex.Message);
            return false;
        }
    }

    internal static bool TryResolveExecutablePath(
        string? installPath,
        out string executablePath,
        out string failureMessage)
    {
        executablePath = string.Empty;

        var normalizedPath = Environment.ExpandEnvironmentVariables(installPath?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            failureMessage = LocalizationService.Instance.T("Start.GameLaunch.PathRequired");
            return false;
        }

        if (File.Exists(normalizedPath))
        {
            if (!string.Equals(Path.GetExtension(normalizedPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                failureMessage = LocalizationService.Instance.T("Start.GameLaunch.PathNotExecutable");
                return false;
            }

            executablePath = Path.GetFullPath(normalizedPath);
            failureMessage = string.Empty;
            return true;
        }

        if (!Directory.Exists(normalizedPath))
        {
            failureMessage = LocalizationService.Instance.T("Start.GameLaunch.InstallPathMissing");
            return false;
        }

        foreach (var candidateExecutableName in CandidateExecutableNames)
        {
            var candidatePath = Path.Combine(normalizedPath, candidateExecutableName);
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            executablePath = Path.GetFullPath(candidatePath);
            failureMessage = string.Empty;
            return true;
        }

        failureMessage = LocalizationService.Instance.T("Start.GameLaunch.ExecutableNotFound");
        return false;
    }

    private string T(string key)
    {
        return _localizationService.T(key);
    }

    private string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
