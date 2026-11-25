using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Retention;

namespace XIV.fm.Server.Api.Retention;

public sealed partial class RetentionHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IRetentionStore store;
    private readonly RetentionPolicy policy;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RetentionHostedService> logger;

    public RetentionHostedService(
        IRetentionStore store,
        RetentionPolicy policy,
        TimeProvider timeProvider,
        ILogger<RetentionHostedService> logger)
    {
        this.store = store;
        this.policy = policy;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, this.timeProvider, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                await this.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Interval, this.timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var removed = await this.store
                .RemoveExpiredAsync(this.timeProvider.GetUtcNow(), this.policy, cancellationToken)
                .ConfigureAwait(false);
            if (removed.Total != 0)
            {
                LogRemoved(
                    this.logger,
                    removed.LinkSessions,
                    removed.Installations,
                    removed.Invitations,
                    removed.Relays);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Cleanup is repeated later and must never stop the API host.
            LogFailed(this.logger, exception);
        }
    }

    [LoggerMessage(1, LogLevel.Information, "Removed expired records: {LinkSessions} link sessions, {Installations} installations, {Invitations} invitations, {Relays} Relays.")]
    private static partial void LogRemoved(
        ILogger logger,
        int linkSessions,
        int installations,
        int invitations,
        int relays);

    [LoggerMessage(2, LogLevel.Warning, "Removing expired records failed and will be retried.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
