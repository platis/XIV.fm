namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Allows only canonical HTTPS links controlled by Last.fm to become clickable in game.
/// </summary>
public static class LastFmLinkPolicy
{
    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null ||
            !uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            uri.UserInfo.Length != 0 ||
            (!uri.IsDefaultPort && uri.Port != 443))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        return string.Equals(host, "last.fm", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".last.fm", StringComparison.OrdinalIgnoreCase);
    }
}
