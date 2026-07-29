using System.Numerics;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayVisibilityTests
{
    [Fact]
    public void DefaultRemoteDistanceIsEightYalms()
    {
        Assert.Equal(8, OverlayVisibility.DefaultRemoteDistanceYalms);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 8)]
    [InlineData(100, 20)]
    public void RemoteDistanceIsClamped(int configured, int expected)
    {
        Assert.Equal(expected, OverlayVisibility.NormalizeRemoteDistance(configured));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void OwnCardSettingAffectsOnlyLocalCard(bool isLocal, bool showOwnCard, bool expected)
    {
        Assert.Equal(expected, OverlayVisibility.ShouldRenderCard(isLocal, showOwnCard));
    }

    [Fact]
    public void RemoteAtExactlyEightYalmsIsVisible()
    {
        Assert.True(OverlayVisibility.IsRemoteWithinRange(Vector3.Zero, new Vector3(8, 0, 0), 8));
    }

    [Fact]
    public void RemoteBeyondEightYalmsIsHidden()
    {
        Assert.False(OverlayVisibility.IsRemoteWithinRange(Vector3.Zero, new Vector3(8.01f, 0, 0), 8));
    }
}
