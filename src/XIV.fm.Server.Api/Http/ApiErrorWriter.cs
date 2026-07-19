using System.Text.Json;
using XIV.fm.Contracts.V1;

namespace XIV.fm.Server.Api.Http;

public static class ApiErrorWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null,
        CancellationToken cancellationToken = default)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        var problem = new ApiError(
            new Uri($"https://xiv.fm/problems/{code.Replace('_', '-')}", UriKind.Absolute),
            title,
            status,
            code,
            context.TraceIdentifier,
            detail,
            context.Request.Path,
            errors);
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }
}
