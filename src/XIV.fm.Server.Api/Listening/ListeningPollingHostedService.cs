using XIV.fm.Server.Application.Listening;

namespace XIV.fm.Server.Api.Listening;

public sealed partial class ListeningPollingHostedService : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

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
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await this.coordinator.RunAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // An escaped exception would otherwise stop the whole API host.
                    LogRestarting(this.logger, exception);
                    await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogStopped(this.logger);
        }
    }

    [LoggerMessage(1, LogLevel.Debug, "The Last.fm listening poll coordinator stopped.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(2, LogLevel.Error, "The Last.fm listening poll coordinator failed and will restart.")]
    private static partial void LogRestarting(ILogger logger, Exception exception);
}
