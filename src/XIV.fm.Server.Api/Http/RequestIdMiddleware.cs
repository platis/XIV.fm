namespace XIV.fm.Server.Api.Http;

public sealed class RequestIdMiddleware
{
    public const string HeaderName = "X-Request-ID";

    private readonly RequestDelegate next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = TryGetValidRequestId(context.Request.Headers[HeaderName].ToString())
            ?? Guid.NewGuid().ToString("N");
        context.TraceIdentifier = requestId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        await this.next(context).ConfigureAwait(false);
    }

    private static string? TryGetValidRequestId(string value)
    {
        if (value.Length is < 1 or > 128)
            return null;

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-'))
                return null;
        }

        return value;
    }
}
