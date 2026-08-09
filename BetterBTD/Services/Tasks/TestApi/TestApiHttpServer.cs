using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBTD.Core.TestApi;
using BetterBTD.Models.TestApi;

namespace BetterBTD.Services.Tasks.TestApi;

internal sealed class TestApiHttpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
        }
    };

    private readonly object _syncRoot = new();
    private readonly ITestApiController _controller;
    private readonly HashSet<Task> _requestTasks = [];

    private HttpListener? _listener;
    private TestApiTokenAuthenticator? _authenticator;
    private CancellationTokenSource? _cancellationSource;
    private Task? _listenTask;
    private string _listenUrl = string.Empty;

    public TestApiHttpServer(ITestApiController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _listener is not null;
            }
        }
    }

    public string ListenUrl
    {
        get
        {
            lock (_syncRoot)
            {
                return _listenUrl;
            }
        }
    }

    public Task StartAsync(
        string listenUrl,
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedListenUrl = TestApiListenUrl.Normalize(listenUrl);
        var listener = new HttpListener();
        var authenticator = new TestApiTokenAuthenticator(token);
        var cancellationSource = new CancellationTokenSource();

        listener.Prefixes.Add(normalizedListenUrl);
        try
        {
            listener.Start();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            listener.Close();
            authenticator.Dispose();
            cancellationSource.Dispose();
            throw;
        }

        lock (_syncRoot)
        {
            if (_listener is not null)
            {
                listener.Close();
                authenticator.Dispose();
                cancellationSource.Dispose();
                throw new InvalidOperationException("The BetterBTD test API is already running.");
            }

            _listener = listener;
            _authenticator = authenticator;
            _cancellationSource = cancellationSource;
            _listenUrl = normalizedListenUrl;
            _listenTask = Task.Run(
                () => ListenLoopAsync(listener, authenticator, cancellationSource.Token),
                CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        HttpListener? listener;
        TestApiTokenAuthenticator? authenticator;
        CancellationTokenSource? cancellationSource;
        Task? listenTask;

        lock (_syncRoot)
        {
            listener = _listener;
            authenticator = _authenticator;
            cancellationSource = _cancellationSource;
            listenTask = _listenTask;

            _listener = null;
            _authenticator = null;
            _cancellationSource = null;
            _listenTask = null;
            _listenUrl = string.Empty;
        }

        if (listener is null)
        {
            return;
        }

        authenticator?.Dispose();
        cancellationSource?.Cancel();
        listener.Close();

        if (listenTask is not null)
        {
            await IgnoreListenerShutdownExceptionsAsync(listenTask).ConfigureAwait(false);
        }

        Task[] requestTasks;
        lock (_syncRoot)
        {
            requestTasks = _requestTasks.ToArray();
        }

        if (requestTasks.Length > 0)
        {
            await Task.WhenAll(requestTasks.Select(IgnoreListenerShutdownExceptionsAsync)).ConfigureAwait(false);
        }

        cancellationSource?.Dispose();
    }

    private async Task ListenLoopAsync(
        HttpListener listener,
        TestApiTokenAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (
                cancellationToken.IsCancellationRequested &&
                ex is ObjectDisposedException or HttpListenerException or InvalidOperationException)
            {
                break;
            }

            var requestTask = HandleContextAsync(context, authenticator, cancellationToken);
            lock (_syncRoot)
            {
                _requestTasks.Add(requestTask);
            }

            _ = requestTask.ContinueWith(
                completedTask =>
                {
                    lock (_syncRoot)
                    {
                        _requestTasks.Remove(completedTask);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleContextAsync(
        HttpListenerContext context,
        TestApiTokenAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        try
        {
            SetSecurityHeaders(context.Response);
            if (context.Request.RemoteEndPoint is null ||
                !IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
            {
                await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new TestApiErrorResponse
                        {
                            Code = TestApiErrorCodes.InvalidToken,
                            Message = "Test API access is restricted to the local machine."
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (!authenticator.Authenticate(context.Request.Headers["Authorization"]))
            {
                context.Response.Headers[HttpResponseHeader.WwwAuthenticate] = "Bearer";
                await WriteJsonAsync(
                        context,
                        HttpStatusCode.Unauthorized,
                        new TestApiErrorResponse
                        {
                            Code = TestApiErrorCodes.InvalidToken,
                            Message = "A valid bearer token is required."
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await DispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (TestApiRequestException ex)
        {
            await TryWriteErrorAsync(context, ex.StatusCode, ex.Code, ex.Message, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await TryWriteErrorAsync(
                    context,
                    HttpStatusCode.BadRequest,
                    TestApiErrorCodes.InvalidRequest,
                    ex.Message,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await TryWriteErrorAsync(
                    context,
                    HttpStatusCode.InternalServerError,
                    TestApiErrorCodes.InternalError,
                    "The test API request failed unexpectedly.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task DispatchAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var route = TestApiRouteResolver.Resolve(
            context.Request.HttpMethod,
            context.Request.Url?.AbsolutePath ?? string.Empty);

        switch (route)
        {
            case TestApiRoute.Health:
                await WriteJsonAsync(context, HttpStatusCode.OK, _controller.GetHealth(), cancellationToken)
                    .ConfigureAwait(false);
                return;

            case TestApiRoute.CaptureStart:
            {
                var request = await ReadJsonAsync<TestApiCaptureStartRequest>(
                        context.Request,
                        allowEmpty: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                var response = await _controller.StartCaptureAsync(request, cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(context, HttpStatusCode.OK, response, cancellationToken).ConfigureAwait(false);
                return;
            }

            case TestApiRoute.ScriptsValidate:
            {
                var request = await ReadJsonAsync<TestApiScriptPathRequest>(
                        context.Request,
                        allowEmpty: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        _controller.ValidateScript(request),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case TestApiRoute.ScriptsExecute:
            {
                var request = await ReadJsonAsync<TestApiScriptExecuteRequest>(
                        context.Request,
                        allowEmpty: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                var response = _controller.ExecuteScript(request);
                context.Response.Headers[HttpResponseHeader.Location] =
                    $"{TestApiConstants.RoutePrefix}/operations/status?operationId={Uri.EscapeDataString(response.OperationId)}";
                await WriteJsonAsync(context, HttpStatusCode.Accepted, response, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case TestApiRoute.OperationStatus:
            {
                var operationId = context.Request.QueryString["operationId"];
                await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        _controller.GetOperationStatus(operationId),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case TestApiRoute.OperationLogs:
            {
                var operationId = context.Request.QueryString["operationId"];
                var afterSequence = ParseLongQueryValue(context.Request, "afterSequence", 0);
                var limit = ParseIntQueryValue(context.Request, "limit", 200);
                await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        _controller.GetOperationLogs(operationId, afterSequence, limit),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case TestApiRoute.OperationPause:
                await WriteControlResponseAsync(
                        context,
                        _controller.Pause,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            case TestApiRoute.OperationResume:
                await WriteControlResponseAsync(
                        context,
                        _controller.Resume,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            case TestApiRoute.OperationCancel:
                await WriteControlResponseAsync(
                        context,
                        _controller.Cancel,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;

            default:
                await WriteJsonAsync(
                        context,
                        HttpStatusCode.NotFound,
                        new TestApiErrorResponse
                        {
                            Code = TestApiErrorCodes.NotFound,
                            Message = "Test API endpoint was not found."
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
        }
    }

    private static async Task WriteControlResponseAsync(
        HttpListenerContext context,
        Func<TestApiOperationControlRequest, TestApiOperationControlResponse> action,
        CancellationToken cancellationToken)
    {
        var request = await ReadJsonAsync<TestApiOperationControlRequest>(
                context.Request,
                allowEmpty: false,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonAsync(context, HttpStatusCode.Accepted, action(request), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpListenerRequest request,
        bool allowEmpty,
        CancellationToken cancellationToken)
        where T : new()
    {
        if (!request.HasEntityBody)
        {
            if (allowEmpty)
            {
                return new T();
            }

            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "A JSON request body is required.");
        }

        var mediaType = request.ContentType?
            .Split(';', 2, StringSplitOptions.TrimEntries)[0];
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "Content-Type must be application/json.");
        }

        if (request.ContentLength64 > TestApiConstants.MaximumRequestBodyBytes)
        {
            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "The request body is too large.");
        }

        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await request.InputStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > TestApiConstants.MaximumRequestBodyBytes)
            {
                throw TestApiRequestException.BadRequest(
                    TestApiErrorCodes.InvalidRequest,
                    "The request body is too large.");
            }

            memory.Write(buffer, 0, read);
        }

        if (memory.Length == 0)
        {
            if (allowEmpty)
            {
                return new T();
            }

            throw TestApiRequestException.BadRequest(
                TestApiErrorCodes.InvalidRequest,
                "A JSON request body is required.");
        }

        return JsonSerializer.Deserialize<T>(memory.GetBuffer().AsSpan(0, checked((int)memory.Length)), JsonOptions)
               ?? throw TestApiRequestException.BadRequest(
                   TestApiErrorCodes.InvalidRequest,
                   "The JSON request body cannot be null.");
    }

    private static long ParseLongQueryValue(HttpListenerRequest request, string key, long defaultValue)
    {
        var value = request.QueryString[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (long.TryParse(value, out var result))
        {
            return result;
        }

        throw TestApiRequestException.BadRequest(
            TestApiErrorCodes.InvalidRequest,
            $"Query parameter '{key}' must be an integer.");
    }

    private static int ParseIntQueryValue(HttpListenerRequest request, string key, int defaultValue)
    {
        var value = request.QueryString[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, out var result))
        {
            return result;
        }

        throw TestApiRequestException.BadRequest(
            TestApiErrorCodes.InvalidRequest,
            $"Query parameter '{key}' must be an integer.");
    }

    private static async Task WriteJsonAsync(
        HttpListenerContext context,
        HttpStatusCode statusCode,
        object? value,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void SetSecurityHeaders(HttpListenerResponse response)
    {
        response.Headers[HttpResponseHeader.CacheControl] = "no-store";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static async Task TryWriteErrorAsync(
        HttpListenerContext context,
        HttpStatusCode statusCode,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteJsonAsync(
                    context,
                    statusCode,
                    new TestApiErrorResponse
                    {
                        Code = code,
                        Message = message
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException or OperationCanceledException)
        {
        }
    }

    private static async Task IgnoreListenerShutdownExceptionsAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException or OperationCanceledException)
        {
        }
    }
}

internal enum TestApiRoute
{
    NotFound,
    Health,
    CaptureStart,
    ScriptsValidate,
    ScriptsExecute,
    OperationStatus,
    OperationLogs,
    OperationPause,
    OperationResume,
    OperationCancel
}

internal static class TestApiRouteResolver
{
    public static TestApiRoute Resolve(string method, string absolutePath)
    {
        var path = absolutePath.TrimEnd('/');
        return (method.ToUpperInvariant(), path.ToLowerInvariant()) switch
        {
            ("GET", "/api/test/v1/health") => TestApiRoute.Health,
            ("POST", "/api/test/v1/capture/start") => TestApiRoute.CaptureStart,
            ("POST", "/api/test/v1/scripts/validate") => TestApiRoute.ScriptsValidate,
            ("POST", "/api/test/v1/scripts/execute") => TestApiRoute.ScriptsExecute,
            ("GET", "/api/test/v1/operations/status") => TestApiRoute.OperationStatus,
            ("GET", "/api/test/v1/operations/logs") => TestApiRoute.OperationLogs,
            ("POST", "/api/test/v1/operations/pause") => TestApiRoute.OperationPause,
            ("POST", "/api/test/v1/operations/resume") => TestApiRoute.OperationResume,
            ("POST", "/api/test/v1/operations/cancel") => TestApiRoute.OperationCancel,
            _ => TestApiRoute.NotFound
        };
    }
}
