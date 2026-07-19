using XIV.fm.Contracts.V1;
using XIV.fm.Server.Api.Authentication;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Api.Endpoints;

public static class RelayEndpoints
{
    public static void MapRelayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ApiRoutes.Relays, CreateAsync).RequireAuthorization().RequireRateLimiting("relay-creation");
        endpoints.MapGet(ApiRoutes.Relays, ListAsync).RequireAuthorization().RequireRateLimiting("relay-read");
        endpoints.MapGet(ApiRoutes.Relay, GetAsync).RequireAuthorization().RequireRateLimiting("relay-read");
        endpoints.MapPatch(ApiRoutes.Relay, RenameAsync).RequireAuthorization().RequireRateLimiting("relay-mutation");
        endpoints.MapDelete(ApiRoutes.Relay, DeleteAsync).RequireAuthorization().RequireRateLimiting("relay-mutation");
        endpoints.MapGet(ApiRoutes.RelayMembers, ListMembersAsync).RequireAuthorization().RequireRateLimiting("relay-read");
        endpoints.MapDelete(ApiRoutes.RelayMember, KickAsync).RequireAuthorization().RequireRateLimiting("relay-mutation");
        endpoints.MapDelete(ApiRoutes.RelayMembership, LeaveAsync).RequireAuthorization().RequireRateLimiting("relay-mutation");
        endpoints.MapPost(ApiRoutes.RelayInvitations, CreateInvitationAsync).RequireAuthorization().RequireRateLimiting("relay-mutation");
        endpoints.MapGet(ApiRoutes.RelayInvitations, ListInvitationsAsync).RequireAuthorization().RequireRateLimiting("relay-read");
        endpoints.MapDelete(ApiRoutes.RelayInvitation, RevokeInvitationAsync).RequireAuthorization().RequireRateLimiting("relay-mutation");
        endpoints.MapPost(ApiRoutes.RelayInvitationPreview, PreviewInvitationAsync).RequireAuthorization().RequireRateLimiting("relay-invitation");
        endpoints.MapPost(ApiRoutes.RelayInvitationAccept, AcceptInvitationAsync).RequireAuthorization().RequireRateLimiting("relay-invitation");
    }

    private static async Task CreateAsync(CreateRelayRequest request, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.CreateAsync(account.Value, request.Name, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (!await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            return;
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(ToResponse(result.Value!, account.Value), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ListAsync(HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var relays = await service.ListAsync(account.Value, cancellationToken).ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new RelayListResponse(relays.Select(relay => ToResponse(relay, account.Value)).ToArray()), cancellationToken).ConfigureAwait(false);
    }

    private static async Task GetAsync(Guid relayId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.GetAsync(account.Value, relayId, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            await context.Response.WriteAsJsonAsync(ToResponse(result.Value!, account.Value), cancellationToken).ConfigureAwait(false);
    }

    private static async Task RenameAsync(Guid relayId, RenameRelayRequest request, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.RenameAsync(account.Value, relayId, request.Name, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            await context.Response.WriteAsJsonAsync(ToResponse(result.Value!, account.Value), cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteAsync(Guid relayId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.DeleteAsync(account.Value, relayId, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task ListMembersAsync(Guid relayId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.ListMembersAsync(account.Value, relayId, cancellationToken).ConfigureAwait(false);
        if (!await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            return;
        var relay = await service.GetAsync(account.Value, relayId, cancellationToken).ConfigureAwait(false);
        var ownerAccountId = relay.IsSuccess ? relay.Value!.OwnerAccountId : account.Value;
        var members = result.Value!.Select(member => new RelayMemberResponse(
            member.MembershipId,
            member.LastFmAccountName,
            member.AccountId == ownerAccountId,
            member.JoinedAt)).ToArray();
        await context.Response.WriteAsJsonAsync(new RelayMemberListResponse(members), cancellationToken).ConfigureAwait(false);
    }

    private static async Task KickAsync(Guid relayId, Guid membershipId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.KickAsync(account.Value, relayId, membershipId, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task LeaveAsync(Guid relayId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.LeaveAsync(account.Value, relayId, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task CreateInvitationAsync(Guid relayId, CreateRelayInvitationRequest? request, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.CreateInvitationAsync(account.Value, relayId, request?.LifetimeHours, cancellationToken).ConfigureAwait(false);
        if (!await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            return;
        var created = result.Value!;
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new CreatedRelayInvitationResponse(created.Invitation.InvitationId, created.Token, created.Invitation.CreatedAt, created.Invitation.ExpiresAt), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ListInvitationsAsync(Guid relayId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.ListInvitationsAsync(account.Value, relayId, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            await context.Response.WriteAsJsonAsync(new RelayInvitationListResponse(result.Value!.Select(ToResponse).ToArray()), cancellationToken).ConfigureAwait(false);
    }

    private static async Task RevokeInvitationAsync(Guid relayId, Guid invitationId, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.RevokeInvitationAsync(account.Value, relayId, invitationId, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task PreviewInvitationAsync(RelayInvitationTokenRequest request, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        if (await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false) is null)
            return;
        var result = await service.PreviewInvitationAsync(request.Token, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            await context.Response.WriteAsJsonAsync(new RelayInvitationPreviewResponse(result.Value!.RelayId, result.Value.RelayName, result.Value.ExpiresAt), cancellationToken).ConfigureAwait(false);
    }

    private static async Task AcceptInvitationAsync(RelayInvitationTokenRequest request, HttpContext context, RelayApplicationService service, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var account = await GetAccountAsync(context, resolver, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return;
        var result = await service.AcceptInvitationAsync(account.Value, request.Token, cancellationToken).ConfigureAwait(false);
        if (await EnsureSuccessAsync(context, result, cancellationToken).ConfigureAwait(false))
            await context.Response.WriteAsJsonAsync(new AcceptRelayInvitationResponse(ToResponse(result.Value!, account.Value)), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<AccountId?> GetAccountAsync(HttpContext context, ILinkedAccountResolver resolver, CancellationToken cancellationToken)
    {
        var claim = context.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
        if (!Guid.TryParse(claim, out var installationId))
        {
            await ApiErrorWriter.WriteAsync(context, 401, "installation_credential_required", "A valid installation credential is required.", cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }
        var account = await resolver.GetForInstallationAsync(new InstallationId(installationId), cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            await ApiErrorWriter.WriteAsync(context, 403, "linked_account_required", "A linked account is required for Custom Relays.", cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }
        return account.AccountId;
    }

    private static async ValueTask<bool> EnsureSuccessAsync<T>(HttpContext context, RelayResult<T> result, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            context.Response.Headers.CacheControl = "no-store";
            return true;
        }
        var failure = result.Failure!;
        var status = failure.Kind switch
        {
            RelayFailureKind.Validation => 400,
            RelayFailureKind.NotFound => 404,
            RelayFailureKind.Authorization => 403,
            RelayFailureKind.Conflict => 409,
            RelayFailureKind.Quota => 429,
            _ => 500,
        };
        if (failure.Code == "relay_creation_rate_exceeded")
            context.Response.Headers.RetryAfter = "60";
        await ApiErrorWriter.WriteAsync(context, status, failure.Code, failure.Title, failure.Detail, failure.Errors, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private static RelayResponse ToResponse(StoredRelay relay, AccountId accountId) =>
        new(relay.RelayId, relay.Name, relay.OwnerAccountId == accountId, relay.MemberCount, relay.CreatedAt, relay.UpdatedAt);

    private static RelayInvitationResponse ToResponse(StoredRelayInvitation invitation) =>
        new(invitation.InvitationId, invitation.CreatedAt, invitation.ExpiresAt, invitation.AcceptedAt);
}
