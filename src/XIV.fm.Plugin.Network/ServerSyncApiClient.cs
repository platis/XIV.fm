using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using XIV.fm.Contracts.V1;

namespace XIV.fm.Plugin.Network;

public sealed class ServerSyncApiClient : IServerSyncApiClient, IAccountLinkApiClient, IInstallationApiClient, IRelayApiClient, IDisposable
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

    public async Task RevokeCurrentInstallationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(serverBaseUri, ApiRoutes.RevokeCurrentInstallation));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationCredential);
        message.Headers.UserAgent.ParseAdd($"XIV.fm/{pluginVersion}");

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
                error?.Title ?? "The XIV.fm server rejected the disconnect request.");
        }
    }

    public Task<RelayListResponse> ListRelaysAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<RelayListResponse>(
            HttpMethod.Get,
            serverBaseUri,
            ApiRoutes.Relays,
            installationCredential,
            pluginVersion,
            null,
            cancellationToken);

    public Task<RelayResponse> CreateRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        CreateRelayRequest request,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<RelayResponse>(
            HttpMethod.Post,
            serverBaseUri,
            ApiRoutes.Relays,
            installationCredential,
            pluginVersion,
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);

    public Task<RelayResponse> RenameRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        RenameRelayRequest request,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<RelayResponse>(
            HttpMethod.Patch,
            serverBaseUri,
            ApiRoutes.GetRelay(relayId),
            installationCredential,
            pluginVersion,
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);

    public Task DeleteRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken) =>
        this.SendRelayNoContentAsync(
            HttpMethod.Delete,
            serverBaseUri,
            ApiRoutes.GetRelay(relayId),
            installationCredential,
            pluginVersion,
            cancellationToken);

    public Task<RelayMemberListResponse> ListRelayMembersAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<RelayMemberListResponse>(
            HttpMethod.Get,
            serverBaseUri,
            ApiRoutes.GetRelayMembers(relayId),
            installationCredential,
            pluginVersion,
            null,
            cancellationToken);

    public Task KickRelayMemberAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        Guid membershipId,
        CancellationToken cancellationToken) =>
        this.SendRelayNoContentAsync(
            HttpMethod.Delete,
            serverBaseUri,
            ApiRoutes.GetRelayMember(relayId, membershipId),
            installationCredential,
            pluginVersion,
            cancellationToken);

    public Task LeaveRelayAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken) =>
        this.SendRelayNoContentAsync(
            HttpMethod.Delete,
            serverBaseUri,
            ApiRoutes.GetRelayMembership(relayId),
            installationCredential,
            pluginVersion,
            cancellationToken);

    public Task<CreatedRelayInvitationResponse> CreateRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CreateRelayInvitationRequest request,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<CreatedRelayInvitationResponse>(
            HttpMethod.Post,
            serverBaseUri,
            ApiRoutes.GetRelayInvitations(relayId),
            installationCredential,
            pluginVersion,
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);

    public Task<RelayInvitationListResponse> ListRelayInvitationsAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<RelayInvitationListResponse>(
            HttpMethod.Get,
            serverBaseUri,
            ApiRoutes.GetRelayInvitations(relayId),
            installationCredential,
            pluginVersion,
            null,
            cancellationToken);

    public Task RevokeRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        Guid relayId,
        Guid invitationId,
        CancellationToken cancellationToken) =>
        this.SendRelayNoContentAsync(
            HttpMethod.Delete,
            serverBaseUri,
            ApiRoutes.GetRelayInvitation(relayId, invitationId),
            installationCredential,
            pluginVersion,
            cancellationToken);

    public Task<RelayInvitationPreviewResponse> PreviewRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        RelayInvitationTokenRequest request,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<RelayInvitationPreviewResponse>(
            HttpMethod.Post,
            serverBaseUri,
            ApiRoutes.RelayInvitationPreview,
            installationCredential,
            pluginVersion,
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);

    public Task<AcceptRelayInvitationResponse> AcceptRelayInvitationAsync(
        Uri serverBaseUri,
        string installationCredential,
        string pluginVersion,
        RelayInvitationTokenRequest request,
        CancellationToken cancellationToken) =>
        this.SendRelayAsync<AcceptRelayInvitationResponse>(
            HttpMethod.Post,
            serverBaseUri,
            ApiRoutes.RelayInvitationAccept,
            installationCredential,
            pluginVersion,
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);

    public void Dispose()
    {
        if (this.ownsHttpClient)
            this.httpClient.Dispose();
    }

    private async Task<T> SendRelayAsync<T>(
        HttpMethod method,
        Uri serverBaseUri,
        string route,
        string installationCredential,
        string pluginVersion,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthenticatedMessage(
            method,
            new Uri(serverBaseUri, route),
            installationCredential,
            pluginVersion,
            content);
        return await this.SendAsync<T>(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRelayNoContentAsync(
        HttpMethod method,
        Uri serverBaseUri,
        string route,
        string installationCredential,
        string pluginVersion,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthenticatedMessage(
            method,
            new Uri(serverBaseUri, route),
            installationCredential,
            pluginVersion,
            null);
        using var response = await this.httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new ServerSyncException("response_too_large", "The XIV.fm server response was too large.");

        var bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        var error = TryDeserialize<ApiError>(bytes);
        throw new ServerSyncException(
            error?.Code ?? $"http_{(int)response.StatusCode}",
            error?.Title ?? "The XIV.fm server rejected the request.");
    }

    private static HttpRequestMessage CreateAuthenticatedMessage(
        HttpMethod method,
        Uri endpoint,
        string installationCredential,
        string pluginVersion,
        HttpContent? content)
    {
        var message = new HttpRequestMessage(method, endpoint)
        {
            Content = content,
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationCredential);
        message.Headers.UserAgent.ParseAdd($"XIV.fm/{pluginVersion}");
        return message;
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
