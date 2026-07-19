using XIV.fm.Server.Application.Listening;

namespace XIV.fm.Server.Api.Listening;

public sealed partial class ListeningPollingHostedService : BackgroundService
{
    private readonly ListeningPollingCoordinator coordinator;
    private readonly ILogger<ListeningPollingHostedService> logger;

    public ListeningPollingHostedService(
        ListeningPollingCoordinator coordinator,
        ILogger<ListeningPollingHostedService> logger)
    {
        this.coordinator = coordinator;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await this.coordinator.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopped(this.logger);
        }
    }

    [LoggerMessage(1, LogLevel.Debug, "The Last.fm listening poll coordinator stopped.")]
    private static partial void LogStopped(ILogger logger);
}
