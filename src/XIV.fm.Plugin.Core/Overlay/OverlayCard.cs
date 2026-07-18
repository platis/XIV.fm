namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Immutable presentation state for one character card.
/// </summary>
public sealed record OverlayCard(
    CharacterIdentity Character,
    string Title,
    string Artist,
    bool IsLocal)
{
    public static OverlayCard LocalPlaceholder(CharacterIdentity character) => new(
        character,
        PlaceholderCard.Default.Title,
        PlaceholderCard.Default.Artist,
        IsLocal: true);

    public static OverlayCard RemotePlaceholder(CharacterIdentity character, int index) => new(
        character,
        $"Mock track {index + 1}",
        "Remote placement test",
        IsLocal: false);
}
