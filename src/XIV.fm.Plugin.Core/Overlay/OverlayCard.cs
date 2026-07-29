using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Immutable presentation state for one character card.
/// </summary>
public sealed record OverlayCard(
    CharacterIdentity Character,
    string Title,
    string Artist,
    bool IsLocal,
    bool IsStale = false,
    bool IsLastFm = false,
    Uri? ArtworkUrl = null,
    Uri? TrackUrl = null)
{
    public static OverlayCard? LocalListening(
        CharacterIdentity character,
        ListeningState listening,
        DateTimeOffset now)
    {
        if (listening.Status != ListeningStatus.Playing || listening.Track is null)
            return null;

        return new OverlayCard(
            character,
            listening.Track.Title,
            listening.Track.Artist,
            IsLocal: true,
            IsStale: IsEffectivelyStale(listening, now),
            IsLastFm: true,
            ArtworkUrl: ArtworkUriPolicy.IsAllowed(listening.Track.AlbumArtUrl)
                ? listening.Track.AlbumArtUrl
                : null,
            TrackUrl: LastFmLinkPolicy.IsAllowed(listening.Track.TrackUrl)
                ? listening.Track.TrackUrl
                : null);
    }

    public static OverlayCard? RemoteListening(
        CharacterIdentity character,
        ListeningState listening,
        DateTimeOffset now)
    {
        var localShape = LocalListening(character, listening, now);
        return localShape is null ? null : localShape with { IsLocal = false };
    }

    public static OverlayCard RemotePlaceholder(CharacterIdentity character, int index) => new(
        character,
        $"Mock track {index + 1}",
        "Remote placement test",
        IsLocal: false);

    private static bool IsEffectivelyStale(ListeningState listening, DateTimeOffset now)
    {
        if (listening.IsStale || listening.ObservedAt is null)
            return listening.IsStale;

        return now >= listening.ObservedAt.Value.AddSeconds(60);
    }
}
