namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Immutable counters for validating the placeholder player/nameplate rendering pipeline.
/// </summary>
public sealed record OverlayRenderDiagnostics(
    int RequestedCards,
    int MatchedPlayers,
    int InRangePlayers,
    int ProjectedAnchors,
    int RenderedCards,
    DateTimeOffset CapturedAt)
{
    public static OverlayRenderDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, DateTimeOffset.MinValue);
}
