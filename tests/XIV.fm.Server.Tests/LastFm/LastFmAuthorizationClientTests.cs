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

        const string providerToken = "provider-token-00000000000000000";
        var identity = await client.CompleteAuthorizationAsync(providerToken, CancellationToken.None);

        Assert.Equal("CanonicalListener", identity.CanonicalName);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("api_sig=93382cada7100d2e60011dfba834e2e1", request.Query, StringComparison.Ordinal);
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

        var result = client.CreateAuthorizationUri(callback);

        Assert.Contains("api_key=key", result.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", result.Query, StringComparison.Ordinal);
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
            const string content = "{\"session\":{\"name\":\"CanonicalListener\",\"key\":\"discard-this-session-key\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
