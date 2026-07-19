using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Tests;

public sealed class FakeLastFmAuthorizationClient : ILastFmAuthorizationClient
{
    public Uri CreateAuthorizationUri(Uri callbackUri) =>
        new($"https://last.fm.test/authorize?cb={Uri.EscapeDataString(callbackUri.AbsoluteUri)}");

    public ValueTask<LastFmAccountIdentity> CompleteAuthorizationAsync(
        string providerToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!providerToken.StartsWith("lastfm-test-callback-token-", StringComparison.Ordinal))
            throw new LastFmAuthorizationException("Unexpected test provider token.");

        return ValueTask.FromResult(new LastFmAccountIdentity("CanonicalListener"));
    }
}
