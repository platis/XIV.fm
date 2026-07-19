using XIV.fm.Contracts.V1;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.AccountLinks;
using XIV.fm.Server.Domain.AccountLinks;

namespace XIV.fm.Server.Api.Endpoints;

public static class AccountLinkEndpoints
{
    public static void MapAccountLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ApiRoutes.StartAccountLink, StartAsync)
            .RequireRateLimiting("account-link-start")
            .WithName("StartAccountLink");
        endpoints.MapPost(ApiRoutes.AccountLinkStatus, GetStatusAsync)
            .RequireRateLimiting("account-link-status")
            .WithName("GetAccountLinkStatus");
        endpoints.MapGet(ApiRoutes.CompleteAccountLink, CompleteAsync)
            .RequireRateLimiting("account-link-callback")
            .WithName("CompleteAccountLink");
    }

    private static async Task StartAsync(
        HttpContext context,
        AccountLinkApplicationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var started = await service.StartAsync(cancellationToken).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(
                new StartAccountLinkResponse(
                    started.SessionId.Value,
                    started.AuthorizationUri,
                    started.LinkCredential,
                    started.ExpiresAt,
                    2),
                cancellationToken).ConfigureAwait(false);
        }
        catch (LastFmAuthorizationException)
        {
            await WriteLastFmUnavailableAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task GetStatusAsync(
        Guid linkSessionId,
        AccountLinkStatusRequest request,
        HttpContext context,
        AccountLinkApplicationService service,
        CancellationToken cancellationToken)
    {
        if (linkSessionId == Guid.Empty || request is null)
        {
            await WriteNotFoundAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        var state = await service.GetStateAsync(
            new AccountLinkSessionId(linkSessionId),
            request.LinkCredential,
            cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            await WriteNotFoundAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(
            new AccountLinkStatusResponse(state.Status, state.ExpiresAt, state.LastFmAccountName),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompleteAsync(
        Guid linkSessionId,
        string? state,
        string? token,
        HttpContext context,
        AccountLinkApplicationService service,
        CancellationToken cancellationToken)
    {
        if (linkSessionId == Guid.Empty || state is null || token is null)
        {
            await WriteBrowserResultAsync(context, false, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var completed = await service.CompleteAsync(
                new AccountLinkSessionId(linkSessionId),
                state,
                token,
                cancellationToken).ConfigureAwait(false);
            await WriteBrowserResultAsync(context, completed, cancellationToken).ConfigureAwait(false);
        }
        catch (LastFmAuthorizationException)
        {
            await WriteBrowserResultAsync(context, false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteBrowserResultAsync(
        HttpContext context,
        bool success,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(
            success
                ? "XIV.fm is linked. You may close this browser window and return to the game."
                : "This XIV.fm link is invalid, expired, or already used. Return to the game and start again.",
            cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteNotFoundAsync(HttpContext context, CancellationToken cancellationToken) =>
        ApiErrorWriter.WriteAsync(
            context,
            StatusCodes.Status404NotFound,
            "account_link_not_found",
            "The account-link session was not found.",
            cancellationToken: cancellationToken);

    private static Task WriteLastFmUnavailableAsync(HttpContext context, CancellationToken cancellationToken) =>
        ApiErrorWriter.WriteAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            "lastfm_authorization_unavailable",
            "Last.fm account authorization is temporarily unavailable.",
            cancellationToken: cancellationToken);
}
