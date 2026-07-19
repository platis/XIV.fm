namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class LastFmAccountEntity
{
    public Guid AccountId { get; set; }

    public string CanonicalName { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
