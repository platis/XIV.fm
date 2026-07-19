using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Application.Abstractions;

public interface ILastFmRecentTracksClient
{
    ValueTask<ListeningObservation> GetCurrentAsync(
        LastFmAccountIdentity identity,
        CancellationToken cancellationToken);
}

public sealed class LastFmRecentTracksException : Exception
{
    public LastFmRecentTracksException(string message)
        : base(message)
    {
    }

    public LastFmRecentTracksException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
