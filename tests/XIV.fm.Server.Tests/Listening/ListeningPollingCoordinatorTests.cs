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
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(180),
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

    [Fact]
    public async Task TransientLeaseFailureDoesNotEndPolling()
    {
        var lastFm = new CountingRecentTracksClient();
        var leases = new FlakyPollLeaseStore();
        var options = CreateFastRetryOptions();
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            new InMemoryListeningStateStore(),
            leases,
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        using var cancellation = new CancellationTokenSource();
        coordinator.NotifyActive(CreateAccount(), DateTimeOffset.UtcNow.AddMinutes(1));
        var run = coordinator.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref leases.CallCount) >= 1);
        Assert.False(run.IsCompleted);
        leases.IsAvailable = true;
        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CallCount) == 1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task TransientCacheFailureDoesNotEndPolling()
    {
        var lastFm = new CountingRecentTracksClient();
        var store = new FlakyListeningStateStore();
        var options = CreateFastRetryOptions();
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        using var cancellation = new CancellationTokenSource();
        coordinator.NotifyActive(CreateAccount(), DateTimeOffset.UtcNow.AddMinutes(1));
        var run = coordinator.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref store.CallCount) >= 2);
        Assert.False(run.IsCompleted);
        store.IsAvailable = true;
        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CallCount) == 1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SlowAccountDoesNotDelayOthers()
    {
        var slow = new LinkedLastFmAccount(new AccountId(Guid.NewGuid()), new LastFmAccountIdentity("SlowListener"));
        var lastFm = new BlockingRecentTracksClient("SlowListener");
        var store = new InMemoryListeningStateStore();
        var options = ListeningPollingOptions.Default;
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            store,
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        coordinator.NotifyActive(slow, DateTimeOffset.UtcNow.AddMinutes(1));
        var others = Enumerable.Range(0, 20).Select(_ => CreateAccount()).ToArray();
        foreach (var account in others)
            coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref lastFm.CompletedCount) == others.Length);
        Assert.Equal(1, lastFm.BlockedCount);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task ConcurrentPollsAreBoundedAndNeverRepeatAnAccount()
    {
        var lastFm = new BlockingRecentTracksClient(blockedName: null);
        var options = ListeningPollingOptions.Default;
        var coordinator = new ListeningPollingCoordinator(
            lastFm,
            new InMemoryListeningStateStore(),
            new InMemoryListeningPollLeaseStore(),
            TimeProvider.System,
            options,
            new ListeningFreshnessPolicy(options));
        for (var index = 0; index < 30; index++)
        {
            var account = new LinkedLastFmAccount(
                new AccountId(Guid.NewGuid()),
                new LastFmAccountIdentity($"Listener{index}"));
            for (var repeat = 0; repeat < 3; repeat++)
                coordinator.NotifyActive(account, DateTimeOffset.UtcNow.AddMinutes(1));
        }

        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => Volatile.Read(ref lastFm.BlockedCount) >= 8);
        await Task.Delay(300);
        Assert.Equal(8, lastFm.BlockedCount);
        Assert.Equal(8, lastFm.Names.Distinct().Count());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private static LinkedLastFmAccount CreateAccount() => new(
        new AccountId(Guid.NewGuid()),
        new LastFmAccountIdentity("CanonicalListener"));

    private static ListeningPollingOptions CreateFastRetryOptions() => new(
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(180),
        TimeSpan.FromMilliseconds(100),
        100,
        TimeSpan.FromSeconds(1));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FlakyPollLeaseStore : IListeningPollLeaseStore
    {
        private readonly InMemoryListeningPollLeaseStore inner = new();
        public int CallCount;

        public volatile bool IsAvailable;

        public ValueTask<IAsyncDisposable?> TryAcquireAsync(
            AccountId accountId,
            TimeSpan leaseLifetime,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.CallCount);
            return this.IsAvailable
                ? this.inner.TryAcquireAsync(accountId, leaseLifetime, cancellationToken)
                : throw new TimeoutException("Expected test failure.");
        }
    }

    private sealed class FlakyListeningStateStore : IListeningStateStore
    {
        private readonly InMemoryListeningStateStore inner = new();
        public int CallCount;

        public volatile bool IsAvailable;

        public ValueTask<ListeningObservation?> GetAsync(AccountId accountId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.CallCount);
            return this.IsAvailable
                ? this.inner.GetAsync(accountId, cancellationToken)
                : throw new TimeoutException("Expected test failure.");
        }

        public ValueTask<IReadOnlyDictionary<AccountId, ListeningObservation>> GetManyAsync(
            IReadOnlyList<AccountId> accountIds,
            CancellationToken cancellationToken) =>
            this.inner.GetManyAsync(accountIds, cancellationToken);

        public ValueTask SetAsync(
            AccountId accountId,
            ListeningObservation observation,
            CancellationToken cancellationToken) =>
            this.inner.SetAsync(accountId, observation, cancellationToken);

        public ValueTask RemoveAsync(AccountId accountId, CancellationToken cancellationToken) =>
            this.inner.RemoveAsync(accountId, cancellationToken);
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

    private sealed class BlockingRecentTracksClient(string? blockedName) : ILastFmRecentTracksClient
    {
        public int BlockedCount;
        public int CompletedCount;

        public System.Collections.Concurrent.ConcurrentBag<string> Names { get; } = [];

        public async ValueTask<ListeningObservation> GetCurrentAsync(
            LastFmAccountIdentity identity,
            CancellationToken cancellationToken)
        {
            this.Names.Add(identity.CanonicalName);
            if (blockedName is null || identity.CanonicalName == blockedName)
            {
                Interlocked.Increment(ref this.BlockedCount);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Interlocked.Increment(ref this.CompletedCount);
            return new ListeningObservation(ListeningObservationStatus.NotPlaying, DateTimeOffset.UtcNow, null);
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
