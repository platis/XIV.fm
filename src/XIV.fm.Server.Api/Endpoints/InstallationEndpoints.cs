using XIV.fm.Contracts.V1;
using XIV.fm.Server.Api.Authentication;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Installations;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Api.Endpoints;

public static class InstallationEndpoints
{
    public static void MapInstallationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ApiRoutes.RotateCurrentInstallation, RotateAsync)
            .RequireAuthorization()
            .RequireRateLimiting("installation-lifecycle")
            .WithName("RotateCurrentInstallationCredential");
        endpoints.MapDelete(ApiRoutes.RevokeCurrentInstallation, RevokeAsync)
            .RequireAuthorization()
            .RequireRateLimiting("installation-lifecycle")
            .WithName("RevokeCurrentInstallation");
        endpoints.MapGet(ApiRoutes.Installations, ListAsync)
            .RequireAuthorization()
            .RequireRateLimiting("relay-read")
            .WithName("ListInstallations");
        endpoints.MapDelete(ApiRoutes.Installation, RevokeLinkedAsync)
            .RequireAuthorization()
            .RequireRateLimiting("installation-lifecycle")
            .WithName("RevokeInstallation");
    }

    private static async Task RotateAsync(
        HttpContext context,
        IInstallationCredentialStore credentialStore,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstallationId(context, out var installationId))
        {
            await WriteMissingCredentialAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var credential = await credentialStore.RotateAndIssueAsync(
                installationId,
                cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(
                new InstallationCredentialResponse(credential),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                "installation_credential_state_conflict",
                "The installation credential cannot be rotated in its current state.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ListAsync(
        HttpContext context,
        InstallationApplicationService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstallationId(context, out var current))
        {
            await WriteMissingCredentialAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var installations = await service.ListAsync(current, cancellationToken).ConfigureAwait(false);
        if (installations is null)
        {
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                "linked_account_required",
                "A linked account is required.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(
            new InstallationListResponse(installations
                .Select(installation => new InstallationResponse(
                    installation.InstallationId.Value,
                    installation.LinkedAt,
                    installation.LastUsedAt,
                    installation.InstallationId == current))
                .ToArray()),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RevokeLinkedAsync(
        Guid installationId,
        HttpContext context,
        InstallationApplicationService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstallationId(context, out var current))
        {
            await WriteMissingCredentialAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await service.RevokeLinkedAsync(current, new InstallationId(installationId), cancellationToken)
            .ConfigureAwait(false))
        {
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status404NotFound,
                "installation_not_found",
                "The installation was not found.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers.CacheControl = "no-store";
    }

    private static async Task RevokeAsync(
        HttpContext context,
        InstallationApplicationService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetInstallationId(context, out var installationId))
        {
            await WriteMissingCredentialAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        await service.RevokeAsync(installationId, cancellationToken).ConfigureAwait(false);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers.CacheControl = "no-store";
    }

    private static bool TryGetInstallationId(HttpContext context, out InstallationId installationId)
    {
        var claim = context.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
        if (Guid.TryParse(claim, out var value) && value != Guid.Empty)
        {
            installationId = new InstallationId(value);
            return true;
        }

        installationId = default;
        return false;
    }

    private static Task WriteMissingCredentialAsync(HttpContext context, CancellationToken cancellationToken) =>
        ApiErrorWriter.WriteAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "installation_credential_required",
            "A valid installation credential is required.",
            cancellationToken: cancellationToken);
}
