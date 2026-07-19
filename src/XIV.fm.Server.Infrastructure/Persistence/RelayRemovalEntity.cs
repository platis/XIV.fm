namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class RelayRemovalEntity
{
    public Guid RelayId { get; set; }
    public RelayEntity Relay { get; set; } = null!;
    public Guid AccountId { get; set; }
    public LastFmAccountEntity Account { get; set; } = null!;
    public DateTimeOffset RemovedAt { get; set; }
}
