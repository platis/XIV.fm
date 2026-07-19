namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class InstallationCredentialEntity
{
    public Guid InstallationId { get; set; }

    public string CredentialHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RotatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
