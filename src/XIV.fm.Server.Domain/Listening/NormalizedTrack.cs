namespace XIV.fm.Server.Domain.Listening;

public sealed record NormalizedTrack(
    string Title,
    string Artist,
    string? Album,
    Uri? AlbumArtUrl,
    Uri? TrackUrl,
    DateTimeOffset? StartedAt);
