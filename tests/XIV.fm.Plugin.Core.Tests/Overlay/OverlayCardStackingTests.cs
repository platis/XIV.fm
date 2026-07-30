using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayCardStackingTests
{
    [Fact]
    public void LocalCardRendersAboveRemoteCards()
    {
        var remoteLayer = OverlayCardStacking.GetLayer(isLocal: false);
        var localLayer = OverlayCardStacking.GetLayer(isLocal: true);

        Assert.True(localLayer > remoteLayer);
        Assert.Equal(OverlayCardStacking.LayerCount - 1, localLayer);
    }
}
