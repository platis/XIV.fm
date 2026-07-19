using Microsoft.Extensions.Diagnostics.HealthChecks;
using XIV.fm.Server.Infrastructure.Authentication;
using XIV.fm.Server.Infrastructure.Presence;

namespace XIV.fm.Server.Api.Health;

public sealed class InMemoryStoresHealthCheck : IHealthCheck
{
    private readonly InMemoryInstallationCredentialStore credentialStore;
    private readonly InMemoryPresenceStore presenceStore;

    public InMemoryStoresHealthCheck(
        InMemoryInstallationCredentialStore credentialStore,
        InMemoryPresenceStore presenceStore)
    {
        this.credentialStore = credentialStore;
        this.presenceStore = presenceStore;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = this.credentialStore;
        _ = this.presenceStore;
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
