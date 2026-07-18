using System.Collections.Immutable;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// A complete point-in-time view consumed by the renderer.
/// </summary>
public sealed record OverlaySnapshot(ImmutableArray<OverlayCard> Cards, DateTimeOffset CapturedAt)
{
    public static OverlaySnapshot Empty { get; } = new([], DateTimeOffset.MinValue);

    public static OverlaySnapshot Create(IEnumerable<OverlayCard> cards, DateTimeOffset capturedAt) =>
        new([.. cards], capturedAt);
}
