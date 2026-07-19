using System.Text.Json.Serialization;

namespace XIV.fm.Contracts.V1;

[JsonConverter(typeof(JsonStringEnumConverter<ListeningStatus>))]
public enum ListeningStatus
{
    [JsonStringEnumMemberName("playing")]
    Playing,

    [JsonStringEnumMemberName("notPlaying")]
    NotPlaying,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
}

public sealed record Track(
    string Title,
    string Artist,
    string? Album,
    Uri? AlbumArtUrl,
    Uri? TrackUrl,
    DateTimeOffset? StartedAt);

public sealed record ListeningState(
    ListeningStatus Status,
    bool IsStale,
    DateTimeOffset? ObservedAt,
    Track? Track);
