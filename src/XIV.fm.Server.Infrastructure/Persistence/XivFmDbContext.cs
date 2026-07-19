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

    public DbSet<LastFmAccountEntity> LastFmAccounts =>
        this.Set<LastFmAccountEntity>();

    public DbSet<AccountLinkSessionEntity> AccountLinkSessions =>
        this.Set<AccountLinkSessionEntity>();

    public DbSet<RelayEntity> Relays => this.Set<RelayEntity>();

    public DbSet<RelayMembershipEntity> RelayMemberships => this.Set<RelayMembershipEntity>();

    public DbSet<RelayInvitationEntity> RelayInvitations => this.Set<RelayInvitationEntity>();

    public DbSet<RelayRemovalEntity> RelayRemovals => this.Set<RelayRemovalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<LastFmAccountEntity>();
        account.ToTable("lastfm_accounts");
        account.HasKey(entity => entity.AccountId);
        account.Property(entity => entity.AccountId).HasColumnName("account_id");
        account.Property(entity => entity.CanonicalName)
            .HasColumnName("canonical_name")
            .HasMaxLength(128)
            .IsRequired();
        account.Property(entity => entity.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(128)
            .IsRequired();
        account.HasIndex(entity => entity.NormalizedName).IsUnique();
        account.Property(entity => entity.CreatedAt).HasColumnName("created_at");

        var linkSession = modelBuilder.Entity<AccountLinkSessionEntity>();
        linkSession.ToTable("account_link_sessions");
        linkSession.HasKey(entity => entity.SessionId);
        linkSession.Property(entity => entity.SessionId).HasColumnName("session_id");
        linkSession.Property(entity => entity.LinkCredentialHash)
            .HasColumnName("link_credential_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        linkSession.HasIndex(entity => entity.LinkCredentialHash).IsUnique();
        linkSession.Property(entity => entity.CallbackStateHash)
            .HasColumnName("callback_state_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        linkSession.HasIndex(entity => entity.CallbackStateHash).IsUnique();
        linkSession.Property(entity => entity.ProviderTokenHash)
            .HasColumnName("provider_token_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        linkSession.HasIndex(entity => entity.ProviderTokenHash).IsUnique();
        linkSession.Property(entity => entity.Status).HasColumnName("status");
        linkSession.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        linkSession.Property(entity => entity.ExpiresAt).HasColumnName("expires_at");
        linkSession.Property(entity => entity.AuthorizationStartedAt).HasColumnName("authorization_started_at");
        linkSession.Property(entity => entity.CompletedAt).HasColumnName("completed_at");
        linkSession.Property(entity => entity.AccountId).HasColumnName("account_id");
        linkSession.HasOne(entity => entity.Account)
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        var installation = modelBuilder.Entity<InstallationCredentialEntity>();
        installation.ToTable("installation_credentials");
        installation.HasKey(entity => entity.InstallationId);
        installation.Property(entity => entity.InstallationId).HasColumnName("installation_id");
        installation.Property(entity => entity.AccountId).HasColumnName("account_id");
        installation.HasOne(entity => entity.Account)
            .WithMany()
            .HasForeignKey(entity => entity.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        installation.Property(entity => entity.CredentialHash)
            .HasColumnName("credential_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        installation.HasIndex(entity => entity.CredentialHash).IsUnique();
        installation.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        installation.Property(entity => entity.RotatedAt).HasColumnName("rotated_at");
        installation.Property(entity => entity.RevokedAt).HasColumnName("revoked_at");

        var relay = modelBuilder.Entity<RelayEntity>();
        relay.ToTable("relays");
        relay.HasKey(entity => entity.RelayId);
        relay.Property(entity => entity.RelayId).HasColumnName("relay_id");
        relay.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(192).IsRequired();
        relay.Property(entity => entity.NormalizedName).HasColumnName("normalized_name").HasMaxLength(192).IsRequired();
        relay.Property(entity => entity.OwnerAccountId).HasColumnName("owner_account_id");
        relay.HasOne(entity => entity.OwnerAccount).WithMany().HasForeignKey(entity => entity.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        relay.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key");
        relay.HasIndex(entity => new { entity.OwnerAccountId, entity.IdempotencyKey }).IsUnique();
        relay.HasIndex(entity => new { entity.OwnerAccountId, entity.CreatedAt });
        relay.Property(entity => entity.MembershipRevision).HasColumnName("membership_revision").IsConcurrencyToken();
        relay.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        relay.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        relay.Property(entity => entity.DeletedAt).HasColumnName("deleted_at");

        var membership = modelBuilder.Entity<RelayMembershipEntity>();
        membership.ToTable("relay_memberships");
        membership.HasKey(entity => entity.MembershipId);
        membership.Property(entity => entity.MembershipId).HasColumnName("membership_id");
        membership.Property(entity => entity.RelayId).HasColumnName("relay_id");
        membership.HasOne(entity => entity.Relay).WithMany(entity => entity.Memberships).HasForeignKey(entity => entity.RelayId).OnDelete(DeleteBehavior.Cascade);
        membership.Property(entity => entity.AccountId).HasColumnName("account_id");
        membership.HasOne(entity => entity.Account).WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict);
        membership.HasIndex(entity => new { entity.RelayId, entity.AccountId }).IsUnique();
        membership.HasIndex(entity => entity.AccountId);
        membership.Property(entity => entity.JoinedAt).HasColumnName("joined_at");

        var invitation = modelBuilder.Entity<RelayInvitationEntity>();
        invitation.ToTable("relay_invitations");
        invitation.HasKey(entity => entity.InvitationId);
        invitation.Property(entity => entity.InvitationId).HasColumnName("invitation_id");
        invitation.Property(entity => entity.RelayId).HasColumnName("relay_id");
        invitation.HasOne(entity => entity.Relay).WithMany(entity => entity.Invitations).HasForeignKey(entity => entity.RelayId).OnDelete(DeleteBehavior.Cascade);
        invitation.Property(entity => entity.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        invitation.HasIndex(entity => entity.TokenHash).IsUnique();
        invitation.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        invitation.Property(entity => entity.ExpiresAt).HasColumnName("expires_at");
        invitation.Property(entity => entity.AcceptedAt).HasColumnName("accepted_at");
        invitation.Property(entity => entity.AcceptedByAccountId).HasColumnName("accepted_by_account_id");
        invitation.HasOne(entity => entity.AcceptedByAccount).WithMany().HasForeignKey(entity => entity.AcceptedByAccountId).OnDelete(DeleteBehavior.Restrict);
        invitation.Property(entity => entity.RevokedAt).HasColumnName("revoked_at");

        var removal = modelBuilder.Entity<RelayRemovalEntity>();
        removal.ToTable("relay_removals");
        removal.HasKey(entity => new { entity.RelayId, entity.AccountId });
        removal.Property(entity => entity.RelayId).HasColumnName("relay_id");
        removal.HasOne(entity => entity.Relay).WithMany().HasForeignKey(entity => entity.RelayId).OnDelete(DeleteBehavior.Cascade);
        removal.Property(entity => entity.AccountId).HasColumnName("account_id");
        removal.HasOne(entity => entity.Account).WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict);
        removal.Property(entity => entity.RemovedAt).HasColumnName("removed_at");
    }
}
