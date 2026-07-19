namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class AccountLinkSessionEntity
{
    public Guid SessionId { get; set; }

    public string LinkCredentialHash { get; set; } = string.Empty;

    public string CallbackStateHash { get; set; } = string.Empty;

    public string? ProviderTokenHash { get; set; }

    public int Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AuthorizationStartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? AccountId { get; set; }

    public LastFmAccountEntity? Account { get; set; }
}
