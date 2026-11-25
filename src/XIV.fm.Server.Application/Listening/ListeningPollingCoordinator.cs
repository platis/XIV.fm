using System.Security.Cryptography;
using System.Threading.Channels;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Application.Listening;

public sealed class ListeningPollingCoordinator : IListeningPollingCoordinator
{
    // A slow provider response delays only its own account. The request budget still limits the rate.
    private const int MaximumConcurrentPolls = 8;

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

    public void Forget(LinkedLastFmAccount account) =>
        this.activations.Writer.TryWrite(new Activation(account, DateTimeOffset.MinValue));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Only this loop touches the plans; a poll owns its plan's schedule until the loop collects it.
        var running = new List<(Task Poll, PollPlan Plan)>();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await this.ApplyActivationsAsync(cancellationToken).ConfigureAwait(false);
                var now = this.timeProvider.GetUtcNow();
                foreach (var expiredAccount in this.plans
                    .Where(pair => pair.Value.ActiveUntil <= now)
                    .Select(pair => pair.Key)
                    .ToArray())
                {
                    this.plans.Remove(expiredAccount);
                }

                foreach (var due in this.plans.Values
                    .Where(plan => !plan.IsPolling && plan.NextPollAt <= now)
                    .OrderBy(plan => plan.NextPollAt)
                    .Take(MaximumConcurrentPolls - running.Count)
                    .ToArray())
                {
                    due.IsPolling = true;
                    running.Add((this.PollAsync(due, cancellationToken).AsTask(), due));
                }

                var wake = Task.Delay(this.GetWakeDelay(now), this.timeProvider, cancellationToken);
                await Task.WhenAny(running.Select(entry => entry.Poll).Append(wake)).ConfigureAwait(false);
                foreach (var finished in running.Where(entry => entry.Poll.IsCompleted).ToArray())
                {
                    running.Remove(finished);
                    finished.Plan.IsPolling = false;
                    await finished.Poll.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // Polls that are still running observe the same cancellation; none may outlive the loop.
            await Task.WhenAll(running.Select(entry => entry.Poll))
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            foreach (var entry in running)
                entry.Plan.IsPolling = false;
        }
    }

    private TimeSpan GetWakeDelay(DateTimeOffset now)
    {
        var waiting = this.plans.Values.Where(plan => !plan.IsPolling).ToArray();
        if (waiting.Length == 0)
            return IdleWakeInterval;

        var delay = waiting.Min(plan => plan.NextPollAt) - now;
        return delay <= TimeSpan.Zero || delay > IdleWakeInterval ? IdleWakeInterval : delay;
    }

    private async ValueTask ApplyActivationsAsync(CancellationToken cancellationToken)
    {
        while (this.activations.Reader.TryRead(out var activation))
        {
            if (activation.ActiveUntil == DateTimeOffset.MinValue)
            {
                this.plans.Remove(activation.Account.AccountId);
                continue;
            }

            if (this.plans.TryGetValue(activation.Account.AccountId, out var existing))
            {
                existing.Account = activation.Account;
                if (activation.ActiveUntil > existing.ActiveUntil)
                    existing.ActiveUntil = activation.ActiveUntil;
                continue;
            }

            ListeningObservation? cached = null;
            try
            {
                cached = await this.stateStore
                    .GetAsync(activation.Account.AccountId, cancellationToken)
                    .ConfigureAwait(false);
                this.telemetry.RecordCacheRead(cached is not null);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The poll itself rechecks the cache and backs off while the store is unavailable.
                this.telemetry.RecordPollFailure(exception);
            }

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
        try
        {
            await this.PollOnceAsync(plan, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Lease, cache, and provider failures all back off; none may end the polling loop.
            this.telemetry.RecordPollFailure(exception);
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

            plan.NextPollAt = this.timeProvider.GetUtcNow().Add(backoff);
        }
    }

    private async ValueTask PollOnceAsync(
        PollPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = plan.Account;
        var lease = await this.leaseStore
            .TryAcquireAsync(account.AccountId, PollLeaseLifetime, cancellationToken)
            .ConfigureAwait(false);
        if (lease is null)
        {
            this.telemetry.RecordLeaseContention();
            var contended = await this.stateStore
                .GetAsync(account.AccountId, cancellationToken)
                .ConfigureAwait(false);
            this.telemetry.RecordCacheRead(contended is not null);
            plan.NextPollAt = contended is null
                ? now.Add(InitialBackoff)
                : contended.ObservedAt.Add(this.freshnessPolicy.GetPollInterval(contended.Status));
            if (plan.NextPollAt <= now)
                plan.NextPollAt = now.Add(InitialBackoff);
            return;
        }

        await using var acquiredLease = lease.ConfigureAwait(false);
        var shared = await this.stateStore
            .GetAsync(account.AccountId, cancellationToken)
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
            .GetCurrentAsync(account.Identity, cancellationToken)
            .ConfigureAwait(false);
        await this.stateStore
            .SetAsync(account.AccountId, observation, cancellationToken)
            .ConfigureAwait(false);
        this.telemetry.RecordPollSuccess();
        plan.ConsecutiveFailures = 0;
        plan.NextPollAt = observation.ObservedAt.Add(AddScheduleJitter(
            this.freshnessPolicy.GetPollInterval(observation.Status)));
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

        public bool IsPolling { get; set; }
    }
}
