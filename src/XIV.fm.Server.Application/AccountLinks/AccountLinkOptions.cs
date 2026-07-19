namespace XIV.fm.Server.Application.AccountLinks;

public sealed record AccountLinkOptions(Uri PublicBaseUri, TimeSpan Lifetime)
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);
}
