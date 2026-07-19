using System.Text.Json.Serialization;

namespace XIV.fm.Contracts.V1;

[JsonConverter(typeof(JsonStringEnumConverter<AccountLinkStatus>))]
public enum AccountLinkStatus
{
    [JsonStringEnumMemberName("pending")]
    Pending,

    [JsonStringEnumMemberName("linked")]
    Linked,

    [JsonStringEnumMemberName("failed")]
    Failed,

    [JsonStringEnumMemberName("expired")]
    Expired,
}

public sealed record StartAccountLinkResponse(
    Guid LinkSessionId,
    Uri AuthorizationUrl,
    string LinkCredential,
    DateTimeOffset ExpiresAt,
    int PollAfterSeconds);

public sealed record AccountLinkStatusRequest(string LinkCredential);

public sealed record AccountLinkStatusResponse(
    AccountLinkStatus Status,
    DateTimeOffset ExpiresAt,
    string? LastFmAccountName);
