using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Network;

public sealed class ServerSyncApiClient : IServerSyncApiClient, IAccountLinkApiClient, IDisposable
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public ServerSyncApiClient()
        : this(
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
            },
            ownsHttpClient: true)
    {
    }

    public ServerSyncApiClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private ServerSyncApiClient(HttpClient httpClient, bool ownsHttpClient)
    {
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<ServerSyncApiResult> SyncAsync(
        Uri serverBaseUri,
        string installationCredential,
        SyncRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(serverBaseUri, ApiRoutes.Sync);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationCredential);
        message.Headers.UserAgent.ParseAdd($"XIV.fm/{request.PluginVersion}");

        using var response = await this.httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new ServerSyncException("response_too_large", "The XIV.fm server response was too large.");

        var bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<ApiError>(bytes);
            throw new ServerSyncException(
                error?.Code ?? $"http_{(int)response.StatusCode}",
                error?.Title ?? "The XIV.fm server rejected the sync request.");
        }

        var syncResponse = TryDeserialize<SyncResponse>(bytes)
            ?? throw new ServerSyncException("invalid_response", "The XIV.fm server returned an invalid response.");
        var requestId = response.Headers.TryGetValues("X-Request-ID", out var values)
            ? values.FirstOrDefault()
            : null;
        return new ServerSyncApiResult(syncResponse, requestId);
    }

    public async Task<StartAccountLinkResponse> StartAccountLinkAsync(
        Uri serverBaseUri,
        string pluginVersion,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(serverBaseUri, ApiRoutes.StartAccountLink));
        message.Headers.UserAgent.ParseAdd($"XIV.fm/{pluginVersion}");
        return await this.SendAsync<StartAccountLinkResponse>(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountLinkStatusResponse> GetAccountLinkStatusAsync(
        Uri serverBaseUri,
        Guid linkSessionId,
        string linkCredential,
        string pluginVersion,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(serverBaseUri, ApiRoutes.GetAccountLinkStatus(linkSessionId)))
        {
            Content = JsonContent.Create(new AccountLinkStatusRequest(linkCredential), options: JsonOptions),
        };
        message.Headers.UserAgent.ParseAdd($"XIV.fm/{pluginVersion}");
        return await this.SendAsync<AccountLinkStatusResponse>(message, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (this.ownsHttpClient)
            this.httpClient.Dispose();
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        using var response = await this.httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new ServerSyncException("response_too_large", "The XIV.fm server response was too large.");

        var bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<ApiError>(bytes);
            throw new ServerSyncException(
                error?.Code ?? $"http_{(int)response.StatusCode}",
                error?.Title ?? "The XIV.fm server rejected the request.");
        }

        return TryDeserialize<T>(bytes)
            ?? throw new ServerSyncException("invalid_response", "The XIV.fm server returned an invalid response.");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return output.ToArray();
            if (output.Length + read > MaximumResponseBytes)
                throw new ServerSyncException("response_too_large", "The XIV.fm server response was too large.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static T? TryDeserialize<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
