using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Domain.Listening;
using DomainLocationScope = XIV.fm.Server.Domain.Presence.LocationScope;

namespace XIV.fm.Server.Application.Presence;

public sealed class PublicPresenceSnapshotService
{
    public const int MaximumEntries = 500;
    private const int MaximumCandidateHeartbeats = 2000;
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<DomainLocationScope, SemaphoreSlim> buildLocks = new();
    private readonly IPresenceStore presenceStore;
    private readonly IListeningStateStore listeningStateStore;
    private readonly IPublicPresenceSnapshotCache snapshotCache;
    private readonly ListeningFreshnessPolicy freshnessPolicy;
    private readonly IPresenceSnapshotTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    public PublicPresenceSnapshotService(
        IPresenceStore presenceStore,
        IListeningStateStore listeningStateStore,
        IPublicPresenceSnapshotCache snapshotCache,
        ListeningFreshnessPolicy freshnessPolicy,
        IPresenceSnapshotTelemetry telemetry,
        TimeProvider timeProvider)
    {
        this.presenceStore = presenceStore;
        this.listeningStateStore = listeningStateStore;
        this.snapshotCache = snapshotCache;
        this.freshnessPolicy = freshnessPolicy;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<PresenceSnapshot> GetAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var cached = await this.snapshotCache.GetAsync(location, cancellationToken).ConfigureAwait(false);
        var cacheHit = cached is not null && cached.ExpiresAt > now;
        this.telemetry.RecordSnapshotCacheRead(cacheHit);
        if (cacheHit)
            return cached!;

        var buildLock = this.buildLocks.GetOrAdd(location, _ => new SemaphoreSlim(1, 1));
        await buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = this.timeProvider.GetUtcNow();
            cached = await this.snapshotCache.GetAsync(location, cancellationToken).ConfigureAwait(false);
            if (cached is not null && cached.ExpiresAt > now)
                return cached;

            var heartbeats = await this.presenceStore.GetPublicAsync(
                location,
                now,
                MaximumCandidateHeartbeats,
                cancellationToken).ConfigureAwait(false);
            var entries = new List<PresenceEntry>();
            foreach (var heartbeat in heartbeats
                .Where(heartbeat => heartbeat.AccountId is not null)
                .GroupBy(heartbeat => heartbeat.AccountId!.Value)
                .Select(group => group.OrderByDescending(heartbeat => heartbeat.SeenAt).First())
                .OrderBy(heartbeat => heartbeat.Character.Name, StringComparer.Ordinal)
                .ThenBy(heartbeat => heartbeat.Character.HomeWorldId)
                .Take(MaximumEntries))
            {
                var listening = await this.listeningStateStore
                    .GetAsync(heartbeat.AccountId!.Value, cancellationToken)
                    .ConfigureAwait(false);
                entries.Add(new PresenceEntry(
                    new CharacterIdentity(heartbeat.Character.Name, heartbeat.Character.HomeWorldId),
                    MapListening(listening, now)));
            }

            var contractLocation = new LocationScope(
                location.CurrentWorldId,
                location.TerritoryId,
                location.MapId,
                location.InstanceId);
            var expiresAt = now.Add(SnapshotLifetime);
            var snapshot = new PresenceSnapshot(
                contractLocation,
                now,
                expiresAt,
                entries);
            await this.snapshotCache.SetAsync(location, snapshot, cancellationToken).ConfigureAwait(false);
            this.telemetry.RecordSnapshotBuild(entries.Count);
            return snapshot;
        }
        finally
        {
            buildLock.Release();
        }
    }

    public ValueTask InvalidateAsync(
        DomainLocationScope location,
        CancellationToken cancellationToken) =>
        this.snapshotCache.RemoveAsync(location, cancellationToken);

    public static string CreateVersion(PresenceSnapshot snapshot)
    {
        var material = new StringBuilder()
            .Append(snapshot.Location.CurrentWorldId).Append(':')
            .Append(snapshot.Location.TerritoryId).Append(':')
            .Append(snapshot.Location.MapId).Append(':')
            .Append(snapshot.Location.InstanceId).Append(':')
            .Append(snapshot.GeneratedAt.UtcTicks);
        foreach (var entry in snapshot.Entries)
        {
            material.Append('|').Append(entry.Character.Name)
                .Append('@').Append(entry.Character.HomeWorldId)
                .Append(':').Append(entry.Listening.Status)
                .Append(':').Append(entry.Listening.ObservedAt?.UtcTicks ?? 0);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())))
            .ToLowerInvariant();
    }

    private ListeningState MapListening(ListeningObservation? observation, DateTimeOffset now)
    {
        if (observation is null)
            return new ListeningState(ListeningStatus.Unavailable, false, null, null);

        return new ListeningState(
            observation.Status == ListeningObservationStatus.Playing
                ? ListeningStatus.Playing
                : ListeningStatus.NotPlaying,
            this.freshnessPolicy.IsStale(observation, now),
            observation.ObservedAt,
            observation.Track is null
                ? null
                : new Track(
                    observation.Track.Title,
                    observation.Track.Artist,
                    observation.Track.Album,
                    observation.Track.AlbumArtUrl,
                    observation.Track.TrackUrl,
                    observation.Track.StartedAt));
    }
}
