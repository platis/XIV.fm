namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class RelayInvitationEntity
{
    public Guid InvitationId { get; set; }
    public Guid RelayId { get; set; }
    public RelayEntity Relay { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedByAccountId { get; set; }
    public LastFmAccountEntity? AcceptedByAccount { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
