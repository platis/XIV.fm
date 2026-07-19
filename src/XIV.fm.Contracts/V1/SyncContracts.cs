namespace XIV.fm.Contracts.V1;

/// <summary>
/// Publishes one authenticated heartbeat and requests cached listening and location presence state.
/// Duty-bound clients do not send this request.
/// </summary>
public sealed record SyncRequest(
    string PluginVersion,
    CharacterIdentity Character,
    LocationScope Location,
    VisibilitySelection Visibility,
    string? KnownSnapshotVersion);

public sealed record SyncResponse(
    DateTimeOffset ServerTime,
    DateTimeOffset PresenceExpiresAt,
    int NextSyncAfterSeconds,
    ListeningState OwnListening,
    SnapshotResult LocationPresence);
