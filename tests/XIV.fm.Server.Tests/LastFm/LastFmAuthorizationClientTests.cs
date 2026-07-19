using System.Net;
using System.Text;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Infrastructure.LastFm;

namespace XIV.fm.Server.Tests.LastFm;

public sealed class LastFmAuthorizationClientTests
{
    [Fact]
    public async Task SignsProviderCallsAndReturnsOnlyCanonicalIdentity()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new LastFmAuthorizationClient(
            httpClient,
            new LastFmAuthorizationOptions(
                "key",
                "shared",
                LastFmAuthorizationOptions.DefaultApiBaseUri,
                LastFmAuthorizationOptions.DefaultBrowserBaseUri),
            new ImmediateBudget());

        var providerToken = await client.RequestTokenAsync(CancellationToken.None);
        var identity = await client.CompleteAuthorizationAsync(providerToken, CancellationToken.None);

        Assert.Equal("provider-token-00000000000000000", providerToken);
        Assert.Equal("CanonicalListener", identity.CanonicalName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("api_sig=37ee171c86dcb38eadd5c8f8d9668df6", handler.Requests[0].Query, StringComparison.Ordinal);
        Assert.Contains("api_sig=93382cada7100d2e60011dfba834e2e1", handler.Requests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizationUriCarriesTheExplicitCallback()
    {
        using var httpClient = new HttpClient(new RecordingHandler());
        var client = new LastFmAuthorizationClient(
            httpClient,
            new LastFmAuthorizationOptions(
                "key",
                "shared",
                LastFmAuthorizationOptions.DefaultApiBaseUri,
                LastFmAuthorizationOptions.DefaultBrowserBaseUri),
            new ImmediateBudget());
        var callback = new Uri("https://xiv.fm/v1/account-links/example/callback?state=secret");

        var result = client.CreateAuthorizationUri("provider-token", callback);

        Assert.Contains("api_key=key", result.Query, StringComparison.Ordinal);
        Assert.Contains("token=provider-token", result.Query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(callback.AbsoluteUri), result.Query, StringComparison.Ordinal);
    }

    private sealed class ImmediateBudget : ILastFmRequestBudget
    {
        public ValueTask AcquireAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = Assert.IsType<Uri>(request.RequestUri);
            this.Requests.Add(uri);
            var content = uri.Query.Contains("auth.getToken", StringComparison.Ordinal)
                ? "{\"token\":\"provider-token-00000000000000000\"}"
                : "{\"session\":{\"name\":\"CanonicalListener\",\"key\":\"discard-this-session-key\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
