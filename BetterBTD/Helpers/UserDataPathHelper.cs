using System.IO;

namespace BetterBTD.Helpers;

internal static class UserDataPathHelper
{
    private static readonly Lazy<string> DefaultUserRootDirectory = new(() => ResolveUserRootDirectory(
        Path.Combine(AppContext.BaseDirectory, "User"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterBTD", "User")));

    public static string ResolveUserDataDirectory(params string[] relativeSegments)
    {
        return ResolveDirectory(DefaultUserRootDirectory.Value, relativeSegments);
    }

    public static string ResolveUserDataFilePath(params string[] relativeSegments)
    {
        return ResolveFilePath(DefaultUserRootDirectory.Value, relativeSegments);
    }

    internal static string ResolveUserRootDirectory(string installedUserDirectory, string localUserDirectory)
    {
        // Keep primary and elevated child sessions on the same library after migration.
        if (Directory.Exists(localUserDirectory))
        {
            EnsureWritableDirectory(localUserDirectory);
            return localUserDirectory;
        }

        try
        {
            EnsureWritableDirectory(installedUserDirectory);
            return installedUserDirectory;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Program Files and other protected installation locations cannot hold mutable data.
            // Publish the initial copy atomically so a failed migration can be retried safely.
            if (!Directory.Exists(localUserDirectory))
            {
                var stagingDirectory = localUserDirectory + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    Directory.CreateDirectory(stagingDirectory);
                    if (Directory.Exists(installedUserDirectory))
                    {
                        CopyDirectory(installedUserDirectory, stagingDirectory);
                    }

                    try
                    {
                        Directory.Move(stagingDirectory, localUserDirectory);
                    }
                    catch (IOException) when (Directory.Exists(localUserDirectory))
                    {
                        // Another instance completed the initial copy first.
                    }
                }
                finally
                {
                    if (Directory.Exists(stagingDirectory))
                    {
                        Directory.Delete(stagingDirectory, recursive: true);
                    }
                }
            }

            EnsureWritableDirectory(localUserDirectory);
            return localUserDirectory;
        }
    }

    private static void EnsureWritableDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
        using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1, FileOptions.DeleteOnClose);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyDirectory(directory, target);
        }
    }

    internal static string ResolveDirectory(string userRootDirectory, params string[] relativeSegments)
    {
        var targetDirectoryPath = CombinePath(userRootDirectory, relativeSegments);
        Directory.CreateDirectory(targetDirectoryPath);
        return targetDirectoryPath;
    }

    internal static string ResolveFilePath(string userRootDirectory, params string[] relativeSegments)
    {
        var targetFilePath = CombinePath(userRootDirectory, relativeSegments);
        var targetDirectoryPath = Path.GetDirectoryName(targetFilePath);

        if (!string.IsNullOrWhiteSpace(targetDirectoryPath))
        {
            Directory.CreateDirectory(targetDirectoryPath);
        }

        return targetFilePath;
    }

    private static string CombinePath(string rootDirectory, IReadOnlyList<string> relativeSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(relativeSegments);

        var path = rootDirectory;
        foreach (var segment in relativeSegments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segment);
            path = Path.Combine(path, segment.Trim());
        }

        return path;
    }
}
