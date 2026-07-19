using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Authentication;

namespace XIV.fm.Server.Infrastructure.AccountLinks;

public sealed class InMemoryAccountLinkStore : IAccountLinkStore, ILinkedAccountResolver
{
    private readonly Lock gate = new();
    private readonly Dictionary<AccountLinkSessionId, Session> sessions = [];
    private readonly Dictionary<string, Account> accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<InstallationId, Account> accountsByInstallation = [];
    private readonly InMemoryInstallationCredentialStore credentials;

    public InMemoryAccountLinkStore(InMemoryInstallationCredentialStore credentials)
    {
        this.credentials = credentials;
    }

    public ValueTask CreateAsync(NewAccountLinkSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stored = new Session(
            SecretHash.Compute(session.LinkCredential),
            SecretHash.Compute(session.CallbackState),
            SecretHash.Compute(session.ProviderToken),
            StoredAccountLinkStatus.Pending,
            session.ExpiresAt,
            null);
        lock (this.gate)
        {
            if (!this.sessions.TryAdd(session.SessionId, stored))
                throw new InvalidOperationException("The account-link session already exists.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryClaimAuthorizationAsync(
        AccountLinkSessionId sessionId,
        string callbackState,
        string providerToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (!this.sessions.TryGetValue(sessionId, out var session) ||
                session.Status != StoredAccountLinkStatus.Pending ||
                session.ExpiresAt <= now ||
                !StringComparer.Ordinal.Equals(session.CallbackStateHash, SecretHash.Compute(callbackState)) ||
                !StringComparer.Ordinal.Equals(session.ProviderTokenHash, SecretHash.Compute(providerToken)))
            {
                return ValueTask.FromResult(false);
            }

            session.Status = StoredAccountLinkStatus.Authorizing;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask CompleteAsync(
        AccountLinkSessionId sessionId,
        LastFmAccountIdentity identity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (!this.sessions.TryGetValue(sessionId, out var session) ||
                session.Status != StoredAccountLinkStatus.Authorizing ||
                session.ExpiresAt <= now)
            {
                throw new InvalidOperationException("The account-link session cannot be completed.");
            }

            if (!this.accounts.TryGetValue(identity.NormalizedName, out var account))
            {
                account = new Account(new AccountId(Guid.NewGuid()), identity.CanonicalName);
                this.accounts.Add(identity.NormalizedName, account);
            }

            var installationId = new InstallationId(Guid.NewGuid());
            this.credentials.RegisterHash(installationId, session.LinkCredentialHash);
            this.accountsByInstallation.Add(installationId, account);
            session.Account = account;
            session.Status = StoredAccountLinkStatus.Linked;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask FailAsync(
        AccountLinkSessionId sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (this.sessions.TryGetValue(sessionId, out var session) &&
                session.Status == StoredAccountLinkStatus.Authorizing)
            {
                session.Status = StoredAccountLinkStatus.Failed;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<LinkedLastFmAccount?> GetForInstallationAsync(
        InstallationId installationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            return ValueTask.FromResult(
                this.accountsByInstallation.TryGetValue(installationId, out var account)
                    ? new LinkedLastFmAccount(
                        account.AccountId,
                        new LastFmAccountIdentity(account.CanonicalName))
                    : null);
        }
    }

    public string? GetAccountName(AccountId accountId)
    {
        lock (this.gate)
        {
            return this.accounts.Values.FirstOrDefault(account => account.AccountId == accountId)?.CanonicalName;
        }
    }

    public ValueTask<StoredAccountLink?> GetAsync(
        AccountLinkSessionId sessionId,
        string linkCredential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hash = SecretHash.Compute(linkCredential);
        lock (this.gate)
        {
            if (!this.sessions.TryGetValue(sessionId, out var session) ||
                !StringComparer.Ordinal.Equals(hash, session.LinkCredentialHash))
            {
                return ValueTask.FromResult<StoredAccountLink?>(null);
            }

            return ValueTask.FromResult<StoredAccountLink?>(
                new StoredAccountLink(session.Status, session.ExpiresAt, session.Account?.CanonicalName));
        }
    }

    private sealed class Session
    {
        public Session(
            string linkCredentialHash,
            string callbackStateHash,
            string providerTokenHash,
            StoredAccountLinkStatus status,
            DateTimeOffset expiresAt,
            Account? account)
        {
            this.LinkCredentialHash = linkCredentialHash;
            this.CallbackStateHash = callbackStateHash;
            this.ProviderTokenHash = providerTokenHash;
            this.Status = status;
            this.ExpiresAt = expiresAt;
            this.Account = account;
        }

        public string LinkCredentialHash { get; }

        public string CallbackStateHash { get; }

        public string ProviderTokenHash { get; }

        public StoredAccountLinkStatus Status { get; set; }

        public DateTimeOffset ExpiresAt { get; }

        public Account? Account { get; set; }
    }

    private sealed record Account(AccountId AccountId, string CanonicalName);
}
