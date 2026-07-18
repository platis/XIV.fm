using System.Collections.Immutable;
using XIV.fm.Plugin.Core.Presence;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// A complete point-in-time view consumed by the renderer.
/// </summary>
public sealed record OverlaySnapshot(
    ImmutableArray<OverlayCard> Cards,
    LocationScope? Location,
    DateTimeOffset CapturedAt)
{
    public static OverlaySnapshot Empty { get; } = new([], null, DateTimeOffset.MinValue);

    public static OverlaySnapshot Create(
        IEnumerable<OverlayCard> cards,
        DateTimeOffset capturedAt,
        LocationScope? location = null) =>
        new([.. cards], location, capturedAt);
}
