using System.Text.Json.Serialization;

namespace XIV.fm.Contracts.V1;

[JsonConverter(typeof(JsonStringEnumConverter<VisibilityMode>))]
public enum VisibilityMode
{
    [JsonStringEnumMemberName("private")]
    Private,

    [JsonStringEnumMemberName("public")]
    Public,

    [JsonStringEnumMemberName("custom")]
    Custom,
}

public sealed record VisibilitySelection(
    VisibilityMode Mode,
    IReadOnlyList<Guid> RelayIds);
