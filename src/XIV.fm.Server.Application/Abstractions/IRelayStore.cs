using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Application.Abstractions;

public enum RelayStoreFailure
{
    NotFound,
    Forbidden,
    Conflict,
    ActiveOwnershipLimit,
    RollingCreationLimit,
    CreationBurstLimit,
    JoinedLimit,
    MemberLimit,
    InvitationLimit,
    InvitationInvalid,
    RemovalRestricted,
}

public sealed record StoredRelay(
    Guid RelayId,
    string Name,
    string NormalizedName,
    AccountId OwnerAccountId,
    long MembershipRevision,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StoredRelayMember(
    Guid MembershipId,
    AccountId AccountId,
    string LastFmAccountName,
    DateTimeOffset JoinedAt);

public sealed record StoredRelayInvitation(
    Guid InvitationId,
    Guid RelayId,
    string RelayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt);

#pragma warning disable CA1000 // Generic result factories keep call sites type-safe.
public sealed record RelayStoreResult<T>(T? Value, RelayStoreFailure? Failure)
{
    public bool IsSuccess => this.Failure is null;

    public static RelayStoreResult<T> Success(T value) => new(value, null);

    public static RelayStoreResult<T> Failed(RelayStoreFailure failure) => new(default, failure);
}

#pragma warning restore CA1000

public interface IRelayStore
{
    ValueTask<RelayStoreResult<StoredRelay>> CreateAsync(
        AccountId accountId,
        string name,
        string normalizedName,
        Guid idempotencyKey,
        DateTimeOffset now,
        RelayOptions limits,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<StoredRelay>> ListAsync(AccountId accountId, CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<StoredRelay>> GetAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<StoredRelay>> RenameAsync(
        AccountId accountId,
        Guid relayId,
        string name,
        string normalizedName,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<bool>> DeleteAsync(
        AccountId accountId,
        Guid relayId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<IReadOnlyList<StoredRelayMember>>> ListMembersAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<AccountId>> KickAsync(
        AccountId accountId,
        Guid relayId,
        Guid membershipId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<bool>> LeaveAsync(
        AccountId accountId,
        Guid relayId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<StoredRelayInvitation>> CreateInvitationAsync(
        AccountId accountId,
        Guid relayId,
        string tokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        RelayOptions limits,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<IReadOnlyList<StoredRelayInvitation>>> ListInvitationsAsync(
        AccountId accountId,
        Guid relayId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<bool>> RevokeInvitationAsync(
        AccountId accountId,
        Guid relayId,
        Guid invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<StoredRelayInvitation>> PreviewInvitationAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    ValueTask<RelayStoreResult<StoredRelay>> AcceptInvitationAsync(
        AccountId accountId,
        string tokenHash,
        DateTimeOffset now,
        RelayOptions limits,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<Guid, long>?> GetMembershipRevisionsAsync(
        AccountId accountId,
        IReadOnlyList<Guid> relayIds,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlySet<AccountId>> GetCurrentMembersAsync(
        Guid relayId,
        IReadOnlyCollection<AccountId> candidateAccountIds,
        CancellationToken cancellationToken);
}
