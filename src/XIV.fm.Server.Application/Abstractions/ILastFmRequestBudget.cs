namespace XIV.fm.Server.Application.Abstractions;

public interface ILastFmRequestBudget
{
    ValueTask AcquireAsync(CancellationToken cancellationToken);
}
