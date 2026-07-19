using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Authentication;

namespace XIV.fm.Server.Tests;

public sealed class ServerApiFactory : WebApplicationFactory<Program>
{
    public const string Credential = "test-installation-credential-0000000000000001";

    public static InstallationId InstallationId { get; } =
        new(Guid.Parse("f9aa8d75-50a7-4aeb-b4c5-5fb6b70132f3"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<InMemoryInstallationCredentialStore>();
            services.RemoveAll<IInstallationCredentialStore>();

            var credentialStore = new InMemoryInstallationCredentialStore();
            credentialStore.RegisterAsync(InstallationId, Credential, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            services.AddSingleton(credentialStore);
            services.AddSingleton<IInstallationCredentialStore>(credentialStore);
        });
    }
}
