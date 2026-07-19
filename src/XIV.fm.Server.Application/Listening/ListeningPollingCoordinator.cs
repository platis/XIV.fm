using System.Security.Cryptography;
using System.Threading.Channels;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Application.Listening;

public sealed class ListeningPollingCoordinator : IListeningPollingCoordinator
{
    private static readonly TimeSpan IdleWakeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollLeaseLifetime = TimeSpan.FromSeconds(15);

    private readonly Channel<Activation> activations = Channel.CreateBounded<Activation>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Dictionary<AccountId, PollPlan> plans = [];
    private readonly ILastFmRecentTracksClient lastFm;
    private readonly IListeningStateStore stateStore;
    private readonly IListeningPollLeaseStore leaseStore;
    private readonly IListeningPollingTelemetry telemetry;
    private readonly TimeProvider timeProvider;
    private readonly ListeningPollingOptions options;
    private readonly ListeningFreshnessPolicy freshnessPolicy;

    public ListeningPollingCoordinator(
        ILastFmRecentTracksClient lastFm,
        IListeningStateStore stateStore,
        IListeningPollLeaseStore leaseStore,
        TimeProvider timeProvider,
        ListeningPollingOptions options,
        ListeningFreshnessPolicy freshnessPolicy,
        IListeningPollingTelemetry? telemetry = null)
    {
        this.lastFm = lastFm;
        this.stateStore = stateStore;
        this.leaseStore = leaseStore;
        this.telemetry = telemetry ?? NullListeningPollingTelemetry.Instance;
        this.timeProvider = timeProvider;
        this.options = options;
        this.freshnessPolicy = freshnessPolicy;
    }

    public void NotifyActive(LinkedLastFmAccount account, DateTimeOffset activeUntil) =>
        this.activations.Writer.TryWrite(new Activation(account, activeUntil));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await this.ApplyActivationsAsync(cancellationToken).ConfigureAwait(false);
            var now = this.timeProvider.GetUtcNow();
            foreach (var expiredAccount in this.plans
                .Where(pair => pair.Value.ActiveUntil <= now)
                .Select(pair => pair.Key)
                .ToArray())
            {
                this.plans.Remove(expiredAccount);
            }

            var due = this.plans.Values
                .Where(plan => plan.NextPollAt <= now)
                .OrderBy(plan => plan.NextPollAt)
                .FirstOrDefault();
            if (due is not null)
            {
                await this.PollAsync(due, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var nextDue = this.plans.Count == 0
                ? now.Add(IdleWakeInterval)
                : this.plans.Values.Min(plan => plan.NextPollAt);
            var delay = nextDue - now;
            if (delay <= TimeSpan.Zero || delay > IdleWakeInterval)
                delay = IdleWakeInterval;
            await Task.Delay(delay, this.timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ApplyActivationsAsync(CancellationToken cancellationToken)
    {
        while (this.activations.Reader.TryRead(out var activation))
        {
            if (this.plans.TryGetValue(activation.Account.AccountId, out var existing))
            {
                existing.Account = activation.Account;
                if (activation.ActiveUntil > existing.ActiveUntil)
                    existing.ActiveUntil = activation.ActiveUntil;
                continue;
            }

            var cached = await this.stateStore
                .GetAsync(activation.Account.AccountId, cancellationToken)
                .ConfigureAwait(false);
            this.telemetry.RecordCacheRead(cached is not null);
            var now = this.timeProvider.GetUtcNow();
            var nextPollAt = cached is null
                ? now
                : cached.ObservedAt.Add(this.freshnessPolicy.GetPollInterval(cached.Status));
            this.plans.Add(
                activation.Account.AccountId,
                new PollPlan(
                    activation.Account,
                    activation.ActiveUntil,
                    nextPollAt > now ? nextPollAt : now));
        }
    }

    private async ValueTask PollAsync(PollPlan plan, CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var lease = await this.leaseStore
            .TryAcquireAsync(plan.Account.AccountId, PollLeaseLifetime, cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
        {
            this.telemetry.RecordLeaseContention();
            var shared = await this.stateStore
                .GetAsync(plan.Account.AccountId, cancellationToken)
                .ConfigureAwait(false);
            this.telemetry.RecordCacheRead(shared is not null);
            plan.NextPollAt = shared is null
                ? now.Add(InitialBackoff)
                : shared.ObservedAt.Add(this.freshnessPolicy.GetPollInterval(shared.Status));
            if (plan.NextPollAt <= now)
                plan.NextPollAt = now.Add(InitialBackoff);
            return;
        }

        await using var acquiredLease = lease.ConfigureAwait(false);
        try
        {
            var shared = await this.stateStore
                .GetAsync(plan.Account.AccountId, cancellationToken)
                .ConfigureAwait(false);
            this.telemetry.RecordCacheRead(shared is not null);
            if (shared is not null)
            {
                var sharedNextPollAt = shared.ObservedAt.Add(
                    this.freshnessPolicy.GetPollInterval(shared.Status));
                if (sharedNextPollAt > now)
                {
                    plan.ConsecutiveFailures = 0;
                    plan.NextPollAt = sharedNextPollAt;
                    return;
                }
            }

            var observation = await this.lastFm
                    .GetCurrentAsync(plan.Account.Identity, cancellationToken)
                    .ConfigureAwait(false);
            await this.stateStore
                .SetAsync(plan.Account.AccountId, observation, cancellationToken)
                .ConfigureAwait(false);
            this.telemetry.RecordPollSuccess();
            plan.ConsecutiveFailures = 0;
            plan.NextPollAt = observation.ObservedAt.Add(AddScheduleJitter(
                this.freshnessPolicy.GetPollInterval(observation.Status)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            this.telemetry.RecordPollFailure();
            plan.ConsecutiveFailures++;
            var exponent = Math.Min(plan.ConsecutiveFailures - 1, 16);
            var maximumTicks = Math.Min(
                this.options.MaximumBackoff.Ticks,
                InitialBackoff.Ticks * (1L << exponent));
            var backoff = FullJitter(TimeSpan.FromTicks(maximumTicks));
            if (plan.ConsecutiveFailures >= this.options.CircuitFailureThreshold &&
                backoff < this.options.CircuitBreakDuration)
            {
                backoff = this.options.CircuitBreakDuration;
            }

            plan.NextPollAt = now.Add(backoff);
        }
    }

    private static TimeSpan AddScheduleJitter(TimeSpan interval)
    {
        var jitterRange = Math.Max(1L, interval.Ticks / 10);
        var offset = RandomNumberGenerator.GetInt32(-1_000_000, 1_000_001) / 1_000_000D;
        return interval.Add(TimeSpan.FromTicks((long)(jitterRange * offset)));
    }

    private static TimeSpan FullJitter(TimeSpan maximum)
    {
        var fraction = RandomNumberGenerator.GetInt32(1, 1_000_001) / 1_000_000D;
        return TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, (long)(maximum.Ticks * fraction)));
    }

    private sealed record Activation(LinkedLastFmAccount Account, DateTimeOffset ActiveUntil);

    private sealed class PollPlan
    {
        public PollPlan(LinkedLastFmAccount account, DateTimeOffset activeUntil, DateTimeOffset nextPollAt)
        {
            this.Account = account;
            this.ActiveUntil = activeUntil;
            this.NextPollAt = nextPollAt;
        }

        public LinkedLastFmAccount Account { get; set; }

        public DateTimeOffset ActiveUntil { get; set; }

        public DateTimeOffset NextPollAt { get; set; }

        public int ConsecutiveFailures { get; set; }
    }
}
