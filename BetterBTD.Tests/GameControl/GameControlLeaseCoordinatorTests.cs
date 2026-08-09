using BetterBTD.Core.GameControl;

namespace BetterBTD.Tests.GameControl;

public sealed class GameControlLeaseCoordinatorTests
{
    [Fact]
    public async Task JoinedScriptScope_KeepsOwnerActiveUntilLastReferenceIsReleased()
    {
        var coordinator = new GameControlLeaseCoordinator();
        Assert.True(coordinator.TryAcquire(GameControlOwnerKind.AutoTask, "auto-owner", out var ownerLease));

        GameControlExecutionScope scriptScope;
        using (GameControlLeaseContext.Push(ownerLease.OwnerId))
        {
            scriptScope = coordinator.AcquireOrJoinForScriptExecution();
        }

        var idleTask = coordinator.WaitForIdleAsync();
        ownerLease.Dispose();

        Assert.True(coordinator.HasActiveLease);
        Assert.False(idleTask.IsCompleted);

        scriptScope.Dispose();
        await idleTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(coordinator.HasActiveLease);
    }

    [Fact]
    public void SuccessfulRetry_ClearsPoisonBeforeLeaseIsReleased()
    {
        var coordinator = new GameControlLeaseCoordinator();
        Assert.True(coordinator.TryAcquire(GameControlOwnerKind.ScriptExecution, "script-owner", out var lease));

        lease.MarkPoisoned();
        Assert.True(coordinator.IsPoisoned);

        lease.ConfirmInputReleased();
        lease.Dispose();

        Assert.False(coordinator.IsPoisoned);
        Assert.True(coordinator.TryAcquire(GameControlOwnerKind.RobotTask, "robot-owner", out var nextLease));
        nextLease.Dispose();
    }
}
