namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Temporary content used to validate nameplate anchoring before visual design begins.
/// </summary>
public sealed record PlaceholderCard(string Title, string Artist)
{
    public static PlaceholderCard Default { get; } = new("Placeholder track", "XIV.fm development build");
}
