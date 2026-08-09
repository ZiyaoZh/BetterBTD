using BetterBTD.Services.Tasks.TestApi;

namespace BetterBTD.Tests.TestApi;

public sealed class TestApiRouteResolverTests
{
    public static TheoryData<string, string, string> Routes => new()
    {
        { "GET", "/api/test/v1/health", nameof(TestApiRoute.Health) },
        { "POST", "/api/test/v1/capture/start", nameof(TestApiRoute.CaptureStart) },
        { "POST", "/api/test/v1/scripts/validate", nameof(TestApiRoute.ScriptsValidate) },
        { "POST", "/api/test/v1/scripts/execute", nameof(TestApiRoute.ScriptsExecute) },
        { "GET", "/api/test/v1/operations/status", nameof(TestApiRoute.OperationStatus) },
        { "GET", "/api/test/v1/operations/logs", nameof(TestApiRoute.OperationLogs) },
        { "POST", "/api/test/v1/operations/pause", nameof(TestApiRoute.OperationPause) },
        { "POST", "/api/test/v1/operations/resume", nameof(TestApiRoute.OperationResume) },
        { "POST", "/api/test/v1/operations/cancel", nameof(TestApiRoute.OperationCancel) }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Resolve_V1Route_ReturnsExpectedRoute(string method, string path, string expected)
    {
        Assert.Equal(expected, TestApiRouteResolver.Resolve(method, path).ToString());
    }

    [Theory]
    [InlineData("GET", "/api/test/v1/scripts/execute")]
    [InlineData("POST", "/api/test/v1/health")]
    [InlineData("GET", "/api/test/v2/health")]
    [InlineData("POST", "/api/robot-task/actions/start/execute")]
    public void Resolve_UnknownMethodOrPath_ReturnsNotFound(string method, string path)
    {
        Assert.Equal(TestApiRoute.NotFound, TestApiRouteResolver.Resolve(method, path));
    }
}
