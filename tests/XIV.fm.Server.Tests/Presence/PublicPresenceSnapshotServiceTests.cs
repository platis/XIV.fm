using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Application.Presence;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Domain.Listening;
using XIV.fm.Server.Domain.Presence;
using XIV.fm.Server.Infrastructure.Listening;
using XIV.fm.Server.Infrastructure.Presence;

namespace XIV.fm.Server.Tests.Presence;

public sealed class PublicPresenceSnapshotServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly LocationScope Location = new(63, 129, 130, 2);

    [Fact]
    public async Task SnapshotIsExactLocationScopedAndExcludesExpiredPresence()
    {
        var presence = new InMemoryPresenceStore();
        var listening = new InMemoryListeningStateStore();
        var service = CreateService(presence, listening);
        var included = CreateHeartbeat("Alice Cat", Location, Now.AddMinutes(1));
        var otherInstance = CreateHeartbeat("Other Instance", new LocationScope(63, 129, 130, 3), Now.AddMinutes(1));
        var expired = CreateHeartbeat("Expired User", Location, Now.AddSeconds(-1));
        await presence.UpsertAsync(included, CancellationToken.None);
        await presence.UpsertAsync(otherInstance, CancellationToken.None);
        await presence.UpsertAsync(expired, CancellationToken.None);
        await listening.SetAsync(
            included.AccountId!.Value,
            new ListeningObservation(
                ListeningObservationStatus.Playing,
                Now,
                new NormalizedTrack("Track", "Artist", null, null, null, null)),
            CancellationToken.None);

        var snapshot = await service.GetAsync(Location, CancellationToken.None);

        var entry = Assert.Single(snapshot.Entries);
        Assert.Equal("Alice Cat", entry.Character.Name);
        Assert.Equal(XIV.fm.Contracts.V1.ListeningStatus.Playing, entry.Listening.Status);
        Assert.Equal(2U, snapshot.Location.InstanceId);
    }

    [Fact]
    public async Task SharedSnapshotIsReusedUntilExplicitInvalidation()
    {
        var presence = new InMemoryPresenceStore();
        var listening = new InMemoryListeningStateStore();
        var service = CreateService(presence, listening);
        await presence.UpsertAsync(
            CreateHeartbeat("Alice Cat", Location, Now.AddMinutes(1)),
            CancellationToken.None);
        var first = await service.GetAsync(Location, CancellationToken.None);
        await presence.UpsertAsync(
            CreateHeartbeat("Bob Bun", Location, Now.AddMinutes(1)),
            CancellationToken.None);

        var shared = await service.GetAsync(Location, CancellationToken.None);
        await service.InvalidateAsync(Location, CancellationToken.None);
        var rebuilt = await service.GetAsync(Location, CancellationToken.None);

        Assert.Same(first, shared);
        Assert.Single(shared.Entries);
        Assert.Equal(2, rebuilt.Entries.Count);
        Assert.NotEqual(
            PublicPresenceSnapshotService.CreateVersion(shared),
            PublicPresenceSnapshotService.CreateVersion(rebuilt));
    }

    [Fact]
    public async Task CrowdedSnapshotIsBoundedToFiveHundredEntries()
    {
        var presence = new InMemoryPresenceStore();
        var listening = new InMemoryListeningStateStore();
        var service = CreateService(presence, listening);
        for (var index = 0; index < PublicPresenceSnapshotService.MaximumEntries + 25; index++)
        {
            await presence.UpsertAsync(
                CreateHeartbeat($"Player {index:D3}", Location, Now.AddMinutes(1)),
                CancellationToken.None);
        }

        var snapshot = await service.GetAsync(Location, CancellationToken.None);

        Assert.Equal(PublicPresenceSnapshotService.MaximumEntries, snapshot.Entries.Count);
    }

    private static PublicPresenceSnapshotService CreateService(
        IPresenceStore presence,
        IListeningStateStore listening)
    {
        var options = ListeningPollingOptions.Default;
        return new PublicPresenceSnapshotService(
            presence,
            listening,
            new InMemoryPublicPresenceSnapshotCache(),
            new ListeningFreshnessPolicy(options),
            NullPresenceSnapshotTelemetry.Instance,
            new FixedTimeProvider());
    }

    private static PresenceHeartbeat CreateHeartbeat(
        string name,
        LocationScope location,
        DateTimeOffset expiresAt)
    {
        var accountId = new AccountId(Guid.NewGuid());
        return new PresenceHeartbeat(
            new InstallationId(Guid.NewGuid()),
            accountId,
            new CharacterIdentity(name, 54),
            location,
            new VisibilitySelection(VisibilityMode.Public, []),
            Now,
            expiresAt);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
