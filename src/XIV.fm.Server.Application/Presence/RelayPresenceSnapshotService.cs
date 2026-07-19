using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Application.Presence;

public sealed record AuthorizedRelaySnapshot(PresenceSnapshot Snapshot, string Version);

public sealed class RelayPresenceSnapshotService
{
    private const int MaximumCandidateHeartbeats = 2000;
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<BuildKey, SemaphoreSlim> buildLocks = new();
    private readonly IRelayStore relayStore;
    private readonly IPresenceStore presenceStore;
    private readonly IListeningStateStore listeningStateStore;
    private readonly IRelayPresenceSnapshotCache snapshotCache;
    private readonly ListeningFreshnessPolicy freshnessPolicy;
    private readonly IPresenceSnapshotTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    public RelayPresenceSnapshotService(
        IRelayStore relayStore,
        IPresenceStore presenceStore,
        IListeningStateStore listeningStateStore,
        IRelayPresenceSnapshotCache snapshotCache,
        ListeningFreshnessPolicy freshnessPolicy,
        IPresenceSnapshotTelemetry telemetry,
        TimeProvider timeProvider)
    {
        this.relayStore = relayStore;
        this.presenceStore = presenceStore;
        this.listeningStateStore = listeningStateStore;
        this.snapshotCache = snapshotCache;
        this.freshnessPolicy = freshnessPolicy;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<AuthorizedRelaySnapshot?> GetAuthorizedUnionAsync(
        AccountId viewerAccountId,
        IReadOnlyList<Guid> relayIds,
        DomainLocationScope location,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var revisions = await this.relayStore.GetMembershipRevisionsAsync(viewerAccountId, relayIds, cancellationToken).ConfigureAwait(false);
            if (revisions is null)
                return null;
            var snapshots = new List<PresenceSnapshot>(relayIds.Count);
            foreach (var relayId in relayIds)
                snapshots.Add(await this.GetRelayAsync(relayId, revisions[relayId], location, cancellationToken).ConfigureAwait(false));

            var confirmed = await this.relayStore.GetMembershipRevisionsAsync(viewerAccountId, relayIds, cancellationToken).ConfigureAwait(false);
            if (confirmed is null)
                return null;
            if (!revisions.All(pair => confirmed.TryGetValue(pair.Key, out var revision) && revision == pair.Value))
                continue;

            var now = this.timeProvider.GetUtcNow();
            var entries = snapshots.SelectMany(snapshot => snapshot.Entries)
                .GroupBy(entry => (entry.Character.Name, entry.Character.HomeWorldId))
                .Select(group => group.OrderByDescending(entry => entry.Listening.ObservedAt).First())
                .OrderBy(entry => entry.Character.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Character.HomeWorldId)
                .Take(PublicPresenceSnapshotService.MaximumEntries)
                .ToArray();
            var expiresAt = snapshots.Count == 0 ? now.Add(SnapshotLifetime) : snapshots.Min(snapshot => snapshot.ExpiresAt);
            var union = new PresenceSnapshot(
                new LocationScope(location.CurrentWorldId, location.TerritoryId, location.MapId, location.InstanceId),
                now,
                expiresAt,
                entries);
            var versionMaterial = string.Join('|', relayIds.Order().Select(id => $"{id:D}:{revisions[id]}").Concat(snapshots.Select(PublicPresenceSnapshotService.CreateVersion)));
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionMaterial))).ToLowerInvariant();
            return new AuthorizedRelaySnapshot(union, version);
        }

        return null;
    }

    public async ValueTask InvalidateAsync(IReadOnlyList<Guid> relayIds, DomainLocationScope location, CancellationToken cancellationToken)
    {
        foreach (var relayId in relayIds)
            await this.snapshotCache.RemoveAsync(relayId, location, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PresenceSnapshot> GetRelayAsync(Guid relayId, long revision, DomainLocationScope location, CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var cached = await this.snapshotCache.GetAsync(relayId, revision, location, cancellationToken).ConfigureAwait(false);
        var hit = cached is not null && cached.ExpiresAt > now;
        this.telemetry.RecordSnapshotCacheRead(hit);
        if (hit)
            return cached!;

        var key = new BuildKey(relayId, revision, location);
        var buildLock = this.buildLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = this.timeProvider.GetUtcNow();
            cached = await this.snapshotCache.GetAsync(relayId, revision, location, cancellationToken).ConfigureAwait(false);
            if (cached is not null && cached.ExpiresAt > now)
                return cached;
            var heartbeats = await this.presenceStore.GetRelayAsync(relayId, location, now, MaximumCandidateHeartbeats, cancellationToken).ConfigureAwait(false);
            var candidates = heartbeats.Where(heartbeat => heartbeat.AccountId is not null).Select(heartbeat => heartbeat.AccountId!.Value).Distinct().ToArray();
            var members = await this.relayStore.GetCurrentMembersAsync(relayId, candidates, cancellationToken).ConfigureAwait(false);
            var entries = new List<PresenceEntry>();
            foreach (var heartbeat in heartbeats
                .Where(heartbeat => heartbeat.AccountId is AccountId accountId && members.Contains(accountId))
                .GroupBy(heartbeat => heartbeat.AccountId!.Value)
                .Select(group => group.OrderByDescending(heartbeat => heartbeat.SeenAt).First())
                .OrderBy(heartbeat => heartbeat.Character.Name, StringComparer.Ordinal)
                .ThenBy(heartbeat => heartbeat.Character.HomeWorldId)
                .Take(PublicPresenceSnapshotService.MaximumEntries))
            {
                var listening = await this.listeningStateStore.GetAsync(heartbeat.AccountId!.Value, cancellationToken).ConfigureAwait(false);
                entries.Add(new PresenceEntry(new CharacterIdentity(heartbeat.Character.Name, heartbeat.Character.HomeWorldId), this.MapListening(listening, now)));
            }
            var snapshot = new PresenceSnapshot(
                new LocationScope(location.CurrentWorldId, location.TerritoryId, location.MapId, location.InstanceId),
                now,
                now.Add(SnapshotLifetime),
                entries);
            await this.snapshotCache.SetAsync(relayId, revision, location, snapshot, cancellationToken).ConfigureAwait(false);
            this.telemetry.RecordSnapshotBuild(entries.Count);
            return snapshot;
        }
        finally
        {
            buildLock.Release();
        }
    }

    private ListeningState MapListening(ListeningObservation? observation, DateTimeOffset now)
    {
        if (observation is null)
            return new ListeningState(ListeningStatus.Unavailable, false, null, null);
        return new ListeningState(
            observation.Status == ListeningObservationStatus.Playing ? ListeningStatus.Playing : ListeningStatus.NotPlaying,
            this.freshnessPolicy.IsStale(observation, now),
            observation.ObservedAt,
            observation.Track is null ? null : new Track(
                observation.Track.Title,
                observation.Track.Artist,
                observation.Track.Album,
                observation.Track.AlbumArtUrl,
                observation.Track.TrackUrl,
                observation.Track.StartedAt));
    }

    private sealed record BuildKey(Guid RelayId, long Revision, DomainLocationScope Location);
}
