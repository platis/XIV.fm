using Microsoft.EntityFrameworkCore;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class PostgresAccountLinkStoreTests
{
    private readonly DurableStores stores;
    private readonly PostgresAccountLinkStore links;

    public PostgresAccountLinkStoreTests(DurableStores stores)
    {
        this.stores = stores;
        this.links = new PostgresAccountLinkStore(stores.Contexts);
    }

    [Fact]
    public async Task SimultaneousLinksOfANewAccountShareOneAccount()
    {
        var name = $"listener-{Guid.NewGuid():N}";
        var sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => this.AuthorizeAsync()));

        await Task.WhenAll(sessions.Select(session => Task.Run(async () => await this.links.CompleteAsync(
            session.SessionId,
            new LastFmAccountIdentity(name),
            DateTimeOffset.UtcNow,
            CancellationToken.None))));

        var credentials = new PostgresInstallationCredentialStore(this.stores.Contexts, TimeProvider.System);
        var accounts = new HashSet<AccountId>();
        foreach (var session in sessions)
        {
            var installation = await credentials.AuthenticateAsync(session.LinkCredential, CancellationToken.None);
            Assert.NotNull(installation);
            var account = await this.links.GetForInstallationAsync(installation.Value, CancellationToken.None);
            Assert.NotNull(account);
            accounts.Add(account.AccountId);
        }

        Assert.Single(accounts);
    }

    [Fact]
    public async Task CompletedSessionCannotIssueASecondCredential()
    {
        var session = await this.AuthorizeAsync();
        var identity = new LastFmAccountIdentity($"listener-{Guid.NewGuid():N}");
        await this.links.CompleteAsync(session.SessionId, identity, DateTimeOffset.UtcNow, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await this.links.CompleteAsync(
            session.SessionId,
            identity,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
    }

    [Fact]
    public async Task RevokedInstallationNoLongerAuthenticates()
    {
        var credentials = new PostgresInstallationCredentialStore(this.stores.Contexts, TimeProvider.System);
        var issued = await credentials.ProvisionAsync(CancellationToken.None);
        var rotated = await credentials.RotateAndIssueAsync(issued.InstallationId, CancellationToken.None);
        Assert.Null(await credentials.AuthenticateAsync(issued.Credential, CancellationToken.None));
        Assert.NotNull(await credentials.AuthenticateAsync(rotated, CancellationToken.None));

        await credentials.RevokeAsync(issued.InstallationId, CancellationToken.None);

        Assert.Null(await credentials.AuthenticateAsync(rotated, CancellationToken.None));
    }

    [Fact]
    public async Task InstallationsAreListedWithCoarseLastUse()
    {
        var identity = new LastFmAccountIdentity($"listener-{Guid.NewGuid():N}");
        var first = await this.AuthorizeAsync();
        var second = await this.AuthorizeAsync();
        await this.links.CompleteAsync(first.SessionId, identity, DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        await this.links.CompleteAsync(second.SessionId, identity, DateTimeOffset.UtcNow, CancellationToken.None);
        var clock = new AdjustableTimeProvider();
        var credentials = new PostgresInstallationCredentialStore(this.stores.Contexts, clock);
        var firstId = (await credentials.AuthenticateAsync(first.LinkCredential, CancellationToken.None))!.Value;
        var account = (await this.links.GetForInstallationAsync(firstId, CancellationToken.None))!.AccountId;

        var listed = await this.links.ListInstallationsAsync(account, CancellationToken.None);
        Assert.Equal(2, listed.Count);
        Assert.Null(listed[0].LastUsedAt);
        Assert.Equal(firstId, listed[1].InstallationId);
        var firstUse = listed[1].LastUsedAt;
        Assert.NotNull(firstUse);

        clock.Now = clock.Now.AddHours(5);
        await credentials.AuthenticateAsync(first.LinkCredential, CancellationToken.None);
        Assert.Equal(firstUse, (await this.links.ListInstallationsAsync(account, CancellationToken.None))[1].LastUsedAt);
        clock.Now = clock.Now.AddHours(1).AddSeconds(1);
        await credentials.AuthenticateAsync(first.LinkCredential, CancellationToken.None);
        Assert.True((await this.links.ListInstallationsAsync(account, CancellationToken.None))[1].LastUsedAt > firstUse);

        await credentials.RevokeAsync(firstId, CancellationToken.None);
        Assert.Single(await this.links.ListInstallationsAsync(account, CancellationToken.None));
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    private async Task<NewAccountLinkSession> AuthorizeAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new NewAccountLinkSession(
            new AccountLinkSessionId(Guid.NewGuid()),
            $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
            $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
            null,
            now,
            now.AddMinutes(10));
        await this.links.CreateAsync(session, CancellationToken.None);
        Assert.True(await this.links.TryClaimAuthorizationAsync(
            session.SessionId,
            session.CallbackState,
            $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
            now,
            CancellationToken.None));
        return session;
    }
}
