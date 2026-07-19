using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Domain.Relays;

public sealed record Relay(
    Guid RelayId,
    string Name,
    string NormalizedName,
    AccountId OwnerAccountId,
    long MembershipRevision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);
