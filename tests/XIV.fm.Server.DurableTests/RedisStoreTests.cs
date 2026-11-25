using XIV.fm.Contracts.V1;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Domain.Listening;
using XIV.fm.Server.Domain.Presence;
using XIV.fm.Server.Infrastructure.LastFm;
using XIV.fm.Server.Infrastructure.Listening;
using XIV.fm.Server.Infrastructure.Presence;
using CharacterIdentity = XIV.fm.Server.Domain.Presence.CharacterIdentity;
using ContractCharacterIdentity = XIV.fm.Contracts.V1.CharacterIdentity;
using ContractLocationScope = XIV.fm.Contracts.V1.LocationScope;
using LocationScope = XIV.fm.Server.Domain.Presence.LocationScope;
using VisibilityMode = XIV.fm.Server.Domain.Presence.VisibilityMode;
using VisibilitySelection = XIV.fm.Server.Domain.Presence.VisibilitySelection;

namespace XIV.fm.Server.DurableTests;

[Collection(DurableStoresGroup.Name)]
public sealed class RedisStoreTests
{
    private readonly DurableStores stores;

    public RedisStoreTests(DurableStores stores)
    {
        this.stores = stores;
    }

    [Fact]
    public async Task OnlyOnePollLeaseIsHeldAtATime()
    {
        var leases = new RedisListeningPollLeaseStore(this.stores.Redis);
        var account = new AccountId(Guid.NewGuid());

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            await leases.TryAcquireAsync(account, TimeSpan.FromSeconds(30), CancellationToken.None))));
        var held = Assert.Single(attempts, lease => lease is not null);

        await held!.DisposeAsync();
        IAsyncDisposable? next = null;
        for (var attempt = 0; attempt < 50 && next is null; attempt++)
        {
            next = await leases.TryAcquireAsync(account, TimeSpan.FromSeconds(30), CancellationToken.None);
            if (next is null)
                await Task.Delay(20);
        }

        Assert.NotNull(next);
        await next.DisposeAsync();
    }

    [Fact]
    public async Task ListeningStateSurvivesSerialization()
    {
        var states = new RedisListeningStateStore(this.stores.Redis);
        var account = new AccountId(Guid.NewGuid());
        var observation = new ListeningObservation(
            ListeningObservationStatus.Playing,
            DateTimeOffset.UtcNow,
            new NormalizedTrack("Title", "Artist", "Album", null, new Uri("https://www.last.fm/music/x"), null));

        Assert.Null(await states.GetAsync(account, CancellationToken.None));
        await states.SetAsync(account, observation, CancellationToken.None);

        Assert.Equal(observation, await states.GetAsync(account, CancellationToken.None));

        await states.RemoveAsync(account, CancellationToken.None);
        Assert.Null(await states.GetAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task PublicPresenceFollowsLocationVisibilityAndRevocation()
    {
        var presence = new RedisPresenceStore(this.stores.Redis, TimeProvider.System);
        var installation = new InstallationId(Guid.NewGuid());
        var account = new AccountId(Guid.NewGuid());
        var first = CreateLocation();
        var second = CreateLocation();

        await presence.UpsertAsync(
            CreateHeartbeat(installation, account, first, VisibilityMode.Public),
            CancellationToken.None);
        Assert.Single(await GetPublicAsync(presence, first));

        await presence.UpsertAsync(
            CreateHeartbeat(installation, account, second, VisibilityMode.Public),
            CancellationToken.None);
        Assert.Empty(await GetPublicAsync(presence, first));
        Assert.Single(await GetPublicAsync(presence, second));

        await presence.UpsertAsync(
            CreateHeartbeat(installation, account, second, VisibilityMode.Private),
            CancellationToken.None);
        Assert.Empty(await GetPublicAsync(presence, second));

        await presence.UpsertAsync(
            CreateHeartbeat(installation, account, second, VisibilityMode.Public),
            CancellationToken.None);
        Assert.NotNull(await presence.RemoveInstallationAsync(installation, CancellationToken.None));
        Assert.Empty(await GetPublicAsync(presence, second));
    }

    [Fact]
    public async Task RemovingOneRelayKeepsTheOther()
    {
        var presence = new RedisPresenceStore(this.stores.Redis, TimeProvider.System);
        var account = new AccountId(Guid.NewGuid());
        var location = CreateLocation();
        var removed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await presence.UpsertAsync(
            CreateHeartbeat(
                new InstallationId(Guid.NewGuid()),
                account,
                location,
                VisibilityMode.Custom,
                removed,
                kept),
            CancellationToken.None);

        var locations = await presence.RemoveRelayPublicationAsync(account, removed, CancellationToken.None);

        Assert.Equal(location, Assert.Single(locations));
        Assert.Empty(await presence.GetRelayAsync(removed, location, DateTimeOffset.UtcNow, 10, CancellationToken.None));
        Assert.Single(await presence.GetRelayAsync(kept, location, DateTimeOffset.UtcNow, 10, CancellationToken.None));
    }

    [Fact]
    public async Task RelaySnapshotsAreRemovedTogether()
    {
        var cache = new RedisRelayPresenceSnapshotCache(this.stores.Redis, TimeProvider.System);
        var relay = Guid.NewGuid();
        var location = CreateLocation();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new PresenceSnapshot(
            new ContractLocationScope(
                location.CurrentWorldId,
                location.TerritoryId,
                location.MapId,
                location.InstanceId),
            now,
            now.AddSeconds(20),
            [
                new PresenceEntry(
                    new ContractCharacterIdentity("Alice Cat", 54),
                    new ListeningState(ListeningStatus.NotPlaying, false, now, null)),
            ]);
        await cache.SetAsync(relay, 1, location, snapshot, CancellationToken.None);
        await cache.SetAsync(relay, 2, location, snapshot, CancellationToken.None);
        Assert.Single((await cache.GetAsync(relay, 1, location, CancellationToken.None))!.Entries);

        await cache.RemoveRelayAsync(relay, CancellationToken.None);

        Assert.Null(await cache.GetAsync(relay, 1, location, CancellationToken.None));
        Assert.Null(await cache.GetAsync(relay, 2, location, CancellationToken.None));
    }

    [Fact]
    public async Task RequestBudgetLimitsABurst()
    {
        await this.stores.Redis.GetDatabase().KeyDeleteAsync("xivfm:lastfm:global-request-budget");
        var budget = new RedisLastFmRequestBudget(this.stores.Redis);
        var started = DateTimeOffset.UtcNow;

        for (var request = 0; request < 11; request++)
            await budget.AcquireAsync(CancellationToken.None);

        // Seven immediate requests, then 3.5 per second.
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromSeconds(0.9));
    }

    private static async Task<IReadOnlyList<PresenceHeartbeat>> GetPublicAsync(
        RedisPresenceStore presence,
        LocationScope location) =>
        await presence.GetPublicAsync(location, DateTimeOffset.UtcNow, 10, CancellationToken.None);

    private static LocationScope CreateLocation() => new(
        (uint)Random.Shared.Next(1, int.MaxValue),
        (uint)Random.Shared.Next(1, int.MaxValue),
        1,
        0);

    private static PresenceHeartbeat CreateHeartbeat(
        InstallationId installation,
        AccountId account,
        LocationScope location,
        VisibilityMode mode,
        params Guid[] relayIds)
    {
        var now = DateTimeOffset.UtcNow;
        return new PresenceHeartbeat(
            installation,
            account,
            new CharacterIdentity("Alice Cat", 54),
            location,
            new VisibilitySelection(mode, relayIds),
            now,
            now.AddSeconds(60));
    }
}
