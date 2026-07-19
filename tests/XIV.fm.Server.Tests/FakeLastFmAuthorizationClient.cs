using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Tests;

public sealed class FakeLastFmAuthorizationClient : ILastFmAuthorizationClient
{
    private int tokenSequence;

    public ValueTask<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Interlocked.Increment(ref this.tokenSequence);
        return ValueTask.FromResult($"lastfm-test-authorization-token-{sequence:D8}");
    }

    public Uri CreateAuthorizationUri(string providerToken, Uri callbackUri) =>
        new($"https://last.fm.test/authorize?token={Uri.EscapeDataString(providerToken)}&cb={Uri.EscapeDataString(callbackUri.AbsoluteUri)}");

    public ValueTask<LastFmAccountIdentity> CompleteAuthorizationAsync(
        string providerToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!providerToken.StartsWith("lastfm-test-authorization-token-", StringComparison.Ordinal))
            throw new LastFmAuthorizationException("Unexpected test provider token.");

        return ValueTask.FromResult(new LastFmAccountIdentity("CanonicalListener"));
    }
}
