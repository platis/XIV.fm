using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using XIV.fm.Contracts.V1;
using XIV.fm.Server.Api.Http;

namespace XIV.fm.Server.Tests.Api;

public sealed class TrustedProxiesTests
{
    private const string ProxyNetwork = "172.18.0.0/16";
    private static readonly IPAddress Proxy = IPAddress.Parse("172.18.0.4");
    private static readonly IPAddress Stranger = IPAddress.Parse("203.0.113.9");

    [Fact]
    public async Task ClientsBehindTheTrustedProxyHaveSeparateLinkLimits()
    {
        await using var factory = CreateFactory(ProxyNetwork);

        for (var request = 0; request < 5; request++)
            Assert.Equal(StatusCodes.Status201Created, await StartLinkAsync(factory, Proxy, "198.51.100.1"));
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            await StartLinkAsync(factory, Proxy, "198.51.100.1"));

        Assert.Equal(StatusCodes.Status201Created, await StartLinkAsync(factory, Proxy, "198.51.100.2"));
    }

    [Fact]
    public async Task OnlyTheAddressAddedByTheProxyIsUsed()
    {
        await using var factory = CreateFactory(ProxyNetwork);

        for (var request = 0; request < 5; request++)
        {
            Assert.Equal(
                StatusCodes.Status201Created,
                await StartLinkAsync(factory, Proxy, $"192.0.2.{request}, 198.51.100.1"));
        }

        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            await StartLinkAsync(factory, Proxy, "192.0.2.200, 198.51.100.1"));
    }

    [Theory]
    [InlineData(ProxyNetwork)]
    [InlineData(null)]
    public async Task UntrustedConnectionsCannotChooseTheirAddress(string? networks)
    {
        await using var factory = CreateFactory(networks);

        for (var request = 0; request < 5; request++)
        {
            Assert.Equal(
                StatusCodes.Status201Created,
                await StartLinkAsync(factory, Stranger, $"198.51.100.{request}"));
        }

        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            await StartLinkAsync(factory, Stranger, "198.51.100.200"));
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("not-a-network")]
    public void UnsafeConfigurationIsRejected(string networks)
    {
        Assert.Throws<InvalidOperationException>(() => TrustedProxies.CreateOptions(networks));
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(string? networks) =>
        new ServerApiFactory().WithWebHostBuilder(builder =>
            builder.UseSetting(TrustedProxies.ConfigurationKey, networks));

    private static async Task<int> StartLinkAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        IPAddress connection,
        string forwardedFor)
    {
        var context = await factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = ApiRoutes.StartAccountLink;
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            context.Connection.RemoteIpAddress = connection;
        });
        return context.Response.StatusCode;
    }
}
