using System.Security.Cryptography;
using System.Text;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Application.Relays;

public enum RelayFailureKind
{
    Validation,
    NotFound,
    Authorization,
    Conflict,
    Quota,
}

public sealed record RelayFailure(
    RelayFailureKind Kind,
    string Code,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

#pragma warning disable CA1000 // Generic result factories keep call sites type-safe.
public sealed record RelayResult<T>(T? Value, RelayFailure? Failure)
{
    public bool IsSuccess => this.Failure is null;

    public static RelayResult<T> Success(T value) => new(value, null);

    public static RelayResult<T> Failed(RelayFailure failure) => new(default, failure);
}

#pragma warning restore CA1000

public sealed record CreatedRelayInvitation(StoredRelayInvitation Invitation, string Token);

public sealed class RelayApplicationService
{
    private const int MinimumNameLength = 3;
    private const int MaximumNameLength = 48;
    private const int MinimumTokenLength = 32;
    private const int MaximumTokenLength = 512;

    private readonly IRelayStore relayStore;
    private readonly IPresenceStore presenceStore;
    private readonly IRelayPresenceSnapshotCache snapshotCache;
    private readonly IRelayTelemetry telemetry;
    private readonly RelayOptions options;
    private readonly TimeProvider timeProvider;

    public RelayApplicationService(
        IRelayStore relayStore,
        IPresenceStore presenceStore,
        IRelayPresenceSnapshotCache snapshotCache,
        IRelayTelemetry telemetry,
        RelayOptions options,
        TimeProvider timeProvider)
    {
        this.relayStore = relayStore;
        this.presenceStore = presenceStore;
        this.snapshotCache = snapshotCache;
        this.telemetry = telemetry;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<RelayResult<StoredRelay>> CreateAsync(
        AccountId accountId,
        string? name,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validated = ValidateName(name);
        if (validated.Failure is not null)
            return RelayResult<StoredRelay>.Failed(validated.Failure);
        if (idempotencyKey == Guid.Empty)
            return Validation<StoredRelay>("idempotencyKey", "A non-empty idempotency key is required.");

        var result = Map(await this.relayStore.CreateAsync(
            accountId,
            validated.Name!,
            validated.NormalizedName!,
            idempotencyKey,
            this.timeProvider.GetUtcNow(),
            this.options,
            cancellationToken).ConfigureAwait(false));
        if (result.IsSuccess)
            this.telemetry.RecordCreated();
        return result;
    }

    public ValueTask<IReadOnlyList<StoredRelay>> ListAsync(AccountId accountId, CancellationToken cancellationToken) =>
        this.relayStore.ListAsync(accountId, cancellationToken);

    public async ValueTask<RelayResult<StoredRelay>> GetAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken) =>
        relayId == Guid.Empty
            ? NotFound<StoredRelay>()
            : Map(await this.relayStore.GetAsync(accountId, relayId, cancellationToken).ConfigureAwait(false));

    public async ValueTask<RelayResult<StoredRelay>> RenameAsync(
        AccountId accountId,
        Guid relayId,
        string? name,
        CancellationToken cancellationToken)
    {
        var validated = ValidateName(name);
        if (validated.Failure is not null)
            return RelayResult<StoredRelay>.Failed(validated.Failure);
        return Map(await this.relayStore.RenameAsync(
            accountId,
            relayId,
            validated.Name!,
            validated.NormalizedName!,
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<RelayResult<bool>> DeleteAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken)
    {
        var result = Map(await this.relayStore.DeleteAsync(
            accountId,
            relayId,
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false));
        if (result.IsSuccess)
        {
            await this.snapshotCache.RemoveRelayAsync(relayId, cancellationToken).ConfigureAwait(false);
            this.telemetry.RecordDeleted();
        }
        return result;
    }

    public async ValueTask<RelayResult<IReadOnlyList<StoredRelayMember>>> ListMembersAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken) =>
        Map(await this.relayStore.ListMembersAsync(accountId, relayId, cancellationToken).ConfigureAwait(false));

    public async ValueTask<RelayResult<bool>> KickAsync(
        AccountId accountId,
        Guid relayId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var kicked = await this.relayStore.KickAsync(
            accountId,
            relayId,
            membershipId,
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        if (!kicked.IsSuccess)
            return RelayResult<bool>.Failed(MapFailure(kicked.Failure!.Value));

        await this.presenceStore.RemoveRelayPublicationAsync(
            kicked.Value,
            relayId,
            cancellationToken).ConfigureAwait(false);
        await this.snapshotCache.RemoveRelayAsync(relayId, cancellationToken).ConfigureAwait(false);
        this.telemetry.RecordKicked();
        return RelayResult<bool>.Success(true);
    }

    public async ValueTask<RelayResult<bool>> LeaveAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken)
    {
        var result = Map(await this.relayStore.LeaveAsync(
            accountId,
            relayId,
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false));
        if (result.IsSuccess)
        {
            await this.presenceStore.RemoveRelayPublicationAsync(accountId, relayId, cancellationToken)
                .ConfigureAwait(false);
            await this.snapshotCache.RemoveRelayAsync(relayId, cancellationToken).ConfigureAwait(false);
            this.telemetry.RecordLeft();
        }
        return result;
    }

    public async ValueTask<RelayResult<CreatedRelayInvitation>> CreateInvitationAsync(
        AccountId accountId,
        Guid relayId,
        int? lifetimeHours,
        CancellationToken cancellationToken)
    {
        var lifetime = lifetimeHours is null
            ? this.options.InvitationLifetime
            : TimeSpan.FromHours(lifetimeHours.Value);
        if (lifetime <= TimeSpan.Zero || lifetime > this.options.MaximumInvitationLifetime)
            return Validation<CreatedRelayInvitation>(
                "lifetimeHours",
                $"Invitation lifetime must be between 1 hour and {(int)this.options.MaximumInvitationLifetime.TotalHours} hours.");

        var token = GenerateToken();
        var now = this.timeProvider.GetUtcNow();
        var stored = await this.relayStore.CreateInvitationAsync(
            accountId,
            relayId,
            HashToken(token),
            now,
            now.Add(lifetime),
            this.options,
            cancellationToken).ConfigureAwait(false);
        if (!stored.IsSuccess)
            return RelayResult<CreatedRelayInvitation>.Failed(MapFailure(stored.Failure!.Value));
        this.telemetry.RecordInvitationCreated();
        return RelayResult<CreatedRelayInvitation>.Success(new CreatedRelayInvitation(stored.Value!, token));
    }

    public async ValueTask<RelayResult<IReadOnlyList<StoredRelayInvitation>>> ListInvitationsAsync(
        AccountId accountId,
        Guid relayId,
        CancellationToken cancellationToken) =>
        Map(await this.relayStore.ListInvitationsAsync(
            accountId,
            relayId,
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false));

    public async ValueTask<RelayResult<bool>> RevokeInvitationAsync(
        AccountId accountId,
        Guid relayId,
        Guid invitationId,
        CancellationToken cancellationToken) =>
        Map(await this.relayStore.RevokeInvitationAsync(
            accountId,
            relayId,
            invitationId,
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false));

    public async ValueTask<RelayResult<StoredRelayInvitation>> PreviewInvitationAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (!IsTokenValid(token))
            return InvalidInvitation<StoredRelayInvitation>();
        return Map(await this.relayStore.PreviewInvitationAsync(
            HashToken(token!),
            this.timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<RelayResult<StoredRelay>> AcceptInvitationAsync(
        AccountId accountId,
        string? token,
        CancellationToken cancellationToken)
    {
        if (!IsTokenValid(token))
            return InvalidInvitation<StoredRelay>();
        var result = Map(await this.relayStore.AcceptInvitationAsync(
            accountId,
            HashToken(token!),
            this.timeProvider.GetUtcNow(),
            this.options,
            cancellationToken).ConfigureAwait(false));
        if (result.IsSuccess)
        {
            await this.snapshotCache.RemoveRelayAsync(result.Value!.RelayId, cancellationToken).ConfigureAwait(false);
            this.telemetry.RecordJoined();
        }
        return result;
    }

    private static (string? Name, string? NormalizedName, RelayFailure? Failure) ValidateName(string? name)
    {
        if (name is null)
            return (null, null, ValidationFailure("name", "Relay name is required."));
        var trimmed = name.Trim();
        var normalized = trimmed.Normalize(NormalizationForm.FormC);
        var length = normalized.EnumerateRunes().Count();
        if (length is < MinimumNameLength or > MaximumNameLength)
            return (null, null, ValidationFailure("name", $"Relay name must contain {MinimumNameLength} to {MaximumNameLength} characters."));
        if (normalized.Any(char.IsControl))
            return (null, null, ValidationFailure("name", "Relay name cannot contain control characters."));
        return (normalized, normalized.ToUpperInvariant(), null);
    }

    private static bool IsTokenValid(string? token) => token is not null &&
        token.Length is >= MinimumTokenLength and <= MaximumTokenLength &&
        !token.Any(char.IsWhiteSpace);

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static RelayResult<T> Map<T>(RelayStoreResult<T> result) => result.IsSuccess
        ? RelayResult<T>.Success(result.Value!)
        : RelayResult<T>.Failed(MapFailure(result.Failure!.Value));

    private static RelayFailure MapFailure(RelayStoreFailure failure) => failure switch
    {
        RelayStoreFailure.NotFound => new(RelayFailureKind.NotFound, "relay_not_found", "The Relay was not found."),
        RelayStoreFailure.Forbidden => new(RelayFailureKind.Authorization, "relay_owner_required", "Relay ownership is required."),
        RelayStoreFailure.Conflict => new(RelayFailureKind.Conflict, "relay_state_conflict", "The Relay state conflicts with this operation."),
        RelayStoreFailure.ActiveOwnershipLimit => new(RelayFailureKind.Quota, "relay_ownership_limit_reached", "The active Relay ownership limit was reached."),
        RelayStoreFailure.RollingCreationLimit => new(RelayFailureKind.Quota, "relay_creation_limit_reached", "The rolling Relay creation limit was reached."),
        RelayStoreFailure.CreationBurstLimit => new(RelayFailureKind.Quota, "relay_creation_rate_exceeded", "Relays may be created only once per minute."),
        RelayStoreFailure.JoinedLimit => new(RelayFailureKind.Quota, "relay_membership_limit_reached", "The joined Relay limit was reached."),
        RelayStoreFailure.MemberLimit => new(RelayFailureKind.Quota, "relay_member_limit_reached", "The Relay member limit was reached."),
        RelayStoreFailure.InvitationLimit => new(RelayFailureKind.Quota, "relay_invitation_limit_reached", "The active invitation limit was reached."),
        RelayStoreFailure.InvitationInvalid => new(RelayFailureKind.NotFound, "relay_invitation_not_found", "The Relay invitation is invalid, expired, revoked, or already used."),
        RelayStoreFailure.RemovalRestricted => new(RelayFailureKind.Authorization, "relay_rejoin_restricted", "An owner must issue a new invitation before this account may rejoin."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    private static RelayResult<T> Validation<T>(string field, string message) =>
        RelayResult<T>.Failed(ValidationFailure(field, message));

    private static RelayFailure ValidationFailure(string field, string message) =>
        new(RelayFailureKind.Validation, "validation_failed", "The Relay request is invalid.", Errors: new Dictionary<string, string[]> { [field] = [message] });

    private static RelayResult<T> NotFound<T>() =>
        RelayResult<T>.Failed(MapFailure(RelayStoreFailure.NotFound));

    private static RelayResult<T> InvalidInvitation<T>() =>
        RelayResult<T>.Failed(MapFailure(RelayStoreFailure.InvitationInvalid));
}
