using System.Collections.Immutable;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;
using CoreCharacterIdentity = XIV.fm.Plugin.Core.Overlay.CharacterIdentity;

namespace XIV.fm.Plugin.Core.Presence;

public sealed class RemotePresenceStateStore
{
    /// <summary>
    /// Keeps cards visible while the next sync replaces an expired snapshot. A snapshot can expire at any
    /// point between two syncs, so this covers one full sync interval plus request time.
    /// </summary>
    public static readonly TimeSpan ReplacementGrace = TimeSpan.FromSeconds(8);

    private readonly Lock gate = new();
    private ImmutableArray<OverlayCard> cards = [];
    private DateTimeOffset visibleUntil = DateTimeOffset.MinValue;

    public bool Apply(
        SnapshotResult result,
        XIV.fm.Contracts.V1.LocationScope expectedLocation,
        DateTimeOffset now)
    {
        if (result.Snapshot is null)
            return true;
        if (result.Snapshot.Location != expectedLocation ||
            result.Snapshot.GeneratedAt > result.Snapshot.ExpiresAt ||
            result.Snapshot.Entries.Count > 500)
        {
            this.Clear();
            return false;
        }

        var retainUntil = result.Snapshot.ExpiresAt.Add(ReplacementGrace);
        var mapped = retainUntil <= now
            ? ImmutableArray<OverlayCard>.Empty
            : result.Snapshot.Entries
                .Select(entry => OverlayCard.RemoteListening(
                    new CoreCharacterIdentity(
                        entry.Character.Name,
                        entry.Character.HomeWorldId),
                    entry.Listening,
                    now))
                .OfType<OverlayCard>()
                .ToImmutableArray();
        lock (this.gate)
        {
            this.cards = mapped;
            this.visibleUntil = retainUntil;
        }

        return true;
    }

    public ImmutableArray<OverlayCard> Read(DateTimeOffset now)
    {
        lock (this.gate)
            return now < this.visibleUntil ? this.cards : [];
    }

    public void Clear()
    {
        lock (this.gate)
        {
            this.cards = [];
            this.visibleUntil = DateTimeOffset.MinValue;
        }
    }
}
