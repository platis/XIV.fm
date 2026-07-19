namespace XIV.fm.Contracts.V1;

/// <summary>
/// Identifies a character by canonical display name and home world.
/// </summary>
public sealed record CharacterIdentity(string Name, uint HomeWorldId);
