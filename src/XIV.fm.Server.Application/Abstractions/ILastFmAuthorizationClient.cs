using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Application.Abstractions;

public interface ILastFmAuthorizationClient
{
    ValueTask<string> RequestTokenAsync(CancellationToken cancellationToken);

    Uri CreateAuthorizationUri(string providerToken, Uri callbackUri);

    ValueTask<LastFmAccountIdentity> CompleteAuthorizationAsync(
        string providerToken,
        CancellationToken cancellationToken);
}

public sealed class LastFmAuthorizationException : Exception
{
    public LastFmAuthorizationException(string message)
        : base(message)
    {
    }

    public LastFmAuthorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
