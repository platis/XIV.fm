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
    bool IsLastFm = false)
{
    public static OverlayCard LocalPlaceholder(CharacterIdentity character) => new(
        character,
        PlaceholderCard.Default.Title,
        PlaceholderCard.Default.Artist,
        IsLocal: true);

    public static OverlayCard LocalListening(
        CharacterIdentity character,
        ListeningState listening,
        DateTimeOffset now) => listening.Status switch
        {
            ListeningStatus.Playing when listening.Track is not null => new OverlayCard(
                character,
                listening.Track.Title,
                listening.Track.Artist,
                IsLocal: true,
                IsStale: IsEffectivelyStale(listening, now),
                IsLastFm: true),
            ListeningStatus.NotPlaying => new OverlayCard(
                character,
                "Nothing playing",
                "No current track",
                IsLocal: true,
                IsStale: IsEffectivelyStale(listening, now),
                IsLastFm: true),
            _ => new OverlayCard(
                character,
                "Listening unavailable",
                "Waiting for Last.fm",
                IsLocal: true,
                IsStale: true,
                IsLastFm: true),
        };

    public static OverlayCard RemoteListening(
        CharacterIdentity character,
        ListeningState listening,
        DateTimeOffset now)
    {
        var localShape = LocalListening(character, listening, now);
        return localShape with { IsLocal = false };
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

        var maximumAge = listening.Status == ListeningStatus.Playing
            ? TimeSpan.FromSeconds(60)
            : TimeSpan.FromSeconds(180);
        return now >= listening.ObservedAt.Value.Add(maximumAge);
    }
}
