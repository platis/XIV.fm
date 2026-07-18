namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Publishes complete immutable snapshots atomically across coordinator and render threads.
/// </summary>
public sealed class OverlayStateStore
{
    private OverlaySnapshot current = OverlaySnapshot.Empty;

    public OverlaySnapshot Current => Volatile.Read(ref this.current);

    public void Publish(OverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref this.current, snapshot);
    }
}
