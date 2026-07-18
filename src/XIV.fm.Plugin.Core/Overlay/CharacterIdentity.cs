namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Identifies a player character without exposing game-object implementation details.
/// </summary>
public readonly record struct CharacterIdentity
{
    public CharacterIdentity(string name, uint homeWorldId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Character name is required.", nameof(name));

        this.Name = name.Trim();
        this.HomeWorldId = homeWorldId;
    }

    public string Name { get; }

    public uint HomeWorldId { get; }

    public bool Matches(CharacterIdentity other) =>
        this.HomeWorldId != 0 &&
        other.HomeWorldId != 0 &&
        this.HomeWorldId == other.HomeWorldId &&
        string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{this.Name}@{this.HomeWorldId}";
}
