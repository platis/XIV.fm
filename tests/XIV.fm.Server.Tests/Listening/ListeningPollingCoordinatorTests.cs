using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;
using XIV.fm.Server.Infrastructure.Listening;

namespace XIV.fm.Server.Tests.Listening;

public sealed class ListeningPollingCoordinatorTests
{
    [Fact]
    public async Task DuplicateInstallationsShareOneImmediateAccountPoll()
    {
        var lastFm = new CountingRecentTracksClient();
        var store = new InMemoryListeningStateStore();
        var options = ListeningPollingOptions.Default;
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        var account = new LinkedLastFmAccount(
            new AccountId(Guid.NewGuid()),
            new LastFmAccountIdentity("CanonicalListener"));
        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);

        coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CallCount) == 1);
        await Task.Delay(100);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, lastFm.CallCount);
        Assert.NotNull(await store.GetAsync(account.AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task PlannerCoalescesOneThousandSessionsIntoOneHundredWorstCaseAccounts()
    {
        var lastFm = new CountingRecentTracksClient();
        var store = new InMemoryListeningStateStore();
        var options = ListeningPollingOptions.Default;
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        var accounts = Enumerable.Range(0, 100)
            .Select(_ => new LinkedLastFmAccount(
                new AccountId(Guid.NewGuid()),
                new LastFmAccountIdentity($"Listener{Guid.NewGuid():N}")))
            .ToArray();
        foreach (var account in accounts)
        {
            for (var installation = 0; installation < 10; installation++)
                coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        }

        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CallCount) == 100);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(100, lastFm.CallCount);
    }

    [Fact]
    public async Task PlannerReusesFreshCacheAcrossTwoHundredMixedAccounts()
    {
        var lastFm = new CountingRecentTracksClient();
        var store = new InMemoryListeningStateStore();
        var options = ListeningPollingOptions.Default;
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 200; index++)
        {
            var account = new LinkedLastFmAccount(
                new AccountId(Guid.NewGuid()),
                new LastFmAccountIdentity($"Listener{Guid.NewGuid():N}"));
            var playing = index < 100;
            await store.SetAsync(
                account.AccountId,
                new ListeningObservation(
                    playing ? ListeningObservationStatus.Playing : ListeningObservationStatus.NotPlaying,
                    now,
                    playing ? new NormalizedTrack("Track", "Artist", null, null, null, null) : null),
                CancellationToken.None);
            coordinator.NotifyActive(account, now.AddMinutes(1));
        }

        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);
        await Task.Delay(200);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, lastFm.CallCount);
    }

    [Fact]
    public async Task SharedLeaseAndCachePreventDuplicateReplicaPolls()
    {
        var lastFm = new CountingRecentTracksClient();
        var store = new InMemoryListeningStateStore();
        var leases = new InMemoryListeningPollLeaseStore();
        var options = ListeningPollingOptions.Default;
        var freshness = new ListeningFreshnessPolicy(options);
        var first = new ListeningPollingCoordinator(
            lastFm,
            store,
            leases,
            TimeProvider.System,
            options,
            freshness);
        var second = new ListeningPollingCoordinator(
            lastFm,
            store,
            leases,
            TimeProvider.System,
            options,
            freshness);
        var account = new LinkedLastFmAccount(
            new AccountId(Guid.NewGuid()),
            new LastFmAccountIdentity("CanonicalListener"));
        using var cancellation = new CancellationTokenSource();
        first.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        second.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        var firstRun = first.RunAsync(cancellation.Token);
        var secondRun = second.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CallCount) == 1);
        await Task.Delay(100);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondRun);
        Assert.Equal(1, lastFm.CallCount);
    }

    [Fact]
    public async Task RepeatedFailuresOpenTheConfiguredCircuit()
    {
        var lastFm = new FailingRecentTracksClient();
        var store = new InMemoryListeningStateStore();
        var options = new ListeningPollingOptions(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromMilliseconds(100),
            2,
            TimeSpan.FromSeconds(2));
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        var account = new LinkedLastFmAccount(
            new AccountId(Guid.NewGuid()),
            new LastFmAccountIdentity("CanonicalListener"));
        using var cancellation = new CancellationTokenSource();
        coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        var run = coordinator.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CallCount) == 2);
        await Task.Delay(1200);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(2, lastFm.CallCount);
    }

    [Fact]
    public async Task ExpiredActivityDoesNotStartProviderWork()
    {
        var lastFm = new CountingRecentTracksClient();
        var store = new InMemoryListeningStateStore();
        var options = ListeningPollingOptions.Default;
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        var account = new LinkedLastFmAccount(
            new AccountId(Guid.NewGuid()),
            new LastFmAccountIdentity("CanonicalListener"));
        using var cancellation = new CancellationTokenSource();
        coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddSeconds(-1));
        var run = coordinator.RunAsync(cancellation.Token);

        await Task.Delay(100);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, lastFm.CallCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FailingRecentTracksClient : ILastFmRecentTracksClient
    {
        public int CallCount;

        public ValueTask<ListeningObservation> GetCurrentAsync(
            LastFmAccountIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref this.CallCount);
            throw new LastFmRecentTracksException("Expected test failure.");
        }
    }

    private sealed class CountingRecentTracksClient : ILastFmRecentTracksClient
    {
        public int CallCount;

        public ValueTask<ListeningObservation> GetCurrentAsync(
            LastFmAccountIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref this.CallCount);
            return ValueTask.FromResult(new ListeningObservation(
                ListeningObservationStatus.NotPlaying,
                DateTimeOffset.UtcNow,
                null));
        }
    }
}
