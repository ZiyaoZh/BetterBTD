using System.IO;
using BetterBTD.Services.Start;

namespace BetterBTD.Tests.Services;

public sealed class GameLaunchServiceTests : IDisposable
{
    private readonly string _temporaryDirectory;

    public GameLaunchServiceTests()
    {
        _temporaryDirectory = Path.Combine(Path.GetTempPath(), "BetterBTD.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public void TryResolveExecutablePath_DirectExecutablePath_ReturnsPath()
    {
        var executablePath = Path.Combine(_temporaryDirectory, "BloonsTD6.exe");
        File.WriteAllText(executablePath, string.Empty);

        var result = GameLaunchService.TryResolveExecutablePath(
            executablePath,
            out var resolvedPath,
            out var failureMessage);

        Assert.True(result);
        Assert.Equal(Path.GetFullPath(executablePath), resolvedPath);
        Assert.Empty(failureMessage);
    }

    [Theory]
    [InlineData("BloonsTD6.exe")]
    [InlineData("BloonsTD6-Epic.exe")]
    public void TryResolveExecutablePath_InstallDirectoryWithKnownExecutable_ReturnsExecutablePath(
        string executableName)
    {
        var executablePath = Path.Combine(_temporaryDirectory, executableName);
        File.WriteAllText(executablePath, string.Empty);

        var result = GameLaunchService.TryResolveExecutablePath(
            _temporaryDirectory,
            out var resolvedPath,
            out var failureMessage);

        Assert.True(result);
        Assert.Equal(Path.GetFullPath(executablePath), resolvedPath);
        Assert.Empty(failureMessage);
    }

    [Fact]
    public void TryResolveExecutablePath_InstallDirectoryWithoutKnownExecutable_ReturnsFalse()
    {
        var result = GameLaunchService.TryResolveExecutablePath(
            _temporaryDirectory,
            out var resolvedPath,
            out var failureMessage);

        Assert.False(result);
        Assert.Empty(resolvedPath);
        Assert.NotEmpty(failureMessage);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
