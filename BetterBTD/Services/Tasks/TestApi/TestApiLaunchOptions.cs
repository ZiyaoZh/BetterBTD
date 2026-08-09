using System.Net;
using System.Security.Cryptography;
using System.Text;
using BetterBTD.Models.TestApi;

namespace BetterBTD.Services.Tasks.TestApi;

internal sealed class TestApiLaunchOptions
{
    public const string TokenEnvironmentVariable = "BETTERBTD_TEST_API_TOKEN";

    public bool Enabled { get; init; }

    public string ListenUrl { get; init; } = TestApiConstants.DefaultListenUrl;

    public string Token { get; init; } = string.Empty;

    public static TestApiLaunchOptions Parse(
        IReadOnlyList<string> arguments,
        Func<string, string?>? environmentReader = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        environmentReader ??= Environment.GetEnvironmentVariable;

        var enabled = arguments.Any(
            argument => string.Equals(argument, "--test-api", StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            return new TestApiLaunchOptions();
        }

        var listenUrl = GetOptionValue(arguments, "--test-api-url") ?? TestApiConstants.DefaultListenUrl;
        var token = GetOptionValue(arguments, "--test-api-token") ??
                    environmentReader(TokenEnvironmentVariable) ??
                    string.Empty;

        if (token.Length < TestApiConstants.MinimumTokenLength)
        {
            throw new ArgumentException(
                $"The test API token must contain at least {TestApiConstants.MinimumTokenLength} characters. " +
                $"Pass --test-api-token or set {TokenEnvironmentVariable} for this process.");
        }

        return new TestApiLaunchOptions
        {
            Enabled = true,
            ListenUrl = TestApiListenUrl.Normalize(listenUrl),
            Token = token
        };
    }

    private static string? GetOptionValue(IReadOnlyList<string> arguments, string optionName)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (value is not null)
            {
                throw new ArgumentException($"Option '{optionName}' can only be specified once.");
            }

            if (index + 1 >= arguments.Count ||
                string.IsNullOrWhiteSpace(arguments[index + 1]) ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{optionName}' requires a value.");
            }

            value = arguments[++index];
        }

        return value;
    }
}

internal static class TestApiListenUrl
{
    public static string Normalize(string listenUrl)
    {
        if (!Uri.TryCreate(listenUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            uri.Port <= 0 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            !IPAddress.TryParse(uri.Host, out var address) ||
            !IPAddress.IsLoopback(address))
        {
            throw new ArgumentException(
                $"Test API listen URL '{listenUrl}' is invalid. Use an HTTP URL with a numeric loopback address and root path.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : $"{uri.AbsoluteUri}/";
    }
}

internal sealed class TestApiTokenAuthenticator : IDisposable
{
    private readonly object _syncRoot = new();
    private byte[] _expectedHash;
    private bool _isValid = true;

    public TestApiTokenAuthenticator(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("A test API token is required.", nameof(token));
        }

        _expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public bool Authenticate(string? authorizationHeader)
    {
        const string bearerPrefix = "Bearer ";
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedToken = authorizationHeader[bearerPrefix.Length..];
        if (suppliedToken.Length == 0 || suppliedToken.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
        lock (_syncRoot)
        {
            return _isValid && CryptographicOperations.FixedTimeEquals(_expectedHash, suppliedHash);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (!_isValid)
            {
                return;
            }

            _isValid = false;
            CryptographicOperations.ZeroMemory(_expectedHash);
            _expectedHash = [];
        }
    }
}
