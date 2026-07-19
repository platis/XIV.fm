using XIV.fm.Contracts.V1;
using XIV.fm.Server.Application.AccountLinks;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Authentication;

namespace XIV.fm.Server.Tests.AccountLinks;

public sealed class AccountLinkApplicationServiceTests
{
    [Fact]
    public async Task PendingSessionExpiresAtTheConfiguredDeadline()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero));
        var credentials = new InMemoryInstallationCredentialStore();
        var store = new InMemoryAccountLinkStore(credentials);
        var service = new AccountLinkApplicationService(
            store,
            new FakeLastFmAuthorizationClient(),
            time,
            new AccountLinkOptions(new Uri("https://xiv.fm"), TimeSpan.FromMinutes(10)));

        var started = await service.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(10));
        var state = await service.GetStateAsync(
            started.SessionId,
            started.LinkCredential,
            CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(AccountLinkStatus.Expired, state.Status);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.utcNow;

        public void Advance(TimeSpan duration) => this.utcNow = this.utcNow.Add(duration);
    }
}
