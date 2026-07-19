using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;

namespace XIV.fm.Server.Application.AccountLinks;

public sealed class AccountLinkApplicationService
{
    private readonly IAccountLinkStore store;
    private readonly ILastFmAuthorizationClient lastFm;
    private readonly TimeProvider timeProvider;
    private readonly AccountLinkOptions options;

    public AccountLinkApplicationService(
        IAccountLinkStore store,
        ILastFmAuthorizationClient lastFm,
        TimeProvider timeProvider,
        AccountLinkOptions options)
    {
        this.store = store;
        this.lastFm = lastFm;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    public async ValueTask<StartedAccountLink> StartAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var sessionId = new AccountLinkSessionId(Guid.NewGuid());
        var linkCredential = SecretGenerator.Generate();
        var callbackState = SecretGenerator.Generate();
        var callbackUri = CreateCallbackUri(this.options.PublicBaseUri, sessionId, callbackState);
        var authorizationUri = this.lastFm.CreateAuthorizationUri(callbackUri);
        var expiresAt = now.Add(this.options.Lifetime);
        await this.store.CreateAsync(
            new NewAccountLinkSession(
                sessionId,
                linkCredential,
                callbackState,
                null,
                now,
                expiresAt),
            cancellationToken).ConfigureAwait(false);
        return new StartedAccountLink(sessionId, authorizationUri, linkCredential, expiresAt);
    }

    public async ValueTask<bool> CompleteAsync(
        AccountLinkSessionId sessionId,
        string callbackState,
        string providerToken,
        CancellationToken cancellationToken)
    {
        if (!IsBoundedSecret(callbackState) || !IsBoundedSecret(providerToken))
            return false;

        var now = this.timeProvider.GetUtcNow();
        var claimed = await this.store.TryClaimAuthorizationAsync(
            sessionId,
            callbackState,
            providerToken,
            now,
            cancellationToken).ConfigureAwait(false);
        if (!claimed)
            return false;

        try
        {
            var identity = await this.lastFm
                .CompleteAuthorizationAsync(providerToken, cancellationToken)
                .ConfigureAwait(false);
            await this.store.CompleteAsync(sessionId, identity, now, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await this.store.FailAsync(
                sessionId,
                this.timeProvider.GetUtcNow(),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<AccountLinkState?> GetStateAsync(
        AccountLinkSessionId sessionId,
        string linkCredential,
        CancellationToken cancellationToken)
    {
        if (!IsBoundedSecret(linkCredential))
            return null;

        var stored = await this.store.GetAsync(sessionId, linkCredential, cancellationToken).ConfigureAwait(false);
        if (stored is null)
            return null;

        var status = stored.ExpiresAt <= this.timeProvider.GetUtcNow() &&
            stored.Status is not StoredAccountLinkStatus.Linked
                ? AccountLinkStatus.Expired
                : stored.Status switch
                {
                    StoredAccountLinkStatus.Linked => AccountLinkStatus.Linked,
                    StoredAccountLinkStatus.Failed => AccountLinkStatus.Failed,
                    _ => AccountLinkStatus.Pending,
                };
        return new AccountLinkState(status, stored.ExpiresAt, stored.LastFmAccountName);
    }

    private static bool IsBoundedSecret(string? value) =>
        value is not null && value.Length is >= 32 and <= 512 && !value.Any(char.IsWhiteSpace);

    private static Uri CreateCallbackUri(
        Uri publicBaseUri,
        AccountLinkSessionId sessionId,
        string callbackState)
    {
        var callback = new Uri(publicBaseUri, ApiRoutes.GetAccountLinkCallback(sessionId.Value));
        return new UriBuilder(callback) { Query = $"state={Uri.EscapeDataString(callbackState)}" }.Uri;
    }
}
