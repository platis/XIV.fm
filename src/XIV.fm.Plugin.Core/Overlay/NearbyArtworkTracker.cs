namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Tracks the covers worth loading: those of players near enough to show, or who were a moment ago.
/// </summary>
public sealed class NearbyArtworkTracker
{
    /// <summary>
    /// Starts loading slightly before a card appears, so its cover is usually ready.
    /// </summary>
    public const float PreloadYalms = 5f;

    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();
    private readonly Dictionary<Uri, DateTimeOffset> lastNearby = [];

    public void Observe(Uri artworkUrl, DateTimeOffset now)
    {
        lock (this.gate)
            this.lastNearby[artworkUrl] = now;
    }

    /// <returns>Covers to keep, most recently nearby first.</returns>
    public IReadOnlyList<Uri> GetCurrent(DateTimeOffset now)
    {
        lock (this.gate)
        {
            foreach (var expired in this.lastNearby
                .Where(pair => now - pair.Value >= Retention)
                .Select(pair => pair.Key)
                .ToArray())
            {
                this.lastNearby.Remove(expired);
            }

            return this.lastNearby.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).ToArray();
        }
    }
}
