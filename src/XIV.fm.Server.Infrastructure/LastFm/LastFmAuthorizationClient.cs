using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;

namespace XIV.fm.Server.Infrastructure.LastFm;

public sealed class LastFmAuthorizationClient : ILastFmAuthorizationClient
{
    private readonly HttpClient httpClient;
    private readonly LastFmAuthorizationOptions options;
    private readonly ILastFmRequestBudget requestBudget;

    public LastFmAuthorizationClient(
        HttpClient httpClient,
        LastFmAuthorizationOptions options,
        ILastFmRequestBudget requestBudget)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.requestBudget = requestBudget;
    }

    public async ValueTask<string> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var parameters = this.CreateSignedParameters("auth.getToken", null);
        using var document = await this.SendAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("token", out var tokenElement))
            throw CreateProviderError(document.RootElement, "Last.fm did not return an authorization token.");

        var token = tokenElement.GetString();
        return !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new LastFmAuthorizationException("Last.fm returned an empty authorization token.");
    }

    public Uri CreateAuthorizationUri(string providerToken, Uri callbackUri)
    {
        var apiKey = this.GetApiKey();
        return BuildUri(
            this.options.BrowserBaseUri,
            [
                new("api_key", apiKey),
                new("token", providerToken),
                new("cb", callbackUri.AbsoluteUri),
            ]);
    }

    public async ValueTask<LastFmAccountIdentity> CompleteAuthorizationAsync(
        string providerToken,
        CancellationToken cancellationToken)
    {
        var parameters = this.CreateSignedParameters("auth.getSession", providerToken);
        using var document = await this.SendAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("session", out var session) ||
            !session.TryGetProperty("name", out var nameElement) ||
            !session.TryGetProperty("key", out var keyElement) ||
            string.IsNullOrWhiteSpace(keyElement.GetString()))
        {
            throw CreateProviderError(document.RootElement, "Last.fm did not return a valid account session.");
        }

        var name = nameElement.GetString();
        return !string.IsNullOrWhiteSpace(name)
            ? new LastFmAccountIdentity(name)
            : throw new LastFmAuthorizationException("Last.fm returned an empty account name.");
    }

    private static LastFmAuthorizationException CreateProviderError(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("message", out var message) && !string.IsNullOrWhiteSpace(message.GetString()))
            return new LastFmAuthorizationException($"Last.fm rejected authorization: {message.GetString()}");

        return new LastFmAuthorizationException(fallback);
    }

    private async ValueTask<JsonDocument> SendAsync(
        IReadOnlyCollection<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(this.options.ApiBaseUri, parameters.Append(new("format", "json")));
        try
        {
            await this.requestBudget.AcquireAsync(cancellationToken).ConfigureAwait(false);
            using var response = await this.httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new LastFmAuthorizationException(
                    $"Last.fm authorization returned HTTP {(int)response.StatusCode}.");
            }

            await response.Content.LoadIntoBufferAsync(64 * 1024, cancellationToken).ConfigureAwait(false);
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(
                content,
                new JsonDocumentOptions { MaxDepth = 16 },
                cancellationToken).ConfigureAwait(false);
        }
        catch (LastFmAuthorizationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            throw new LastFmAuthorizationException("Last.fm authorization is temporarily unavailable.", exception);
        }
    }

    private SortedDictionary<string, string> CreateSignedParameters(
        string method,
        string? providerToken)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = this.GetApiKey(),
            ["method"] = method,
        };
        if (providerToken is not null)
            values["token"] = providerToken;

        var signatureInput = new StringBuilder();
        foreach (var pair in values)
            signatureInput.Append(pair.Key).Append(pair.Value);
        signatureInput.Append(this.GetSharedSecret());
#pragma warning disable CA5351 // MD5 is mandated by the Last.fm API signature protocol, not used for credential storage.
        var signature = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(signatureInput.ToString()))).ToLowerInvariant();
#pragma warning restore CA5351
        values["api_sig"] = signature;
        return values;
    }

    private string GetApiKey() => !string.IsNullOrWhiteSpace(this.options.ApiKey)
        ? this.options.ApiKey
        : throw new LastFmAuthorizationException("Last.fm account linking is not configured.");

    private string GetSharedSecret() => !string.IsNullOrWhiteSpace(this.options.SharedSecret)
        ? this.options.SharedSecret
        : throw new LastFmAuthorizationException("Last.fm account linking is not configured.");

    private static Uri BuildUri(Uri baseUri, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var query = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var builder = new UriBuilder(baseUri) { Query = query };
        return builder.Uri;
    }
}
