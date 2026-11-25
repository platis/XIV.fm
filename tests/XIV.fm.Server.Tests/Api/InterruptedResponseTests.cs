using System.Net;
using XIV.fm.Plugin.Network;

namespace XIV.fm.Server.Tests.Api;

public sealed class InterruptedResponseTests
{
    [Fact]
    public async Task InterruptedResponseBodyIsAConnectionFailure()
    {
        using var http = new HttpClient(new InterruptedHandler());
        using var client = new ServerSyncApiClient(http);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.StartAccountLinkAsync(
            new Uri("https://example.invalid"),
            "0.1.35.0",
            CancellationToken.None));

        Assert.IsAssignableFrom<IOException>(exception.InnerException);
    }

    private sealed class InterruptedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InterruptedStream()),
            });
    }

    private sealed class InterruptedStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded);
    }
}
