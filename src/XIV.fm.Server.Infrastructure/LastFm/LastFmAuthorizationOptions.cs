namespace XIV.fm.Server.Infrastructure.LastFm;

public sealed record LastFmAuthorizationOptions(
    string? ApiKey,
    string? SharedSecret,
    Uri ApiBaseUri,
    Uri BrowserBaseUri)
{
    public static readonly Uri DefaultApiBaseUri = new("https://ws.audioscrobbler.com/2.0/");

    public static readonly Uri DefaultBrowserBaseUri = new("https://www.last.fm/api/auth/");
}
