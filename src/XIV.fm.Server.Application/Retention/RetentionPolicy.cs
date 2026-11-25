namespace XIV.fm.Server.Application.Retention;

/// <summary>
/// How long records that no longer have any effect are kept. Linked accounts, active installations, active
/// Relays, memberships, and removal restrictions are never removed here.
/// </summary>
public sealed record RetentionPolicy(
    TimeSpan ExpiredLinkSessions,
    TimeSpan RevokedInstallations,
    TimeSpan EndedInvitations,
    TimeSpan DeletedRelays)
{
    public static RetentionPolicy Default { get; } = new(
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(60));

    /// <summary>
    /// Deleted Relays still count toward the rolling creation limit, so they must outlive its window.
    /// </summary>
    public RetentionPolicy Covering(TimeSpan relayCreationWindow) =>
        this.DeletedRelays > relayCreationWindow
            ? this
            : this with { DeletedRelays = relayCreationWindow.Add(TimeSpan.FromDays(1)) };
}

public sealed record RetentionResult(
    int LinkSessions,
    int Installations,
    int Invitations,
    int Relays)
{
    public int Total => this.LinkSessions + this.Installations + this.Invitations + this.Relays;
}
