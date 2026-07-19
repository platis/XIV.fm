using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Application.Abstractions;

public enum StoredAccountLinkStatus
{
    Pending,
    Authorizing,
    Linked,
    Failed,
}

public sealed record NewAccountLinkSession(
    AccountLinkSessionId SessionId,
    string LinkCredential,
    string CallbackState,
    string? ProviderToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record StoredAccountLink(
    StoredAccountLinkStatus Status,
    DateTimeOffset ExpiresAt,
    string? LastFmAccountName);

public interface IAccountLinkStore
{
    ValueTask CreateAsync(NewAccountLinkSession session, CancellationToken cancellationToken);

    ValueTask<bool> TryClaimAuthorizationAsync(
        AccountLinkSessionId sessionId,
        string callbackState,
        string providerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask CompleteAsync(
        AccountLinkSessionId sessionId,
        LastFmAccountIdentity identity,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask FailAsync(
        AccountLinkSessionId sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<StoredAccountLink?> GetAsync(
        AccountLinkSessionId sessionId,
        string linkCredential,
        CancellationToken cancellationToken);
}
