using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterBTD.Core.RobotControl;
using BetterBTD.Models.RobotControl;

namespace BetterBTD.Services.Tasks.RobotControl;

public sealed class RobotTaskHttpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly RobotTaskCoordinator _coordinator;
    private readonly object _syncRoot = new();
    private readonly HashSet<Task> _requestTasks = [];

    private HttpListener? _listener;
    private CancellationTokenSource? _cancellationSource;
    private Task? _listenTask;
    private string _listenUrl = string.Empty;

    public RobotTaskHttpServer()
        : this(RobotTaskCoordinator.Instance)
    {
    }

    internal RobotTaskHttpServer(RobotTaskCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
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

    public Task StartAsync(string listenUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedListenUrl = NormalizeListenUrl(listenUrl);

        lock (_syncRoot)
        {
            if (_listener is not null)
            {
                throw new InvalidOperationException("Robot task HTTP server is already running.");
            }

            var listener = new HttpListener();
            listener.Prefixes.Add(normalizedListenUrl);
            var cancellationSource = new CancellationTokenSource();
            try
            {
                listener.Start();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                listener.Close();
                cancellationSource.Dispose();
                throw;
            }

            _listener = listener;
            _listenUrl = normalizedListenUrl;
            _cancellationSource = cancellationSource;
            _listenTask = Task.Run(() => ListenLoopAsync(listener, _cancellationSource.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        HttpListener? listener;
        Task? listenTask;
        CancellationTokenSource? cancellationSource;

        lock (_syncRoot)
        {
            listener = _listener;
            listenTask = _listenTask;
            cancellationSource = _cancellationSource;

            _listener = null;
            _listenTask = null;
            _cancellationSource = null;
            _listenUrl = string.Empty;
        }

        if (listener is null)
        {
            return;
        }

        cancellationSource?.Cancel();
        listener.Close();

        if (listenTask is not null)
        {
            try
            {
                await listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }

        Task[] requestTasks;
        lock (_syncRoot)
        {
            requestTasks = _requestTasks.ToArray();
        }

        if (requestTasks.Length > 0)
        {
            await Task.WhenAll(requestTasks.Select(IgnoreRequestShutdownExceptionsAsync)).ConfigureAwait(false);
        }

        cancellationSource?.Dispose();
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
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

            var requestTask = HandleContextAsync(context, cancellationToken);
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

    private static async Task IgnoreRequestShutdownExceptionsAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException or
                ObjectDisposedException or
                HttpListenerException or
                InvalidOperationException or
                OperationCanceledException)
        {
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await DispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(
                    context,
                    HttpStatusCode.InternalServerError,
                    new
                    {
                        code = RobotActionErrorCodes.Failed,
                        message = ex.Message
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException or
                    HttpListenerException or
                    InvalidOperationException)
            {
            }
        }
    }

    private async Task DispatchAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var pathSegments = request.Url?.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            IsPath(pathSegments, "api", "robot-task", "status"))
        {
            await WriteJsonAsync(context, HttpStatusCode.OK, _coordinator.GetStatusSnapshot(), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            IsPath(pathSegments, "api", "robot-task", "actions"))
        {
            await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new
                    {
                        actions = _coordinator.GetActionMetadata()
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            IsPath(pathSegments, "api", "robot-task", "operations", "current"))
        {
            await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    _coordinator.GetStatusSnapshot().CurrentOperation,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            IsPath(pathSegments, "api", "robot-task", "operations", "last"))
        {
            await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    _coordinator.GetStatusSnapshot().LastResult,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            pathSegments.Length == 5 &&
            IsPathPrefix(pathSegments, "api", "robot-task", "actions") &&
            string.Equals(pathSegments[4], "execute", StringComparison.OrdinalIgnoreCase))
        {
            var actionKey = WebUtility.UrlDecode(pathSegments[3]);
            var actionRequest = await ReadActionRequestAsync(context, actionKey, cancellationToken)
                .ConfigureAwait(false);
            var response = await _coordinator
                .ExecuteActionAsync(actionKey, actionRequest, cancellationToken)
                .ConfigureAwait(false);

            await WriteJsonAsync(context, ResolveStatusCode(response), response, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(
                context,
                HttpStatusCode.NotFound,
                new
                {
                    code = RobotActionErrorCodes.InvalidAction,
                    message = "Robot task endpoint was not found."
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<RobotActionRequest> ReadActionRequestAsync(
        HttpListenerContext context,
        string actionKey,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasEntityBody)
        {
            return new RobotActionRequest
            {
                Action = actionKey
            };
        }

        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new RobotActionRequest
            {
                Action = actionKey
            };
        }

        var payload = JsonSerializer.Deserialize<RobotActionHttpRequestPayload>(body, JsonOptions)
            ?? new RobotActionHttpRequestPayload();

        return new RobotActionRequest
        {
            RequestId = payload.RequestId ?? string.Empty,
            Action = actionKey,
            Parameters = payload.Parameters ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };
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

    private static HttpStatusCode ResolveStatusCode(RobotActionResponse response)
    {
        if (response.Accepted)
        {
            return HttpStatusCode.OK;
        }

        return response.Code switch
        {
            RobotActionErrorCodes.InvalidAction => HttpStatusCode.NotFound,
            RobotActionErrorCodes.InvalidParameter => HttpStatusCode.BadRequest,
            RobotActionErrorCodes.TaskNotRunning => HttpStatusCode.ServiceUnavailable,
            RobotActionErrorCodes.Busy => HttpStatusCode.Conflict,
            RobotActionErrorCodes.InvalidGameState => HttpStatusCode.Conflict,
            RobotActionErrorCodes.UiAutomationRequired => HttpStatusCode.Conflict,
            _ => HttpStatusCode.BadRequest
        };
    }

    private static bool IsPath(IReadOnlyList<string> segments, params string[] expected)
    {
        return segments.Count == expected.Length && IsPathPrefix(segments, expected);
    }

    private static bool IsPathPrefix(IReadOnlyList<string> segments, params string[] expected)
    {
        if (segments.Count < expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (!string.Equals(segments[index], expected[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeListenUrl(string listenUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(listenUrl)
            ? RobotTaskConstants.DefaultListenUrl
            : listenUrl.Trim();

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException($"Robot task listen URL '{listenUrl}' is invalid. Use an HTTP URL.");
        }

        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : $"{normalized}/";
    }

    private sealed class RobotActionHttpRequestPayload
    {
        public string? RequestId { get; set; }

        public Dictionary<string, object?>? Parameters { get; set; }
    }
}
