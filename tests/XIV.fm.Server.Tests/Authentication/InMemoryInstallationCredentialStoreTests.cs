using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Authentication;

namespace XIV.fm.Server.Tests.Authentication;

public sealed class InMemoryInstallationCredentialStoreTests
{
    [Fact]
    public async Task ProvisionReturnsAuthenticatingOpaqueCredential()
    {
        var store = new InMemoryInstallationCredentialStore();

        var issued = await store.ProvisionAsync(CancellationToken.None);

        Assert.True(issued.Credential.Length >= 32);
        Assert.Equal(
            issued.InstallationId,
            await store.AuthenticateAsync(issued.Credential, CancellationToken.None));
    }

    [Fact]
    public async Task RotateInvalidatesOldCredentialAndAcceptsNewCredential()
    {
        var store = new InMemoryInstallationCredentialStore();
        var installationId = new InstallationId(Guid.NewGuid());
        const string oldCredential = "old-test-credential-00000000000000000001";
        const string newCredential = "new-test-credential-00000000000000000001";
        await store.RegisterAsync(installationId, oldCredential, CancellationToken.None);

        await store.RotateAsync(installationId, newCredential, CancellationToken.None);

        Assert.Null(await store.AuthenticateAsync(oldCredential, CancellationToken.None));
        Assert.Equal(installationId, await store.AuthenticateAsync(newCredential, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeInvalidatesCredential()
    {
        var store = new InMemoryInstallationCredentialStore();
        var installationId = new InstallationId(Guid.NewGuid());
        const string credential = "revoked-test-credential-00000000000000001";
        await store.RegisterAsync(installationId, credential, CancellationToken.None);

        await store.RevokeAsync(installationId, CancellationToken.None);

        Assert.Null(await store.AuthenticateAsync(credential, CancellationToken.None));
    }

    [Fact]
    public void GeneratorCreatesOpaqueHighEntropyCredential()
    {
        var first = InstallationCredentialGenerator.Generate();
        var second = InstallationCredentialGenerator.Generate();

        Assert.True(first.Length >= 32);
        Assert.DoesNotContain("=", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }
}
