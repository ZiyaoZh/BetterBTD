using System.IO;
using System.Text;
using BetterBTD.Helpers;
using BetterBTD.Services.ChildSession;

namespace BetterBTD.Services.Diagnostics;

internal sealed class DiagnosticsFileLogWriter : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public DiagnosticsFileLogWriter(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = filePath;
        _writer = new StreamWriter(
            new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public string FilePath { get; }

    public void Trace(string category, string message)
    {
        Write("Trace", category, message);
    }

    public void Info(string category, string message)
    {
        Write("Info", category, message);
    }

    public void Warning(string category, string message)
    {
        Write("Warning", category, message);
    }

    public void Error(string category, string message)
    {
        Write("Error", category, message);
    }

    public void Write(string level, string category, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? "Info" : level.Trim();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        var normalizedMessage = message.Trim();

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{normalizedLevel}] [{normalizedCategory}] {normalizedMessage}");
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}

internal static class DiagnosticsLogFilePathFactory
{
    public static string CreateDatedLogFilePath(string logGroup, string sessionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        var logDirectory = UserDataPathHelper.ResolveUserDataDirectory(
            "Logs",
            logGroup.Trim(),
            ChildSessionRuntimeState.LogSessionDirectoryName,
            DateTime.Now.ToString("yyyyMMdd"));
        var safeSessionName = SanitizeFileName(sessionName);
        var fileName = $"{DateTime.Now:HHmmss_fff}_{safeSessionName}.log";
        return Path.Combine(logDirectory, fileName);
    }

    public static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Session";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return string.IsNullOrWhiteSpace(builder.ToString())
            ? "Session"
            : builder.ToString();
    }
}
