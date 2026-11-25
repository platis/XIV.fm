namespace XIV.fm.Server.Api.Http;

/// <summary>
/// The page a browser shows after Last.fm authorization. It contains only fixed text and an inline style
/// sheet, and its content security policy allows nothing else.
/// </summary>
public static class BrowserResultPage
{
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static async Task WriteAsync(HttpContext context, bool success, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        await context.Response.WriteAsync(success ? Linked : Failed, cancellationToken).ConfigureAwait(false);
    }

    private static string Linked { get; } = Create(
        "linked",
        "Last.fm is linked",
        "Return to the game. You can close this tab.");

    private static string Failed { get; } = Create(
        "failed",
        "This link didn’t work",
        "It may have expired or already been used. Return to the game and connect again.");

    private static string Create(string state, string title, string detail) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <meta name="color-scheme" content="dark">
        <title>{{title}} · XIV.fm</title>
        <style>
        body{margin:0;min-height:100vh;display:grid;place-items:center;background:#1b1b1b;color:#f2f2f2;font:16px/1.5 system-ui,sans-serif}
        main{box-sizing:border-box;width:min(26rem,calc(100% - 2rem));padding:1.75rem;background:#2b2b2b;border:1px solid #4c4c4e;border-radius:6px}
        .name{margin:0;color:#deba73;font-weight:600}
        h1{margin:.75rem 0 .25rem;font-size:1.375rem;line-height:1.25}
        p{margin:0;color:#c9cacd}
        .linked{border-top:3px solid #52c77a}
        .failed{border-top:3px solid #ff8580}
        </style>
        </head>
        <body>
        <main class="{{state}}">
        <p class="name">XIV.fm</p>
        <h1>{{title}}</h1>
        <p>{{detail}}</p>
        </main>
        </body>
        </html>
        """;
}
