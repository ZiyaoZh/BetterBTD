using System.IO.Pipes;
using System.IO;
using System.Text;

namespace BetterBTD.Services.ChildSession;

internal sealed class ChildSessionControlServer : IDisposable
{
    private readonly NamedPipeServerStream _server;
    private readonly CancellationTokenSource _cancellationSource = new();

    private ChildSessionControlServer(string pipeName)
    {
        PipeName = pipeName;
        _server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
    }

    public string PipeName { get; }

    public event EventHandler<string>? MessageReceived;

    public event EventHandler? ConnectionClosed;

    public static ChildSessionControlServer Create()
    {
        return new ChildSessionControlServer($"BetterBTD.ChildSession.{Guid.NewGuid():N}");
    }

    public Task StartAsync()
    {
        return Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        try
        {
            await _server.WaitForConnectionAsync(_cancellationSource.Token).ConfigureAwait(false);
            using var reader = new StreamReader(_server, Encoding.UTF8, leaveOpen: true);
            while (!_cancellationSource.IsCancellationRequested)
            {
                var message = await reader.ReadLineAsync(_cancellationSource.Token).ConfigureAwait(false);
                if (message is null)
                {
                    return;
                }

                MessageReceived?.Invoke(this, message);
            }
        }
        catch (OperationCanceledException) when (_cancellationSource.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_cancellationSource.IsCancellationRequested)
        {
        }
        catch (IOException) when (_cancellationSource.IsCancellationRequested)
        {
        }
        finally
        {
            if (!_cancellationSource.IsCancellationRequested)
            {
                ConnectionClosed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        _cancellationSource.Cancel();
        _server.Dispose();
        _cancellationSource.Dispose();
    }
}

internal sealed class ChildSessionControlClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _client;
    private readonly StreamWriter _writer;

    private ChildSessionControlClient(NamedPipeClientStream client)
    {
        _client = client;
        _writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    public static async Task<ChildSessionControlClient?> ConnectAsync(
        string? pipeName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return null;
        }

        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
            return new ChildSessionControlClient(client);
        }
        catch (TimeoutException)
        {
            client.Dispose();
            return null;
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw;
        }
        catch (IOException)
        {
            client.Dispose();
            return null;
        }
    }

    public Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();
        _writer.WriteLine(message);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _writer.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
