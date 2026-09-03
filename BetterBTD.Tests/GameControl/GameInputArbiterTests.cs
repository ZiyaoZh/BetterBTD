using BetterBTD.Core.GameControl;

namespace BetterBTD.Tests.GameControl;

public sealed class GameInputArbiterTests
{
    [Fact]
    public void ChildLease_IsAllowedOnlyUnderItsNavigationOwner()
    {
        var arbiter = new GameInputArbiter();
        Assert.True(arbiter.TryAcquire("navigation", GameInputPriority.Navigation, out var navigation));
        Assert.True(arbiter.TryAcquire("script", GameInputPriority.Script, out var script, "navigation"));
        Assert.False(arbiter.TryAcquire("other", GameInputPriority.Script, out _, "other-parent"));

        script.Dispose();
        navigation.Dispose();
        Assert.False(arbiter.HasActiveLease);
    }

    [Fact]
    public void AmbientNavigationScope_AllowsScriptChildLeaseAndRestoresContext()
    {
        var arbiter = new GameInputArbiter();
        Assert.True(arbiter.TryAcquire("navigation", GameInputPriority.Navigation, out var navigation));
        Assert.Null(GameInputArbiterContext.Current);

        using (GameInputArbiterContext.Push(new InputArbiterContextState(arbiter, navigation)))
        {
            Assert.Same(arbiter, GameInputArbiterContext.Current!.Arbiter);
            Assert.True(arbiter.TryAcquire("script", GameInputPriority.Script, out var script, navigation.OwnerId));
            script.Dispose();
        }

        Assert.Null(GameInputArbiterContext.Current);
        navigation.Dispose();
    }

    [Fact]
    public async Task TemporaryPreemption_RequiresAcknowledgementAndRelease()
    {
        var arbiter = new GameInputArbiter();
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        InputArbiterLease? script = null;
        Assert.True(arbiter.TryAcquire(
            "script",
            GameInputPriority.Script,
            out var scriptLease,
            preemptAsync: _ => { script?.Dispose(); released.TrySetResult(true); return Task.FromResult(true); }));
        script = scriptLease;

        using var popup = await arbiter.AcquireTemporaryAsync(
            "popup", GameInputPriority.TemporaryPopup, TimeSpan.FromSeconds(1));
        Assert.True(await released.Task);
        Assert.Equal("popup", popup.OwnerId);
    }

    [Fact]
    public async Task TemporaryPreemption_TimesOutWithoutAcknowledgement()
    {
        var arbiter = new GameInputArbiter();
        Assert.True(arbiter.TryAcquire("script", GameInputPriority.Script, out var script,
            preemptAsync: _ => Task.FromResult(false)));

        await Assert.ThrowsAsync<TimeoutException>(() => arbiter.AcquireTemporaryAsync(
            "popup", GameInputPriority.TemporaryPopup, TimeSpan.FromMilliseconds(50)));
        script.Dispose();
    }

    [Fact]
    public void ReleaseFailure_PoisoningBlocksNewLeasesUntilConfirmed()
    {
        var arbiter = new GameInputArbiter();
        Assert.True(arbiter.TryAcquire("navigation", GameInputPriority.Navigation, out var lease));
        lease.MarkPoisoned();
        lease.Dispose();
        Assert.True(arbiter.IsPoisoned);
        Assert.False(arbiter.TryAcquire("next", GameInputPriority.Navigation, out _));
    }
}
