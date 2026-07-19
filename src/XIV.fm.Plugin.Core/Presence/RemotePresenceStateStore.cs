using System.Collections.Immutable;
using XIV.fm.Contracts.V1;
using XIV.fm.Plugin.Core.Overlay;
using CoreCharacterIdentity = XIV.fm.Plugin.Core.Overlay.CharacterIdentity;

namespace XIV.fm.Plugin.Core.Presence;

public sealed class RemotePresenceStateStore
{
    private readonly Lock gate = new();
    private ImmutableArray<OverlayCard> cards = [];
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

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

        var mapped = result.Snapshot.ExpiresAt <= now
            ? ImmutableArray<OverlayCard>.Empty
            : result.Snapshot.Entries
                .Select(entry => OverlayCard.RemoteListening(
                    new CoreCharacterIdentity(
                        entry.Character.Name,
                        entry.Character.HomeWorldId),
                    entry.Listening,
                    now))
                .ToImmutableArray();
        lock (this.gate)
        {
            this.cards = mapped;
            this.expiresAt = result.Snapshot.ExpiresAt;
        }

        return true;
    }

    public ImmutableArray<OverlayCard> Read(DateTimeOffset now)
    {
        lock (this.gate)
            return now < this.expiresAt ? this.cards : [];
    }

    public void Clear()
    {
        lock (this.gate)
        {
            this.cards = [];
            this.expiresAt = DateTimeOffset.MinValue;
        }
    }
}
