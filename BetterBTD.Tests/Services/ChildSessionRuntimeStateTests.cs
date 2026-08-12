using BetterBTD.Services.ChildSession;

namespace BetterBTD.Tests.Services;

public sealed class ChildSessionRuntimeStateTests
{
    [Fact]
    public void Parse_DefaultsToPrimary()
    {
        var options = InstanceLaunchOptions.Parse([]);

        Assert.True(options.IsPrimary);
        Assert.Equal(BetterBtdInstanceRole.Primary, options.Role);
        Assert.Null(options.RootSessionId);
        Assert.Null(options.ControlPipeName);
    }

    [Fact]
    public void Parse_ChildSessionRequiresRootSessionAndPreservesPipe()
    {
        var options = InstanceLaunchOptions.Parse(
            ["--instance", "child-session", "--root-session-id", "42", "--child-session-pipe", "pipe-name"]);

        Assert.False(options.IsPrimary);
        Assert.Equal(BetterBtdInstanceRole.ChildSession, options.Role);
        Assert.Equal((uint)42, options.RootSessionId);
        Assert.Equal("pipe-name", options.ControlPipeName);
    }

    [Fact]
    public void Parse_RejectsInvalidRoleAndMissingChildRootSession()
    {
        Assert.Throws<ArgumentException>(() => InstanceLaunchOptions.Parse(["--instance", "other"]));
        Assert.Throws<ArgumentException>(() => InstanceLaunchOptions.Parse(["--instance", "child-session"]));
    }

    [Fact]
    public void PrimaryControlBlockPreventsGameControlAndPersistence()
    {
        try
        {
            ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
                BetterBtdInstanceRole.Primary,
                null,
                null));
            ChildSessionRuntimeState.SetPrimaryControlBlocked(true);

            Assert.True(ChildSessionRuntimeState.PrimaryControlBlocked);
            Assert.False(ChildSessionRuntimeState.CanPersistSharedData);
            Assert.Throws<InvalidOperationException>(ChildSessionRuntimeState.EnsurePrimaryCanControl);
            Assert.Throws<InvalidOperationException>(ChildSessionRuntimeState.EnsureSharedDataWritable);
        }
        finally
        {
            ResetRuntimeState();
        }
    }

    [Fact]
    public void ChildSessionIsAlwaysReadOnlyForSharedData()
    {
        try
        {
            ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
                BetterBtdInstanceRole.ChildSession,
                42,
                "pipe-name"));

            Assert.True(ChildSessionRuntimeState.IsChildSession);
            Assert.False(ChildSessionRuntimeState.CanPersistSharedData);
            Assert.Throws<InvalidOperationException>(ChildSessionRuntimeState.EnsureSharedDataWritable);
            ChildSessionRuntimeState.EnsurePrimaryCanControl();
        }
        finally
        {
            ResetRuntimeState();
        }
    }

    private static void ResetRuntimeState()
    {
        ChildSessionRuntimeState.Initialize(new InstanceLaunchOptions(
            BetterBtdInstanceRole.Primary,
            null,
            null));
    }
}
