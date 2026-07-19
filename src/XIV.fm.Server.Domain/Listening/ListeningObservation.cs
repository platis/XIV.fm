namespace XIV.fm.Server.Domain.Listening;

public enum ListeningObservationStatus
{
    Playing,
    NotPlaying,
}

public sealed record ListeningObservation
{
    public ListeningObservation(
        ListeningObservationStatus status,
        DateTimeOffset observedAt,
        NormalizedTrack? track)
    {
        if (status == ListeningObservationStatus.Playing && track is null)
            throw new ArgumentException("Playing observations require a track.", nameof(track));
        if (status == ListeningObservationStatus.NotPlaying && track is not null)
            throw new ArgumentException("Not-playing observations cannot include a track.", nameof(track));

        this.Status = status;
        this.ObservedAt = observedAt;
        this.Track = track;
    }

    public ListeningObservationStatus Status { get; }

    public DateTimeOffset ObservedAt { get; }

    public NormalizedTrack? Track { get; }
}
