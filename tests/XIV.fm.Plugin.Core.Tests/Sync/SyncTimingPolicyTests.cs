using XIV.fm.Plugin.Core.Sync;

namespace XIV.fm.Plugin.Core.Tests.Sync;

public sealed class SyncTimingPolicyTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(30, 30)]
    [InlineData(600, 300)]
    public void ServerDelayIsClamped(int requested, int expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), SyncTimingPolicy.FromServerDelay(requested));
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(6, 300)]
    [InlineData(20, 300)]
    public void FailureDelayUsesBoundedExponentialBackoff(int failures, int expected)
    {
        Assert.Equal(TimeSpan.FromSeconds(expected), SyncTimingPolicy.FailureDelay(failures));
    }
}
