namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class RelayEntity
{
    public Guid RelayId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public Guid OwnerAccountId { get; set; }
    public LastFmAccountEntity OwnerAccount { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public long MembershipRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<RelayMembershipEntity> Memberships { get; set; } = [];
    public List<RelayInvitationEntity> Invitations { get; set; } = [];
}
