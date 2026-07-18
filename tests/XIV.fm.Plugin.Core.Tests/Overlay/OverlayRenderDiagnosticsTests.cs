using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.Core.Tests.Overlay;

public sealed class OverlayRenderDiagnosticsTests
{
    [Fact]
    public void EmptyDiagnosticsReportNoRenderedWork()
    {
        var diagnostics = OverlayRenderDiagnostics.Empty;

        Assert.Equal(0, diagnostics.RequestedCards);
        Assert.Equal(0, diagnostics.MatchedPlayers);
        Assert.Equal(0, diagnostics.InRangePlayers);
        Assert.Equal(0, diagnostics.ProjectedAnchors);
        Assert.Equal(0, diagnostics.RenderedCards);
    }
}
