using Microsoft.EntityFrameworkCore;

namespace XIV.fm.Server.Infrastructure.Persistence;

public sealed class XivFmDbContext : DbContext
{
    public XivFmDbContext(DbContextOptions<XivFmDbContext> options)
        : base(options)
    {
    }

    public DbSet<InstallationCredentialEntity> InstallationCredentials =>
        this.Set<InstallationCredentialEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var installation = modelBuilder.Entity<InstallationCredentialEntity>();
        installation.ToTable("installation_credentials");
        installation.HasKey(entity => entity.InstallationId);
        installation.Property(entity => entity.InstallationId).HasColumnName("installation_id");
        installation.Property(entity => entity.CredentialHash)
            .HasColumnName("credential_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        installation.HasIndex(entity => entity.CredentialHash).IsUnique();
        installation.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        installation.Property(entity => entity.RotatedAt).HasColumnName("rotated_at");
        installation.Property(entity => entity.RevokedAt).HasColumnName("revoked_at");
    }
}
