using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Domain.Presence;

public sealed record PresenceHeartbeat(
    InstallationId InstallationId,
    CharacterIdentity Character,
    LocationScope Location,
    VisibilitySelection Visibility,
    DateTimeOffset SeenAt,
    DateTimeOffset ExpiresAt);
