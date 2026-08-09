using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BetterBTD.Core.ScriptExecution;
using BetterBTD.Core.TestApi;
using BetterBTD.Models.ScriptExecution;
using BetterBTD.Models.TestApi;
using BetterBTD.Services.Tasks.TestApi;

namespace BetterBTD.Tests.TestApi;

public sealed class TestApiHttpServerTests
{
    [Fact]
    public async Task Health_RequiresBearerTokenAndUsesNonOracleDiagnosticsEnvelope()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        var port = ReservePort();
        var listenUrl = $"http://127.0.0.1:{port}/";
        var coordinator = new TestApiCoordinator(
            new HealthOnlyRuntimeEnvironment(),
            _ => throw new NotSupportedException());
        var server = new TestApiHttpServer(coordinator);

        await server.StartAsync(listenUrl, token);
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(listenUrl)
            };

            using var anonymousResponse = await client.GetAsync("api/test/v1/health");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.Contains("no-store", anonymousResponse.Headers.CacheControl?.ToString());

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var authenticatedResponse = await client.GetAsync("api/test/v1/health");
            Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);

            using var body = JsonDocument.Parse(await authenticatedResponse.Content.ReadAsStringAsync());
            Assert.Equal("v1", body.RootElement.GetProperty("apiVersion").GetString());
            Assert.True(body.RootElement.TryGetProperty("nonOracleDiagnostics", out var diagnostics));
            Assert.True(diagnostics.TryGetProperty("capture", out _));
            Assert.False(body.RootElement.TryGetProperty("passed", out _));
            Assert.False(body.RootElement.TryGetProperty("oracleEligible", out _));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Restart_InvalidatesPreviousToken()
    {
        const string firstToken = "11111111111111111111111111111111";
        const string secondToken = "22222222222222222222222222222222";
        var port = ReservePort();
        var listenUrl = $"http://127.0.0.1:{port}/";
        var coordinator = new TestApiCoordinator(
            new HealthOnlyRuntimeEnvironment(),
            _ => throw new NotSupportedException());
        var server = new TestApiHttpServer(coordinator);

        await server.StartAsync(listenUrl, firstToken);
        await server.StopAsync();
        await server.StartAsync(listenUrl, secondToken);
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(listenUrl)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
            using var oldTokenResponse = await client.GetAsync("api/test/v1/health");
            Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
            using var newTokenResponse = await client.GetAsync("api/test/v1/health");
            Assert.Equal(HttpStatusCode.OK, newTokenResponse.StatusCode);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task CallerCancellationAfterStart_DoesNotStopServerLifetime()
    {
        const string token = "33333333333333333333333333333333";
        var port = ReservePort();
        var listenUrl = $"http://127.0.0.1:{port}/";
        var coordinator = new TestApiCoordinator(
            new HealthOnlyRuntimeEnvironment(),
            _ => throw new NotSupportedException());
        var server = new TestApiHttpServer(coordinator);
        using var cancellationSource = new CancellationTokenSource();

        await server.StartAsync(listenUrl, token, cancellationSource.Token);
        cancellationSource.Cancel();
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(listenUrl),
                Timeout = TimeSpan.FromSeconds(2)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var firstResponse = await client.GetAsync("api/test/v1/health");
            using var secondResponse = await client.GetAsync("api/test/v1/health");

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task PreCancelledCallerToken_DoesNotStartServer()
    {
        const string token = "44444444444444444444444444444444";
        var server = new TestApiHttpServer(new TestApiCoordinator(
            new HealthOnlyRuntimeEnvironment(),
            _ => throw new NotSupportedException()));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await server.StartAsync(
                $"http://127.0.0.1:{ReservePort()}/",
                token,
                cancellationSource.Token));

        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Execute_ReturnsAcceptedAndOperationLocationWithoutWaitingForCompletion()
    {
        const string token = "abcdef0123456789abcdef0123456789";
        var port = ReservePort();
        var listenUrl = $"http://127.0.0.1:{port}/";
        var server = new TestApiHttpServer(new ExecuteOnlyController());

        await server.StartAsync(listenUrl, token);
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(listenUrl)
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var content = new StringContent(
                "{\"scriptPath\":\"test.json\"}",
                Encoding.UTF8,
                "application/json");

            using var response = await client.PostAsync("api/test/v1/scripts/execute", content);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(
                "/api/test/v1/operations/status?operationId=test-operation",
                response.Headers.Location?.OriginalString);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("test-operation", body.RootElement.GetProperty("operationId").GetString());
            Assert.Equal("Starting", body.RootElement.GetProperty("status").GetString());

            using var invalidContent = new StringContent(
                "{\"scriptPath\":\"test.json\"}",
                Encoding.UTF8,
                "text/plain");
            using var invalidResponse = await client.PostAsync(
                "api/test/v1/scripts/execute",
                invalidContent);
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

            using var numericEnumContent = new StringContent(
                "{\"scriptPath\":\"test.json\",\"intervalStrategy\":0}",
                Encoding.UTF8,
                "application/json");
            using var numericEnumResponse = await client.PostAsync(
                "api/test/v1/scripts/execute",
                numericEnumContent);
            Assert.Equal(HttpStatusCode.BadRequest, numericEnumResponse.StatusCode);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class HealthOnlyRuntimeEnvironment : ITestApiRuntimeEnvironment
    {
        public bool IsScriptExecutorRunning => false;

        public bool IsAutoTaskRunning => false;

        public bool IsRobotTaskRunning => false;

        public ScriptExecutionProgressSnapshot? CurrentProgress => null;

        public event EventHandler<ScriptExecutionProgressSnapshot>? ProgressChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ScriptExecutionRuntimeLogEntry>? RuntimeLogEmitted
        {
            add { }
            remove { }
        }

        public TestApiConfigurationSnapshot GetConfigurationSnapshot() => new()
        {
            TargetWindowTitle = "BloonsTD6",
            CaptureModeName = "TestCapture",
            CaptureIntervalMs = 50,
            GameLanguageCode = "zh-CN",
            KeyboardMouseSimulationModeName = "SendInput"
        };

        public TestApiCaptureSnapshot GetCaptureSnapshot() => new()
        {
            IsRunning = false,
            TargetWindowTitle = "BloonsTD6",
            CaptureModeName = "TestCapture",
            CaptureIntervalMs = 50
        };

        public Task<TestApiCaptureSnapshot> StartCaptureAsync(
            TestApiCaptureStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool RequestPause() => false;

        public bool Resume() => false;

        public void ReleaseAllKeys()
        {
        }

        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptTaskFlow taskFlow,
            ScriptExecutionOptions options,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ExecuteOnlyController : ITestApiController
    {
        public TestApiScriptExecuteResponse ExecuteScript(TestApiScriptExecuteRequest request) => new()
        {
            OperationId = "test-operation",
            Status = TestApiOperationStatus.Starting,
            AcceptedAt = DateTimeOffset.UtcNow
        };

        public TestApiHealthResponse GetHealth() => throw new NotSupportedException();

        public Task<TestApiCaptureStartResponse> StartCaptureAsync(
            TestApiCaptureStartRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public TestApiScriptValidationResponse ValidateScript(TestApiScriptPathRequest request) =>
            throw new NotSupportedException();

        public TestApiOperationSnapshot GetOperationStatus(string? operationId) =>
            throw new NotSupportedException();

        public TestApiOperationLogsResponse GetOperationLogs(
            string? operationId,
            long afterSequence,
            int limit) => throw new NotSupportedException();

        public TestApiOperationControlResponse Pause(TestApiOperationControlRequest request) =>
            throw new NotSupportedException();

        public TestApiOperationControlResponse Resume(TestApiOperationControlRequest request) =>
            throw new NotSupportedException();

        public TestApiOperationControlResponse Cancel(TestApiOperationControlRequest request) =>
            throw new NotSupportedException();
    }
}
