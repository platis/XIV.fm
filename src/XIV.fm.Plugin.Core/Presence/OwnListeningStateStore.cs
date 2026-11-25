using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Core.Presence;

/// <summary>
/// Holds the local player's synchronized listening state for a bounded time. Without successful syncs the
/// plugin cannot know whether playback continues, so the state becomes unavailable instead of lingering.
/// </summary>
public sealed class OwnListeningStateStore
{
    /// <summary>
    /// Outlasts a short server restart, including the first two sync retries.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);

    private readonly Lock gate = new();
    private ListeningState current = Unavailable;
    private DateTimeOffset synchronizedAt = DateTimeOffset.MinValue;

    private static ListeningState Unavailable { get; } =
        new(ListeningStatus.Unavailable, false, null, null);

    public ListeningState Read(DateTimeOffset now)
    {
        lock (this.gate)
            return now - this.synchronizedAt < Retention ? this.current : Unavailable;
    }

    /// <returns>Whether the visible listening state changed.</returns>
    public bool Apply(ListeningState listening, DateTimeOffset now)
    {
        lock (this.gate)
        {
            var changed = this.Read(now) != listening;
            this.current = listening;
            this.synchronizedAt = now;
            return changed;
        }
    }

    /// <returns>Whether the visible listening state changed.</returns>
    public bool Clear(DateTimeOffset now)
    {
        lock (this.gate)
        {
            var changed = this.Read(now) != Unavailable;
            this.current = Unavailable;
            this.synchronizedAt = DateTimeOffset.MinValue;
            return changed;
        }
    }
}
