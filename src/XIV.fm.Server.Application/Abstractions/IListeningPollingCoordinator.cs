namespace XIV.fm.Server.Application.Abstractions;

public interface IListeningPollingCoordinator
{
    void NotifyActive(LinkedLastFmAccount account, DateTimeOffset activeUntil);
}
