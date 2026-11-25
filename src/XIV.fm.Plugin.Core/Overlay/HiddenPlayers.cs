namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// A player whose listening cards are hidden on this device only.
/// </summary>
public sealed record HiddenPlayer(string Name, uint HomeWorldId, string HomeWorldName)
{
    public bool Matches(CharacterIdentity character) =>
        this.HomeWorldId != 0 &&
        this.HomeWorldId == character.HomeWorldId &&
        string.Equals(this.Name, character.Name, StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        string.IsNullOrWhiteSpace(this.HomeWorldName) ? this.Name : $"{this.Name} ({this.HomeWorldName})";
}

public static class HiddenPlayers
{
    public const int Maximum = 200;

    /// <summary>
    /// Drops entries that cannot match a character, including ones damaged in saved configuration.
    /// </summary>
    public static List<HiddenPlayer> Normalize(IEnumerable<HiddenPlayer?>? players) =>
        (players ?? [])
            .OfType<HiddenPlayer>()
            .Where(player => !string.IsNullOrWhiteSpace(player.Name) && player.HomeWorldId != 0)
            .DistinctBy(player => (player.Name.ToUpperInvariant(), player.HomeWorldId))
            .Take(Maximum)
            .ToList();

    public static bool IsHidden(IReadOnlyCollection<HiddenPlayer> players, CharacterIdentity character) =>
        players.Any(player => player.Matches(character));

    /// <returns>Whether the player was added.</returns>
    public static bool TryAdd(ICollection<HiddenPlayer> players, CharacterIdentity character, string homeWorldName)
    {
        if (character.HomeWorldId == 0 ||
            players.Count >= Maximum ||
            players.Any(player => player.Matches(character)))
        {
            return false;
        }

        players.Add(new HiddenPlayer(character.Name, character.HomeWorldId, homeWorldName));
        return true;
    }
}
