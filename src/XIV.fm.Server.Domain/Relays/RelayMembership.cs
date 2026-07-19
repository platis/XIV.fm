using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Domain.Relays;

public sealed record RelayMembership(
    Guid MembershipId,
    Guid RelayId,
    AccountId AccountId,
    DateTimeOffset JoinedAt);
