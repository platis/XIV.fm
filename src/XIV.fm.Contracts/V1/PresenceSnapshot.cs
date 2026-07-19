namespace XIV.fm.Contracts.V1;

public sealed record PresenceEntry(
    CharacterIdentity Character,
    ListeningState Listening);

public sealed record PresenceSnapshot(
    LocationScope Location,
    DateTimeOffset GeneratedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<PresenceEntry> Entries);

/// <summary>
/// Carries a shared snapshot, or only its version when the client's known version is current.
/// </summary>
public sealed record SnapshotResult(
    string Version,
    PresenceSnapshot? Snapshot);
