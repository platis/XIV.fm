using XIV.fm.Server.Domain.AccountLinks;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class CachedLinkedAccountResolverTests
{
    private readonly DurableStores stores;

    public CachedLinkedAccountResolverTests(DurableStores stores)
    {
        this.stores = stores;
    }

    [Fact]
    public async Task MissingLinksAreNotRememberedAndFoundLinksAre()
    {
        var links = new PostgresAccountLinkStore(this.stores.Contexts);
        var credentials = new PostgresInstallationCredentialStore(this.stores.Contexts, TimeProvider.System);
        var clock = new Clock();
        var resolver = new CachedLinkedAccountResolver(links, clock);
        var account = await this.stores.CreateAccountAsync();
        var installation = (await credentials.ProvisionAsync(CancellationToken.None)).InstallationId;
        Assert.Null(await resolver.GetForInstallationAsync(installation, CancellationToken.None));

        await using (var context = await this.stores.Contexts.CreateDbContextAsync())
        {
            (await context.InstallationCredentials.FindAsync(installation.Value))!.AccountId = account.Value;
            await context.SaveChangesAsync();
        }

        Assert.Equal(account, (await resolver.GetForInstallationAsync(installation, CancellationToken.None))!.AccountId);
        await credentials.RevokeAsync(installation, CancellationToken.None);
        Assert.NotNull(await resolver.GetForInstallationAsync(installation, CancellationToken.None));

        clock.Now = clock.Now.AddMinutes(1);
        Assert.Null(await resolver.GetForInstallationAsync(installation, CancellationToken.None));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => this.Now;
    }
}
