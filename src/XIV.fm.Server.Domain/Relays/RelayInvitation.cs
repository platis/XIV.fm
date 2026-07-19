namespace XIV.fm.Server.Domain.Relays;

public sealed record RelayInvitation(
    Guid InvitationId,
    Guid RelayId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt);
