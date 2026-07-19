using XIV.fm.Contracts.V1;
using XIV.fm.Server.Api.Authentication;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Api.Telemetry;
using XIV.fm.Server.Application.Sync;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Api.Endpoints;

public static class SyncEndpoints
{
    public static RouteHandlerBuilder MapSyncEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(ApiRoutes.Sync, HandleAsync)
            .RequireAuthorization()
            .RequireRateLimiting("sync")
            .WithName("SyncPresence");

    private static async Task HandleAsync(
        HttpContext context,
        SyncRequest? request,
        SyncApplicationService syncService,
        XivFmTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        telemetry.RecordSyncRequest();
        var installationClaim = context.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
        if (!Guid.TryParse(installationClaim, out var installationGuid) || installationGuid == Guid.Empty)
        {
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "installation_credential_required",
                "A valid installation credential is required.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await syncService.SyncAsync(
            new InstallationId(installationGuid),
            request,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var failure = result.Failure!;
            var status = failure.Kind switch
            {
                SyncFailureKind.Validation => StatusCodes.Status400BadRequest,
                SyncFailureKind.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError,
            };
            await ApiErrorWriter.WriteAsync(
                context,
                status,
                failure.Code,
                failure.Title,
                failure.Detail,
                failure.Errors,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        telemetry.RecordSyncSuccess();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(result.Response, cancellationToken).ConfigureAwait(false);
    }
}
