using System.Net;
using System.Net.Sockets;
using System.Text;
using BetterBTD.Core.AutoTasks.Runtime;
using BetterBTD.Core.GameControl;
using BetterBTD.Core.RobotControl;
using BetterBTD.Models.AutoTasks;
using BetterBTD.Models.RobotControl;
using BetterBTD.Services.Tasks.AutoTasks;
using BetterBTD.Services.Tasks.Input;
using BetterBTD.Services.Tasks.RobotControl;

namespace BetterBTD.Tests.RobotControl;

public sealed class RobotTaskRuntimeTests
{
    [Fact]
    public async Task CallerCancellationAfterStart_DoesNotHalfStopRuntime()
    {
        var coordinator = new RobotTaskCoordinator(
            new RobotActionRegistry([]),
            [],
            new StaticGameUiStateService(),
            GameCaptureService.Instance,
            ScriptInputSimulationService.Instance,
            CoordinateTransformService.Instance);
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var listenUrl = $"http://127.0.0.1:{ReservePort()}/";
        var runtime = new RobotTaskRuntime(
            coordinator,
            new RobotTaskHttpServer(coordinator),
            leaseCoordinator,
            () => { });
        using var cancellationSource = new CancellationTokenSource();

        await runtime.StartAsync(new RobotTaskRuntimeOptions
        {
            ListenUrl = listenUrl,
            UiAutomationPollIntervalMs = 5000
        }, cancellationSource.Token);
        cancellationSource.Cancel();

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(listenUrl),
                Timeout = TimeSpan.FromSeconds(2)
            };
            using var firstResponse = await client.GetAsync("api/robot-task/status");
            using var secondResponse = await client.GetAsync("api/robot-task/status");

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            Assert.True(runtime.IsRunning);
            Assert.True(leaseCoordinator.HasActiveLease);
        }
        finally
        {
            await runtime.StopAsync();
        }

        Assert.False(leaseCoordinator.HasActiveLease);
    }

    [Fact]
    public async Task PreCancelledCallerToken_DoesNotStartRuntimeOrAcquireLease()
    {
        var coordinator = new RobotTaskCoordinator(
            new RobotActionRegistry([]),
            [],
            new StaticGameUiStateService(),
            GameCaptureService.Instance,
            ScriptInputSimulationService.Instance,
            CoordinateTransformService.Instance);
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var runtime = new RobotTaskRuntime(
            coordinator,
            new RobotTaskHttpServer(coordinator),
            leaseCoordinator,
            () => { });
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StartAsync(
            new RobotTaskRuntimeOptions
            {
                ListenUrl = $"http://127.0.0.1:{ReservePort()}/"
            },
            cancellationSource.Token));

        Assert.False(runtime.IsRunning);
        Assert.False(leaseCoordinator.HasActiveLease);
    }


    [Fact]
    public async Task StopAsync_InputReleaseFailurePoisonsLeaseWithoutLeavingItHeld()
    {
        var action = new DelayedCancellationRobotAction();
        var coordinator = new RobotTaskCoordinator(
            new RobotActionRegistry([action]),
            [],
            new StaticGameUiStateService(),
            GameCaptureService.Instance,
            ScriptInputSimulationService.Instance,
            CoordinateTransformService.Instance);
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var runtime = new RobotTaskRuntime(
            coordinator,
            new RobotTaskHttpServer(coordinator),
            leaseCoordinator,
            () => throw new InvalidOperationException("release failed"));

        await runtime.StartAsync(new RobotTaskRuntimeOptions
        {
            ListenUrl = $"http://127.0.0.1:{ReservePort()}/",
            UiAutomationPollIntervalMs = 5000
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StopAsync());

        Assert.Equal("release failed", exception.Message);
        Assert.True(leaseCoordinator.IsPoisoned);
        Assert.False(leaseCoordinator.HasActiveLease);
    }

    [Fact]
    public async Task StopAsync_WaitsForHttpActionBeforeReleasingInputLease()
    {
        var action = new DelayedCancellationRobotAction();
        var coordinator = new RobotTaskCoordinator(
            new RobotActionRegistry([action]),
            [],
            new StaticGameUiStateService(),
            GameCaptureService.Instance,
            ScriptInputSimulationService.Instance,
            CoordinateTransformService.Instance);
        var leaseCoordinator = new GameControlLeaseCoordinator();
        var releaseAllKeysCallCount = 0;
        var runtime = new RobotTaskRuntime(
            coordinator,
            new RobotTaskHttpServer(coordinator),
            leaseCoordinator,
            () => Interlocked.Increment(ref releaseAllKeysCallCount));
        var listenUrl = $"http://127.0.0.1:{ReservePort()}/";
        Task? stopTask = null;

        await runtime.StartAsync(new RobotTaskRuntimeOptions
        {
            ListenUrl = listenUrl,
            UiAutomationPollIntervalMs = 5000
        });

        using var client = new HttpClient { BaseAddress = new Uri(listenUrl) };
        var requestTask = client.PostAsync(
            $"api/robot-task/actions/{action.Key}/execute",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        try
        {
            await action.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            stopTask = runtime.StopAsync();
            await action.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(stopTask.IsCompleted);
            Assert.True(leaseCoordinator.HasActiveLease);
            Assert.Equal(0, Volatile.Read(ref releaseAllKeysCallCount));

            action.AllowExit();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(leaseCoordinator.HasActiveLease);
            Assert.Equal(1, Volatile.Read(ref releaseAllKeysCallCount));
            await ObserveRequestCompletionAsync(requestTask);
        }
        finally
        {
            action.AllowExit();
            if (stopTask is not null)
            {
                await stopTask;
            }
            else
            {
                await runtime.StopAsync();
            }
        }
    }

    private static async Task ObserveRequestCompletionAsync(Task<HttpResponseMessage> requestTask)
    {
        try
        {
            using var response = await requestTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
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

    private sealed class StaticGameUiStateService : IGameUiStateService
    {
        public Task<GameUiSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GameUiSnapshot
            {
                State = GameUiStateId.InLevel,
                Confidence = 1d
            });
        }

        public void ResetStabilizationState()
        {
        }
    }

    private sealed class DelayedCancellationRobotAction : IRobotGameAction
    {
        private readonly TaskCompletionSource _allowExit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string Key => "blocking-runtime";

        public RobotActionMetadata Metadata { get; } = new()
        {
            Key = "blocking-runtime",
            DisplayName = "Blocking runtime action",
            TimeoutMs = 10000
        };

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RobotActionPrecheckResult> CheckAsync(
            RobotActionContext context,
            RobotActionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RobotActionPrecheckResult.Success("Ready."));
        }

        public async Task<RobotActionResult> ExecuteAsync(
            RobotActionContext context,
            RobotActionRequest request,
            IProgress<RobotActionProgress> progress,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The Robot action unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                await _allowExit.Task;
                throw;
            }
        }

        public void AllowExit()
        {
            _allowExit.TrySetResult();
        }
    }
}
