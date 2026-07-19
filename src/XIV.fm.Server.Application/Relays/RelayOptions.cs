namespace XIV.fm.Server.Application.Relays;

public sealed record RelayOptions(
    int MaximumActiveOwnedRelays,
    int MaximumCreationsPerRollingWindow,
    TimeSpan CreationRollingWindow,
    TimeSpan CreationBurstWindow,
    int MaximumJoinedRelays,
    int MaximumMembersPerRelay,
    int MaximumActiveInvitationsPerRelay,
    TimeSpan InvitationLifetime,
    TimeSpan MaximumInvitationLifetime,
    int MaximumSelectedRelays)
{
    public static RelayOptions Default { get; } = new(
        3,
        10,
        TimeSpan.FromDays(30),
        TimeSpan.FromMinutes(1),
        20,
        100,
        20,
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(7),
        5);
}
