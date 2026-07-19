namespace XIV.fm.Server.Domain.Presence;

public sealed record CharacterIdentity
{
    public CharacterIdentity(string name, uint homeWorldId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Character name is required.", nameof(name));
        ArgumentOutOfRangeException.ThrowIfZero(homeWorldId);

        this.Name = name.Trim();
        this.HomeWorldId = homeWorldId;
    }

    public string Name { get; }

    public uint HomeWorldId { get; }
}
