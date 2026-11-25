using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace XIV.fm.Server.Api.Http;

/// <summary>
/// Resolves the real client address for per-address rate limits. Forwarded addresses are accepted only from
/// the explicitly configured proxy networks; without configuration the connection address is used unchanged.
/// </summary>
public static class TrustedProxies
{
    public const string ConfigurationKey = "XIVFM_TRUSTED_PROXY_NETWORKS";

    public static ForwardedHeadersOptions? CreateOptions(string? networks)
    {
        if (string.IsNullOrWhiteSpace(networks))
            return null;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in networks.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed) || parsed.PrefixLength == 0)
            {
                throw new InvalidOperationException(
                    $"{ConfigurationKey} must list CIDR networks narrower than the whole address space.");
            }

            options.KnownIPNetworks.Add(parsed);
        }

        return options;
    }
}
