using BetterBTD.Services.Updates;

namespace BetterBTD.Tests.Services;

public sealed class ApplicationUpdateServiceTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.1.0", "v0.2.0")]
    [InlineData("1.2.3", "1.2.4+build.9")]
    [InlineData("1.2.3", "1.2.3.1")]
    public void IsNewerVersion_HigherRelease_ReturnsTrue(string currentVersion, string latestVersion)
    {
        var result = ApplicationUpdateService.IsNewerVersion(currentVersion, latestVersion);

        Assert.True(result);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.2.0", "v0.2.0")]
    [InlineData("1.2.3", "1.2.3-beta.1")]
    [InlineData("2.0.0", "1.9.9")]
    public void IsNewerVersion_SameOrOlderRelease_ReturnsFalse(string currentVersion, string latestVersion)
    {
        var result = ApplicationUpdateService.IsNewerVersion(currentVersion, latestVersion);

        Assert.False(result);
    }
}
