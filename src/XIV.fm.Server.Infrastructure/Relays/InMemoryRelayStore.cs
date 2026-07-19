using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Infrastructure.AccountLinks;

namespace XIV.fm.Server.Infrastructure.Relays;

public sealed class InMemoryRelayStore : IRelayStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, RelayState> relays = [];
    private readonly Dictionary<string, InvitationState> invitationsByHash = new(StringComparer.Ordinal);
    private readonly InMemoryAccountLinkStore accounts;

    public InMemoryRelayStore(InMemoryAccountLinkStore accounts)
    {
        this.accounts = accounts;
    }

    public ValueTask<RelayStoreResult<StoredRelay>> CreateAsync(
        AccountId accountId,
        string name,
        string normalizedName,
        Guid idempotencyKey,
        DateTimeOffset now,
        RelayOptions limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var existing = this.relays.Values.FirstOrDefault(relay =>
                relay.OwnerAccountId == accountId && relay.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                return ValueTask.FromResult(existing.DeletedAt is null && existing.NormalizedName == normalizedName
                    ? RelayStoreResult<StoredRelay>.Success(ToStored(existing))
                    : RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.Conflict));
            }

            var owned = this.relays.Values.Where(relay => relay.OwnerAccountId == accountId).ToArray();
            if (owned.Count(relay => relay.DeletedAt is null) >= limits.MaximumActiveOwnedRelays)
                return Failed<StoredRelay>(RelayStoreFailure.ActiveOwnershipLimit);
            if (owned.Count(relay => relay.CreatedAt > now.Subtract(limits.CreationRollingWindow)) >= limits.MaximumCreationsPerRollingWindow)
                return Failed<StoredRelay>(RelayStoreFailure.RollingCreationLimit);
            if (owned.Any(relay => relay.CreatedAt > now.Subtract(limits.CreationBurstWindow)))
                return Failed<StoredRelay>(RelayStoreFailure.CreationBurstLimit);
            if (CountMemberships(accountId) >= limits.MaximumJoinedRelays)
                return Failed<StoredRelay>(RelayStoreFailure.JoinedLimit);

            var relay = new RelayState(
                Guid.NewGuid(),
                name,
                normalizedName,
                accountId,
                idempotencyKey,
                1,
                now,
                now);
            relay.Members.Add(new MembershipState(Guid.NewGuid(), accountId, now));
            this.relays.Add(relay.RelayId, relay);
            return ValueTask.FromResult(RelayStoreResult<StoredRelay>.Success(ToStored(relay)));
        }
    }

    public ValueTask<IReadOnlyList<StoredRelay>> ListAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            IReadOnlyList<StoredRelay> result = this.relays.Values
                .Where(relay => relay.DeletedAt is null && relay.Members.Any(member => member.AccountId == accountId))
                .OrderBy(relay => relay.Name, StringComparer.Ordinal)
                .ThenBy(relay => relay.RelayId)
                .Select(ToStored)
                .ToArray();
            return ValueTask.FromResult(result);
        }
    }

    public ValueTask<RelayStoreResult<StoredRelay>> GetAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            return ValueTask.FromResult(TryGetMemberRelay(accountId, relayId, out var relay)
                ? RelayStoreResult<StoredRelay>.Success(ToStored(relay!))
                : RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.NotFound));
        }
    }

    public ValueTask<RelayStoreResult<StoredRelay>> RenameAsync(
        AccountId accountId,
        Guid relayId,
        string name,
        string normalizedName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<StoredRelay>(authorization.Value);
            relay!.Name = name;
            relay.NormalizedName = normalizedName;
            relay.UpdatedAt = now;
            return ValueTask.FromResult(RelayStoreResult<StoredRelay>.Success(ToStored(relay)));
        }
    }

    public ValueTask<RelayStoreResult<bool>> DeleteAsync(
        AccountId accountId,
        Guid relayId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<bool>(authorization.Value);
            relay!.DeletedAt = now;
            relay.UpdatedAt = now;
            relay.MembershipRevision++;
            relay.Members.Clear();
            foreach (var invitation in relay.Invitations.Where(invitation => invitation.RevokedAt is null && invitation.AcceptedAt is null))
                invitation.RevokedAt = now;
            return ValueTask.FromResult(RelayStoreResult<bool>.Success(true));
        }
    }

    public ValueTask<RelayStoreResult<IReadOnlyList<StoredRelayMember>>> ListMembersAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<IReadOnlyList<StoredRelayMember>>(authorization.Value);
            IReadOnlyList<StoredRelayMember> members = relay!.Members
                .OrderBy(member => member.JoinedAt)
                .Select(member => new StoredRelayMember(
                    member.MembershipId,
                    member.AccountId,
                    this.accounts.GetAccountName(member.AccountId) ?? "Linked account",
                    member.JoinedAt))
                .ToArray();
            return ValueTask.FromResult(RelayStoreResult<IReadOnlyList<StoredRelayMember>>.Success(members));
        }
    }

    public ValueTask<RelayStoreResult<AccountId>> KickAsync(
        AccountId accountId,
        Guid relayId,
        Guid membershipId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<AccountId>(authorization.Value);
            var member = relay!.Members.SingleOrDefault(candidate => candidate.MembershipId == membershipId);
            if (member is null)
                return Failed<AccountId>(RelayStoreFailure.NotFound);
            if (member.AccountId == relay.OwnerAccountId)
                return Failed<AccountId>(RelayStoreFailure.Conflict);
            relay.Members.Remove(member);
            relay.Removals[member.AccountId] = now;
            relay.MembershipRevision++;
            relay.UpdatedAt = now;
            return ValueTask.FromResult(RelayStoreResult<AccountId>.Success(member.AccountId));
        }
    }

    public ValueTask<RelayStoreResult<bool>> LeaveAsync(
        AccountId accountId,
        Guid relayId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (!TryGetMemberRelay(accountId, relayId, out var relay))
                return Failed<bool>(RelayStoreFailure.NotFound);
            if (relay!.OwnerAccountId == accountId)
                return Failed<bool>(RelayStoreFailure.Conflict);
            relay.Members.RemoveAll(member => member.AccountId == accountId);
            relay.MembershipRevision++;
            relay.UpdatedAt = now;
            return ValueTask.FromResult(RelayStoreResult<bool>.Success(true));
        }
    }

    public ValueTask<RelayStoreResult<StoredRelayInvitation>> CreateInvitationAsync(
        AccountId accountId,
        Guid relayId,
        string tokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        RelayOptions limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<StoredRelayInvitation>(authorization.Value);
            if (relay!.Invitations.Count(invitation => invitation.AcceptedAt is null && invitation.RevokedAt is null && invitation.ExpiresAt > now) >= limits.MaximumActiveInvitationsPerRelay)
                return Failed<StoredRelayInvitation>(RelayStoreFailure.InvitationLimit);
            if (this.invitationsByHash.ContainsKey(tokenHash))
                return Failed<StoredRelayInvitation>(RelayStoreFailure.Conflict);
            var invitation = new InvitationState(Guid.NewGuid(), relay.RelayId, tokenHash, now, expiresAt);
            relay.Invitations.Add(invitation);
            this.invitationsByHash.Add(tokenHash, invitation);
            return ValueTask.FromResult(RelayStoreResult<StoredRelayInvitation>.Success(ToStored(relay, invitation)));
        }
    }

    public ValueTask<RelayStoreResult<IReadOnlyList<StoredRelayInvitation>>> ListInvitationsAsync(
        AccountId accountId,
        Guid relayId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<IReadOnlyList<StoredRelayInvitation>>(authorization.Value);
            IReadOnlyList<StoredRelayInvitation> invitations = relay!.Invitations
                .Where(invitation => invitation.RevokedAt is null && (invitation.AcceptedAt is not null || invitation.ExpiresAt > now))
                .OrderByDescending(invitation => invitation.CreatedAt)
                .Take(100)
                .Select(invitation => ToStored(relay, invitation))
                .ToArray();
            return ValueTask.FromResult(RelayStoreResult<IReadOnlyList<StoredRelayInvitation>>.Success(invitations));
        }
    }

    public ValueTask<RelayStoreResult<bool>> RevokeInvitationAsync(
        AccountId accountId,
        Guid relayId,
        Guid invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var authorization = GetOwnerRelay(accountId, relayId, out var relay);
            if (authorization is not null)
                return Failed<bool>(authorization.Value);
            var invitation = relay!.Invitations.SingleOrDefault(candidate => candidate.InvitationId == invitationId);
            if (invitation is null)
                return Failed<bool>(RelayStoreFailure.NotFound);
            if (invitation.AcceptedAt is not null)
                return Failed<bool>(RelayStoreFailure.Conflict);
            invitation.RevokedAt ??= now;
            return ValueTask.FromResult(RelayStoreResult<bool>.Success(true));
        }
    }

    public ValueTask<RelayStoreResult<StoredRelayInvitation>> PreviewInvitationAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (!TryGetUsableInvitation(tokenHash, now, out var relay, out var invitation))
                return Failed<StoredRelayInvitation>(RelayStoreFailure.InvitationInvalid);
            return ValueTask.FromResult(RelayStoreResult<StoredRelayInvitation>.Success(ToStored(relay!, invitation!)));
        }
    }

    public ValueTask<RelayStoreResult<StoredRelay>> AcceptInvitationAsync(
        AccountId accountId,
        string tokenHash,
        DateTimeOffset now,
        RelayOptions limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            if (!this.invitationsByHash.TryGetValue(tokenHash, out var invitation) ||
                !this.relays.TryGetValue(invitation.RelayId, out var relay) || relay.DeletedAt is not null)
                return Failed<StoredRelay>(RelayStoreFailure.InvitationInvalid);
            if (invitation.AcceptedAt is not null)
            {
                return ValueTask.FromResult(invitation.AcceptedByAccountId == accountId && relay.Members.Any(member => member.AccountId == accountId)
                    ? RelayStoreResult<StoredRelay>.Success(ToStored(relay))
                    : RelayStoreResult<StoredRelay>.Failed(RelayStoreFailure.InvitationInvalid));
            }
            if (invitation.RevokedAt is not null || invitation.ExpiresAt <= now)
                return Failed<StoredRelay>(RelayStoreFailure.InvitationInvalid);
            if (relay.Removals.TryGetValue(accountId, out var removedAt) && invitation.CreatedAt <= removedAt)
                return Failed<StoredRelay>(RelayStoreFailure.RemovalRestricted);

            var isMember = relay.Members.Any(member => member.AccountId == accountId);
            if (!isMember && CountMemberships(accountId) >= limits.MaximumJoinedRelays)
                return Failed<StoredRelay>(RelayStoreFailure.JoinedLimit);
            if (!isMember && relay.Members.Count >= limits.MaximumMembersPerRelay)
                return Failed<StoredRelay>(RelayStoreFailure.MemberLimit);
            if (!isMember)
            {
                relay.Members.Add(new MembershipState(Guid.NewGuid(), accountId, now));
                relay.MembershipRevision++;
                relay.UpdatedAt = now;
            }
            relay.Removals.Remove(accountId);
            invitation.AcceptedAt = now;
            invitation.AcceptedByAccountId = accountId;
            return ValueTask.FromResult(RelayStoreResult<StoredRelay>.Success(ToStored(relay)));
        }
    }

    public ValueTask<IReadOnlyDictionary<Guid, long>?> GetMembershipRevisionsAsync(
        AccountId accountId,
        IReadOnlyList<Guid> relayIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            var result = new Dictionary<Guid, long>();
            foreach (var relayId in relayIds)
            {
                if (!TryGetMemberRelay(accountId, relayId, out var relay))
                    return ValueTask.FromResult<IReadOnlyDictionary<Guid, long>?>(null);
                result[relayId] = relay!.MembershipRevision;
            }
            return ValueTask.FromResult<IReadOnlyDictionary<Guid, long>?>(result);
        }
    }

    public ValueTask<IReadOnlySet<AccountId>> GetCurrentMembersAsync(
        Guid relayId,
        IReadOnlyCollection<AccountId> candidateAccountIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            IReadOnlySet<AccountId> result = !this.relays.TryGetValue(relayId, out var relay) || relay.DeletedAt is not null
                ? new HashSet<AccountId>()
                : relay.Members.Select(member => member.AccountId).Where(candidateAccountIds.Contains).ToHashSet();
            return ValueTask.FromResult(result);
        }
    }

    private int CountMemberships(AccountId accountId) => this.relays.Values.Count(relay =>
        relay.DeletedAt is null && relay.Members.Any(member => member.AccountId == accountId));

    private bool TryGetMemberRelay(AccountId accountId, Guid relayId, out RelayState? relay)
    {
        if (!this.relays.TryGetValue(relayId, out relay) || relay.DeletedAt is not null ||
            !relay.Members.Any(member => member.AccountId == accountId))
        {
            relay = null;
            return false;
        }
        return true;
    }

    private RelayStoreFailure? GetOwnerRelay(AccountId accountId, Guid relayId, out RelayState? relay)
    {
        if (!this.relays.TryGetValue(relayId, out relay) || relay.DeletedAt is not null)
            return RelayStoreFailure.NotFound;
        return relay.OwnerAccountId == accountId ? null : RelayStoreFailure.Forbidden;
    }

    private bool TryGetUsableInvitation(
        string tokenHash,
        DateTimeOffset now,
        out RelayState? relay,
        out InvitationState? invitation)
    {
        relay = null;
        if (!this.invitationsByHash.TryGetValue(tokenHash, out invitation) || invitation.AcceptedAt is not null ||
            invitation.RevokedAt is not null || invitation.ExpiresAt <= now ||
            !this.relays.TryGetValue(invitation.RelayId, out relay) || relay.DeletedAt is not null)
            return false;
        return true;
    }

    private static StoredRelay ToStored(RelayState relay) => new(
        relay.RelayId,
        relay.Name,
        relay.NormalizedName,
        relay.OwnerAccountId,
        relay.MembershipRevision,
        relay.Members.Count,
        relay.CreatedAt,
        relay.UpdatedAt);

    private static StoredRelayInvitation ToStored(RelayState relay, InvitationState invitation) => new(
        invitation.InvitationId,
        relay.RelayId,
        relay.Name,
        invitation.CreatedAt,
        invitation.ExpiresAt,
        invitation.AcceptedAt);

    private static ValueTask<RelayStoreResult<T>> Failed<T>(RelayStoreFailure failure) =>
        ValueTask.FromResult(RelayStoreResult<T>.Failed(failure));

    private sealed class RelayState
    {
        public RelayState(Guid relayId, string name, string normalizedName, AccountId ownerAccountId, Guid idempotencyKey, long membershipRevision, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        {
            this.RelayId = relayId;
            this.Name = name;
            this.NormalizedName = normalizedName;
            this.OwnerAccountId = ownerAccountId;
            this.IdempotencyKey = idempotencyKey;
            this.MembershipRevision = membershipRevision;
            this.CreatedAt = createdAt;
            this.UpdatedAt = updatedAt;
        }

        public Guid RelayId { get; }
        public string Name { get; set; }
        public string NormalizedName { get; set; }
        public AccountId OwnerAccountId { get; }
        public Guid IdempotencyKey { get; }
        public long MembershipRevision { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public List<MembershipState> Members { get; } = [];
        public List<InvitationState> Invitations { get; } = [];
        public Dictionary<AccountId, DateTimeOffset> Removals { get; } = [];
    }

    private sealed record MembershipState(Guid MembershipId, AccountId AccountId, DateTimeOffset JoinedAt);

    private sealed class InvitationState
    {
        public InvitationState(Guid invitationId, Guid relayId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        {
            this.InvitationId = invitationId;
            this.RelayId = relayId;
            this.TokenHash = tokenHash;
            this.CreatedAt = createdAt;
            this.ExpiresAt = expiresAt;
        }

        public Guid InvitationId { get; }
        public Guid RelayId { get; }
        public string TokenHash { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset ExpiresAt { get; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public AccountId? AcceptedByAccountId { get; set; }
    }
}
