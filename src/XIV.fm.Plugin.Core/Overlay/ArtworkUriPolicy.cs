using System.Net;

namespace XIV.fm.Plugin.Core.Overlay;

/// <summary>
/// Rejects artwork locations that could target the game client's local network or use an insecure transport.
/// Provider-specific permission remains a separate server-side requirement.
/// </summary>
public static class ArtworkUriPolicy
{
    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null ||
            !uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0 ||
            (!uri.IsDefaultPort && uri.Port != 443))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Length == 0 ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            !host.Contains('.'))
        {
            return false;
        }

        return !IPAddress.TryParse(host, out _);
    }
}
