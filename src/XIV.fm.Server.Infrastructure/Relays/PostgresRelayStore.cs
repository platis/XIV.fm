using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.Persistence;

namespace XIV.fm.Server.Infrastructure.Relays;

public sealed class PostgresRelayStore : IRelayStore
{
    private readonly IDbContextFactory<XivFmDbContext> contextFactory;

    public PostgresRelayStore(IDbContextFactory<XivFmDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async ValueTask<RelayStoreResult<StoredRelay>> CreateAsync(
        AccountId accountId,
        string name,
        string normalizedName,
        Guid idempotencyKey,
        DateTimeOffset now,
        RelayOptions limits,
        CancellationToken cancellationToken)
    {
        await using var context = await this.CreateTransactionalContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await context.Relays.Include(relay => relay.Memberships).SingleOrDefaultAsync(
            relay => relay.OwnerAccountId == accountId.Value && relay.IdempotencyKey == idempotencyKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var result = existing.DeletedAt is null && existing.NormalizedName == normalizedName
                ? RelayStoreResult<StoredRelay>.Success(ToStored(existing))
                : RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.Conflict);
            await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        var activeOwned = await context.Relays.CountAsync(
            relay => relay.OwnerAccountId == accountId.Value && relay.DeletedAt == null,
            cancellationToken).ConfigureAwait(false);
        if (activeOwned >= limits.MaximumActiveOwnedRelays)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.ActiveOwnershipLimit);
        var rollingStart = now.Subtract(limits.CreationRollingWindow);
        var rollingCount = await context.Relays.CountAsync(
            relay => relay.OwnerAccountId == accountId.Value && relay.CreatedAt > rollingStart,
            cancellationToken).ConfigureAwait(false);
        if (rollingCount >= limits.MaximumCreationsPerRollingWindow)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.RollingCreationLimit);
        var burstStart = now.Subtract(limits.CreationBurstWindow);
        if (await context.Relays.AnyAsync(
            relay => relay.OwnerAccountId == accountId.Value && relay.CreatedAt > burstStart,
            cancellationToken).ConfigureAwait(false))
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.CreationBurstLimit);
        if (await CountMembershipsAsync(context, accountId, cancellationToken).ConfigureAwait(false) >= limits.MaximumJoinedRelays)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.JoinedLimit);

        var relay = new RelayEntity
        {
            RelayId = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizedName,
            OwnerAccountId = accountId.Value,
            IdempotencyKey = idempotencyKey,
            MembershipRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        relay.Memberships.Add(new RelayMembershipEntity
        {
            MembershipId = Guid.NewGuid(),
            AccountId = accountId.Value,
            JoinedAt = now,
        });
        context.Relays.Add(relay);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            return RelayStoreResult<StoredRelay>.Success(ToStored(relay));
        }
        catch (DbUpdateException)
        {
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.Conflict);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.Conflict);
        }
    }

    public async ValueTask<IReadOnlyList<StoredRelay>> ListAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var relays = await context.Relays.AsNoTracking()
            .Where(relay => relay.DeletedAt == null && relay.Memberships.Any(member => member.AccountId == accountId.Value))
            .OrderBy(relay => relay.Name).ThenBy(relay => relay.RelayId)
            .Select(relay => new StoredRelay(
                relay.RelayId,
                relay.Name,
                relay.NormalizedName,
                new AccountId(relay.OwnerAccountId),
                relay.MembershipRevision,
                relay.Memberships.Count,
                relay.CreatedAt,
                relay.UpdatedAt))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return relays;
    }

    public async ValueTask<RelayStoreResult<StoredRelay>> GetAsync(AccountId accountId, Guid relayId, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var relay = await context.Relays.AsNoTracking().Include(candidate => candidate.Memberships).SingleOrDefaultAsync(
            candidate => candidate.RelayId == relayId && candidate.DeletedAt == null && candidate.Memberships.Any(member => member.AccountId == accountId.Value),
            cancellationToken).ConfigureAwait(false);
        return relay is null ? NotFound<StoredRelay>() : RelayStoreResult<StoredRelay>.Success(ToStored(relay));
    }

    public async ValueTask<RelayStoreResult<StoredRelay>> RenameAsync(AccountId accountId, Guid relayId, string name, string normalizedName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, true, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<StoredRelay>.Failed(authorization.Failure);
        authorization.Relay.Name = name;
        authorization.Relay.NormalizedName = normalizedName;
        authorization.Relay.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<StoredRelay>.Success(ToStored(authorization.Relay));
    }

    public async ValueTask<RelayStoreResult<bool>> DeleteAsync(AccountId accountId, Guid relayId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.CreateTransactionalContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, true, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<bool>.Failed(authorization.Failure);
        var relay = authorization.Relay;
        relay.DeletedAt = now;
        relay.UpdatedAt = now;
        relay.MembershipRevision++;
        await context.RelayInvitations.Where(invitation => invitation.RelayId == relayId && invitation.AcceptedAt == null && invitation.RevokedAt == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(invitation => invitation.RevokedAt, now), cancellationToken).ConfigureAwait(false);
        await context.RelayMemberships.Where(member => member.RelayId == relayId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<bool>.Success(true);
    }

    public async ValueTask<RelayStoreResult<IReadOnlyList<StoredRelayMember>>> ListMembersAsync(AccountId accountId, Guid relayId, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, false, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<IReadOnlyList<StoredRelayMember>>.Failed(authorization.Failure);
        IReadOnlyList<StoredRelayMember> members = await context.RelayMemberships.AsNoTracking()
            .Where(member => member.RelayId == relayId)
            .OrderBy(member => member.JoinedAt)
            .Select(member => new StoredRelayMember(member.MembershipId, new AccountId(member.AccountId), member.Account.CanonicalName, member.JoinedAt))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<IReadOnlyList<StoredRelayMember>>.Success(members);
    }

    public async ValueTask<RelayStoreResult<AccountId>> KickAsync(AccountId accountId, Guid relayId, Guid membershipId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.CreateTransactionalContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, false, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<AccountId>.Failed(authorization.Failure);
        var member = await context.RelayMemberships.SingleOrDefaultAsync(candidate => candidate.RelayId == relayId && candidate.MembershipId == membershipId, cancellationToken).ConfigureAwait(false);
        if (member is null)
            return NotFound<AccountId>();
        if (member.AccountId == authorization.Relay.OwnerAccountId)
            return RelayStoreResult<AccountId>.Failed(RelayStoreFailure.Conflict);
        context.RelayMemberships.Remove(member);
        var removal = await context.RelayRemovals.SingleOrDefaultAsync(candidate => candidate.RelayId == relayId && candidate.AccountId == member.AccountId, cancellationToken).ConfigureAwait(false);
        if (removal is null)
            context.RelayRemovals.Add(new RelayRemovalEntity { RelayId = relayId, AccountId = member.AccountId, RemovedAt = now });
        else
            removal.RemovedAt = now;
        authorization.Relay.MembershipRevision++;
        authorization.Relay.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<AccountId>.Success(new AccountId(member.AccountId));
    }

    public async ValueTask<RelayStoreResult<bool>> LeaveAsync(AccountId accountId, Guid relayId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.CreateTransactionalContextAsync(cancellationToken).ConfigureAwait(false);
        var relay = await context.Relays.SingleOrDefaultAsync(candidate => candidate.RelayId == relayId && candidate.DeletedAt == null, cancellationToken).ConfigureAwait(false);
        if (relay is null)
            return NotFound<bool>();
        if (relay.OwnerAccountId == accountId.Value)
            return RelayStoreResult<bool>.Failed(RelayStoreFailure.Conflict);
        var member = await context.RelayMemberships.SingleOrDefaultAsync(candidate => candidate.RelayId == relayId && candidate.AccountId == accountId.Value, cancellationToken).ConfigureAwait(false);
        if (member is null)
            return NotFound<bool>();
        context.RelayMemberships.Remove(member);
        relay.MembershipRevision++;
        relay.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<bool>.Success(true);
    }

    public async ValueTask<RelayStoreResult<StoredRelayInvitation>> CreateInvitationAsync(AccountId accountId, Guid relayId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt, RelayOptions limits, CancellationToken cancellationToken)
    {
        await using var context = await this.CreateTransactionalContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, false, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<StoredRelayInvitation>.Failed(authorization.Failure);
        var activeCount = await context.RelayInvitations.CountAsync(invitation => invitation.RelayId == relayId && invitation.AcceptedAt == null && invitation.RevokedAt == null && invitation.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
        if (activeCount >= limits.MaximumActiveInvitationsPerRelay)
            return RelayStoreResult<StoredRelayInvitation>.Failed(RelayStoreFailure.InvitationLimit);
        var invitation = new RelayInvitationEntity { InvitationId = Guid.NewGuid(), RelayId = relayId, TokenHash = tokenHash, CreatedAt = now, ExpiresAt = expiresAt };
        context.RelayInvitations.Add(invitation);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            return RelayStoreResult<StoredRelayInvitation>.Success(ToStored(authorization.Relay, invitation));
        }
        catch (DbUpdateException)
        {
            return RelayStoreResult<StoredRelayInvitation>.Failed(RelayStoreFailure.Conflict);
        }
    }

    public async ValueTask<RelayStoreResult<IReadOnlyList<StoredRelayInvitation>>> ListInvitationsAsync(AccountId accountId, Guid relayId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, false, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<IReadOnlyList<StoredRelayInvitation>>.Failed(authorization.Failure);
        IReadOnlyList<StoredRelayInvitation> invitations = await context.RelayInvitations.AsNoTracking()
            .Where(invitation => invitation.RelayId == relayId && invitation.RevokedAt == null && (invitation.AcceptedAt != null || invitation.ExpiresAt > now))
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Take(100)
            .Select(invitation => new StoredRelayInvitation(invitation.InvitationId, relayId, authorization.Relay.Name, invitation.CreatedAt, invitation.ExpiresAt, invitation.AcceptedAt))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<IReadOnlyList<StoredRelayInvitation>>.Success(invitations);
    }

    public async ValueTask<RelayStoreResult<bool>> RevokeInvitationAsync(AccountId accountId, Guid relayId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var authorization = await GetOwnerRelayAsync(context, accountId, relayId, false, cancellationToken).ConfigureAwait(false);
        if (authorization.Relay is null)
            return RelayStoreResult<bool>.Failed(authorization.Failure);
        var invitation = await context.RelayInvitations.SingleOrDefaultAsync(candidate => candidate.RelayId == relayId && candidate.InvitationId == invitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null)
            return NotFound<bool>();
        if (invitation.AcceptedAt is not null)
            return RelayStoreResult<bool>.Failed(RelayStoreFailure.Conflict);
        invitation.RevokedAt ??= now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RelayStoreResult<bool>.Success(true);
    }

    public async ValueTask<RelayStoreResult<StoredRelayInvitation>> PreviewInvitationAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var invitation = await context.RelayInvitations.AsNoTracking().Include(candidate => candidate.Relay).SingleOrDefaultAsync(
            candidate => candidate.TokenHash == tokenHash && candidate.AcceptedAt == null && candidate.RevokedAt == null && candidate.ExpiresAt > now && candidate.Relay.DeletedAt == null,
            cancellationToken).ConfigureAwait(false);
        return invitation is null
            ? RelayStoreResult<StoredRelayInvitation>.Failed(RelayStoreFailure.InvitationInvalid)
            : RelayStoreResult<StoredRelayInvitation>.Success(ToStored(invitation.Relay, invitation));
    }

    public async ValueTask<RelayStoreResult<StoredRelay>> AcceptInvitationAsync(AccountId accountId, string tokenHash, DateTimeOffset now, RelayOptions limits, CancellationToken cancellationToken)
    {
        await using var context = await this.CreateTransactionalContextAsync(cancellationToken).ConfigureAwait(false);
        var invitation = await context.RelayInvitations.Include(candidate => candidate.Relay).ThenInclude(relay => relay.Memberships).SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken).ConfigureAwait(false);
        if (invitation is null || invitation.Relay.DeletedAt is not null)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.InvitationInvalid);
        if (invitation.AcceptedAt is not null)
            return invitation.AcceptedByAccountId == accountId.Value && invitation.Relay.Memberships.Any(member => member.AccountId == accountId.Value)
                ? RelayStoreResult<StoredRelay>.Success(ToStored(invitation.Relay))
                : RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.InvitationInvalid);
        if (invitation.RevokedAt is not null || invitation.ExpiresAt <= now)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.InvitationInvalid);
        var removal = await context.RelayRemovals.SingleOrDefaultAsync(candidate => candidate.RelayId == invitation.RelayId && candidate.AccountId == accountId.Value, cancellationToken).ConfigureAwait(false);
        if (removal is not null && invitation.CreatedAt <= removal.RemovedAt)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.RemovalRestricted);
        var isMember = invitation.Relay.Memberships.Any(member => member.AccountId == accountId.Value);
        if (!isMember && await CountMembershipsAsync(context, accountId, cancellationToken).ConfigureAwait(false) >= limits.MaximumJoinedRelays)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.JoinedLimit);
        if (!isMember && invitation.Relay.Memberships.Count >= limits.MaximumMembersPerRelay)
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.MemberLimit);
        if (!isMember)
        {
            context.RelayMemberships.Add(new RelayMembershipEntity
            {
                MembershipId = Guid.NewGuid(),
                RelayId = invitation.RelayId,
                Relay = invitation.Relay,
                AccountId = accountId.Value,
                JoinedAt = now,
            });
            invitation.Relay.MembershipRevision++;
            invitation.Relay.UpdatedAt = now;
        }
        if (removal is not null)
            context.RelayRemovals.Remove(removal);
        invitation.AcceptedAt = now;
        invitation.AcceptedByAccountId = accountId.Value;
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
            return RelayStoreResult<StoredRelay>.Success(ToStored(invitation.Relay));
        }
        catch (DbUpdateException)
        {
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.Conflict);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.Conflict);
        }
    }

    public async ValueTask<IReadOnlyDictionary<Guid, long>?> GetMembershipRevisionsAsync(AccountId accountId, IReadOnlyList<Guid> relayIds, CancellationToken cancellationToken)
    {
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var distinctIds = relayIds.Distinct().ToArray();
        var values = await context.Relays.AsNoTracking()
            .Where(relay => distinctIds.Contains(relay.RelayId) && relay.DeletedAt == null && relay.Memberships.Any(member => member.AccountId == accountId.Value))
            .Select(relay => new { relay.RelayId, relay.MembershipRevision })
            .ToDictionaryAsync(relay => relay.RelayId, relay => relay.MembershipRevision, cancellationToken).ConfigureAwait(false);
        return values.Count == distinctIds.Length ? values : null;
    }

    public async ValueTask<IReadOnlySet<AccountId>> GetCurrentMembersAsync(Guid relayId, IReadOnlyCollection<AccountId> candidateAccountIds, CancellationToken cancellationToken)
    {
        if (candidateAccountIds.Count == 0)
            return new HashSet<AccountId>();
        var candidates = candidateAccountIds.Select(account => account.Value).ToArray();
        await using var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var members = await context.RelayMemberships.AsNoTracking()
            .Where(member => member.RelayId == relayId && member.Relay.DeletedAt == null && candidates.Contains(member.AccountId))
            .Select(member => member.AccountId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return members.Select(value => new AccountId(value)).ToHashSet();
    }

    private async ValueTask<XivFmDbContext> CreateTransactionalContextAsync(CancellationToken cancellationToken)
    {
        var context = await this.contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        return context;
    }

    private static async ValueTask<int> CountMembershipsAsync(XivFmDbContext context, AccountId accountId, CancellationToken cancellationToken) =>
        await context.RelayMemberships.CountAsync(member => member.AccountId == accountId.Value && member.Relay.DeletedAt == null, cancellationToken).ConfigureAwait(false);

    private static async ValueTask<(RelayEntity? Relay, RelayStoreFailure Failure)> GetOwnerRelayAsync(XivFmDbContext context, AccountId accountId, Guid relayId, bool includeMembers, CancellationToken cancellationToken)
    {
        IQueryable<RelayEntity> query = context.Relays;
        if (includeMembers)
            query = query.Include(relay => relay.Memberships);
        var relay = await query.SingleOrDefaultAsync(candidate => candidate.RelayId == relayId && candidate.DeletedAt == null, cancellationToken).ConfigureAwait(false);
        return relay is null
            ? (null, RelayStoreFailure.NotFound)
            : relay.OwnerAccountId != accountId.Value
                ? (null, RelayStoreFailure.Forbidden)
                : (relay, RelayStoreFailure.Conflict);
    }

    private static StoredRelay ToStored(RelayEntity relay) => new(relay.RelayId, relay.Name, relay.NormalizedName, new AccountId(relay.OwnerAccountId), relay.MembershipRevision, relay.Memberships.Count, relay.CreatedAt, relay.UpdatedAt);

    private static StoredRelayInvitation ToStored(RelayEntity relay, RelayInvitationEntity invitation) => new(invitation.InvitationId, relay.RelayId, relay.Name, invitation.CreatedAt, invitation.ExpiresAt, invitation.AcceptedAt);

    private static RelayStoreResult<T> NotFound<T>() => RelayStoreResult<T>.Failed(RelayStoreFailure.NotFound);
}
