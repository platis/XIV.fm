using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Core.Sync;

public enum ConnectionTone
{
    Neutral,
    Success,
    Warning,
    Danger,
}

/// <summary>
/// Describes the real connection to XIV.fm and Last.fm. A stored credential alone is not a connection.
/// </summary>
public sealed record ConnectionSummary(
    string Title,
    string Detail,
    ConnectionTone Tone,
    bool RequiresRelink = false)
{
    public const string CredentialRejectedCode = "installation_credential_required";
    public const string RelayMembershipRequiredCode = "relay_membership_required";

    private static readonly TimeSpan RecentSync = TimeSpan.FromSeconds(60);

    public static ConnectionSummary Create(
        bool isLinked,
        string? accountName,
        bool isInDuty,
        SyncRuntimeState sync,
        ListeningState listening,
        DateTimeOffset now)
    {
        if (!isLinked)
        {
            return new ConnectionSummary(
                "Not connected",
                "Connect Last.fm to show what you’re listening to above your character.",
                ConnectionTone.Neutral);
        }

        if (sync.ErrorCode == CredentialRejectedCode)
        {
            return new ConnectionSummary(
                "Link no longer valid",
                "XIV.fm no longer accepts this link. Connect Last.fm again.",
                ConnectionTone.Danger,
                RequiresRelink: true);
        }

        if (isInDuty)
        {
            return new ConnectionSummary(
                "Paused in duty",
                "Syncing resumes after you leave the duty.",
                ConnectionTone.Warning);
        }

        if (sync.ErrorCode == RelayMembershipRequiredCode)
        {
            return new ConnectionSummary(
                "Updating Relays",
                "You are no longer in a selected Relay. XIV.fm is updating your audience.",
                ConnectionTone.Warning);
        }

        if (sync.Error is not null)
        {
            return new ConnectionSummary(
                "Reconnecting",
                $"XIV.fm can’t sync right now. {DescribeRetry(sync, now)}",
                ConnectionTone.Warning);
        }

        if (sync.LastSuccessAt is not DateTimeOffset lastSuccess || now - lastSuccess >= RecentSync)
        {
            return new ConnectionSummary(
                "Connecting",
                "Waiting for the first sync. You must be logged in to a character.",
                ConnectionTone.Neutral);
        }

        var account = string.IsNullOrWhiteSpace(accountName) ? "Last.fm" : accountName;
        return new ConnectionSummary(
            "Connected",
            $"{account} · {DescribeListening(listening)}",
            listening.Status == ListeningStatus.Unavailable || listening.IsStale
                ? ConnectionTone.Warning
                : ConnectionTone.Success);
    }

    private static string DescribeRetry(SyncRuntimeState sync, DateTimeOffset now)
    {
        if (sync.NextSyncAt is not DateTimeOffset next || next <= now)
            return "Retrying now.";

        var seconds = (int)Math.Ceiling((next - now).TotalSeconds);
        return $"Next attempt in {seconds} {(seconds == 1 ? "second" : "seconds")}.";
    }

    private static string DescribeListening(ListeningState listening) => listening switch
    {
        { Status: ListeningStatus.Playing, Track: not null, IsStale: true } =>
            $"{listening.Track.Title} by {listening.Track.Artist}. Last.fm isn’t responding, so this may be out of date.",
        { Status: ListeningStatus.Playing, Track: not null } =>
            $"{listening.Track.Title} by {listening.Track.Artist}",
        { Status: ListeningStatus.NotPlaying } => "Nothing playing",
        _ => "Waiting for Last.fm. If this continues, check that your recent listening isn’t hidden in your Last.fm privacy settings.",
    };
}
