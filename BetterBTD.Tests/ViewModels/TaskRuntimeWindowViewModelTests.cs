using BetterBTD.Models.AutoTasks;
using BetterBTD.ViewModels;

namespace BetterBTD.Tests.ViewModels;

public sealed class TaskRuntimeWindowViewModelTests
{
    [Fact]
    public void ApplyProgressSnapshot_MapsCurrentLoopAndFormatsLongRuntimeDuration()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 14, 32, 8, TimeSpan.Zero));
        using var viewModel = CreateViewModel(timeProvider);

        Start(viewModel);
        viewModel.ApplyProgressSnapshot(CreateProgressSnapshot(
            timeProvider.GetUtcNow() - new TimeSpan(25, 2, 3),
            loopIteration: 128));

        Assert.Equal("128", viewModel.CurrentLoopText);
        Assert.Equal("25:02:03", viewModel.RuntimeDurationText);
    }

    [Fact]
    public async Task StartExecutionAgain_ResetsRuntimeMetrics()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 14, 32, 8, TimeSpan.Zero));
        var startCount = 0;
        using var viewModel = CreateViewModel(
            timeProvider,
            viewModel =>
            {
                startCount++;
                if (startCount == 1)
                {
                    viewModel.ApplyResult(CreateResult(CreateProgressSnapshot(timeProvider.GetUtcNow(), loopIteration: 128)));
                }

                return Task.CompletedTask;
            });

        await viewModel.StartCommand.ExecuteAsync(null);
        viewModel.StartCommand.Execute(null);

        Assert.Equal(2, startCount);
        Assert.Equal(LocalizationService.Instance.T("Tasks.Runtime.Metrics.NotStarted"), viewModel.CurrentLoopText);
        Assert.Equal("00:00:00", viewModel.RuntimeDurationText);

        viewModel.ApplyResult(CreateResult(CreateProgressSnapshot(timeProvider.GetUtcNow(), loopIteration: 0)));
    }

    [Fact]
    public void ApplyResult_FreezesRuntimeDuration()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 14, 32, 8, TimeSpan.Zero));
        using var viewModel = CreateViewModel(timeProvider);

        Start(viewModel);
        viewModel.ApplyProgressSnapshot(CreateProgressSnapshot(timeProvider.GetUtcNow(), loopIteration: 7));
        timeProvider.Advance(TimeSpan.FromMinutes(3));
        viewModel.ApplyResult(CreateResult(CreateProgressSnapshot(timeProvider.GetUtcNow() - TimeSpan.FromMinutes(3), loopIteration: 8)));

        Assert.Equal("00:03:00", viewModel.RuntimeDurationText);

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        viewModel.ApplyUnexpectedException(new InvalidOperationException("ignored after completion"));

        Assert.Equal("00:03:00", viewModel.RuntimeDurationText);
    }

    private static TaskRuntimeWindowViewModel CreateViewModel(
        TimeProvider timeProvider,
        Func<TaskRuntimeWindowViewModel, Task>? startExecutionAsync = null)
    {
        return new TaskRuntimeWindowViewModel(
            LocalizationService.Instance,
            "Test Task",
            "Test task summary",
            operationIntervalMs: 200,
            startExecutionAsync: startExecutionAsync ?? (_ => Task.CompletedTask),
            requestStop: () => { },
            timeProvider: timeProvider);
    }

    private static void Start(TaskRuntimeWindowViewModel viewModel)
    {
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.IsRunning);
    }

    private static AutoTaskProgressSnapshot CreateProgressSnapshot(DateTimeOffset startedAt, int loopIteration)
    {
        return new AutoTaskProgressSnapshot
        {
            RunState = AutoTaskRunState.Running,
            StartedAt = startedAt,
            LoopIteration = loopIteration,
            Message = "Running"
        };
    }

    private static AutoTaskExecutionResult CreateResult(AutoTaskProgressSnapshot finalProgress)
    {
        return new AutoTaskExecutionResult
        {
            Status = AutoTaskExecutionStatus.Completed,
            FinalProgress = finalProgress
        };
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan elapsed)
        {
            _utcNow += elapsed;
        }
    }
}
