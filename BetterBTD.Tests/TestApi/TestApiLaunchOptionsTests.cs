using BetterBTD.Models.TestApi;
using BetterBTD.Services.Tasks.TestApi;

namespace BetterBTD.Tests.TestApi;

public sealed class TestApiLaunchOptionsTests
{
    [Fact]
    public void Parse_WithoutEnableFlag_ReturnsDisabledOptions()
    {
        var options = TestApiLaunchOptions.Parse([], _ => null);

        Assert.False(options.Enabled);
    }

    [Fact]
    public void Parse_UsesProcessEnvironmentTokenWithoutPersistingItInArguments()
    {
        var token = new string('t', TestApiConstants.MinimumTokenLength);

        var options = TestApiLaunchOptions.Parse(
            ["--test-api", "--test-api-url", "http://127.0.0.1:19001/"],
            name => name == TestApiLaunchOptions.TokenEnvironmentVariable ? token : null);

        Assert.True(options.Enabled);
        Assert.Equal("http://127.0.0.1:19001/", options.ListenUrl);
        Assert.Equal(token, options.Token);
    }

    [Theory]
    [InlineData("http://127.0.0.1:18767/", "http://127.0.0.1:18767/")]
    [InlineData("http://[::1]:18767/", "http://[::1]:18767/")]
    public void Normalize_LoopbackAddress_ReturnsNormalizedUrl(string value, string expected)
    {
        Assert.Equal(expected, TestApiListenUrl.Normalize(value));
    }

    [Theory]
    [InlineData("https://127.0.0.1:18767/")]
    [InlineData("http://0.0.0.0:18767/")]
    [InlineData("http://192.168.1.2:18767/")]
    [InlineData("http://localhost:18767/")]
    [InlineData("http://+:18767/")]
    [InlineData("http://127.0.0.1:18767/api/")]
    [InlineData("http://user@127.0.0.1:18767/")]
    [InlineData("http://127.0.0.1:18767/?token=value")]
    [InlineData("http://127.0.0.1:18767/#fragment")]
    public void Normalize_NonLoopbackOrAmbiguousUrl_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => TestApiListenUrl.Normalize(value));
    }

    [Fact]
    public void Parse_ShortToken_Throws()
    {
        Assert.Throws<ArgumentException>(() => TestApiLaunchOptions.Parse(
            ["--test-api", "--test-api-token", "short"],
            _ => null));
    }
}

public sealed class TestApiTokenAuthenticatorTests
{
    [Fact]
    public void Authenticate_RequiresExactBearerToken()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        using var authenticator = new TestApiTokenAuthenticator(token);

        Assert.True(authenticator.Authenticate($"Bearer {token}"));
        Assert.True(authenticator.Authenticate($"bearer {token}"));
        Assert.False(authenticator.Authenticate(null));
        Assert.False(authenticator.Authenticate(token));
        Assert.False(authenticator.Authenticate($"Bearer {token} "));
        Assert.False(authenticator.Authenticate($"Bearer {token[..^1]}x"));
    }

    [Fact]
    public void Dispose_InvalidatesToken()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        var authenticator = new TestApiTokenAuthenticator(token);

        authenticator.Dispose();

        Assert.False(authenticator.Authenticate($"Bearer {token}"));
    }
}
