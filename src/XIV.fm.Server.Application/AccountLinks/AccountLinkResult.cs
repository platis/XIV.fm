using XIV.fm.Contracts.V1;
using XIV.fm.Server.Domain.AccountLinks;

namespace XIV.fm.Server.Application.AccountLinks;

public sealed record StartedAccountLink(
    AccountLinkSessionId SessionId,
    Uri AuthorizationUri,
    string LinkCredential,
    DateTimeOffset ExpiresAt);

public sealed record AccountLinkState(
    AccountLinkStatus Status,
    DateTimeOffset ExpiresAt,
    string? LastFmAccountName);
