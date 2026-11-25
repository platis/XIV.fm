using XIV.fm.Contracts.V1;
using XIV.fm.Server.Api.Authentication;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Application.Accounts;
using XIV.fm.Server.Domain.Installations;

namespace XIV.fm.Server.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapDelete(ApiRoutes.DeleteAccount, DeleteAsync)
            .RequireAuthorization()
            .RequireRateLimiting("installation-lifecycle")
            .WithName("DeleteAccount");

    private static async Task DeleteAsync(
        HttpContext context,
        AccountDeletionService service,
        CancellationToken cancellationToken)
    {
        var claim = context.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
        if (!Guid.TryParse(claim, out var installationId) ||
            !await service.DeleteAsync(new InstallationId(installationId), cancellationToken).ConfigureAwait(false))
        {
            await ApiErrorWriter.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                "linked_account_required",
                "A linked account is required.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers.CacheControl = "no-store";
    }
}
