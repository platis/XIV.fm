using System.Text;
using System.Text.Json;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Domain.Accounts;
using XIV.fm.Server.Domain.Listening;

namespace XIV.fm.Server.Infrastructure.LastFm;

public sealed class LastFmRecentTracksClient : ILastFmRecentTracksClient
{
    private readonly HttpClient httpClient;
    private readonly LastFmAuthorizationOptions options;
    private readonly ILastFmRequestBudget requestBudget;
    private readonly TimeProvider timeProvider;

    public LastFmRecentTracksClient(
        HttpClient httpClient,
        LastFmAuthorizationOptions options,
        ILastFmRequestBudget requestBudget,
        TimeProvider timeProvider)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.requestBudget = requestBudget;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<ListeningObservation> GetCurrentAsync(
        LastFmAccountIdentity identity,
        CancellationToken cancellationToken)
    {
        var apiKey = !string.IsNullOrWhiteSpace(this.options.ApiKey)
            ? this.options.ApiKey
            : throw new LastFmRecentTracksException("Last.fm listening is not configured.");
        var uri = BuildUri(
            this.options.ApiBaseUri,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["method"] = "user.getRecentTracks",
                ["user"] = identity.CanonicalName,
                ["api_key"] = apiKey,
                ["limit"] = "1",
                ["format"] = "json",
            });

        try
        {
            await this.requestBudget.AcquireAsync(cancellationToken).ConfigureAwait(false);
            using var response = await this.httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new LastFmRecentTracksException(
                    $"Last.fm recent tracks returned HTTP {(int)response.StatusCode}.");
            }

            await response.Content.LoadIntoBufferAsync(64 * 1024, cancellationToken).ConfigureAwait(false);
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                content,
                new JsonDocumentOptions { MaxDepth = 24 },
                cancellationToken).ConfigureAwait(false);
            return Map(document.RootElement, this.timeProvider.GetUtcNow());
        }
        catch (LastFmRecentTracksException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or IOException or LastFmAuthorizationException)
        {
            throw new LastFmRecentTracksException("Last.fm recent tracks are temporarily unavailable.", exception);
        }
    }

    private static ListeningObservation Map(JsonElement root, DateTimeOffset observedAt)
    {
        if (root.TryGetProperty("error", out _))
            throw CreateProviderError(root);
        if (!root.TryGetProperty("recenttracks", out var recentTracks) ||
            !recentTracks.TryGetProperty("track", out var tracks))
        {
            throw new LastFmRecentTracksException("Last.fm returned an invalid recent-tracks response.");
        }

        var track = tracks.ValueKind switch
        {
            JsonValueKind.Array when tracks.GetArrayLength() > 0 => tracks[0],
            JsonValueKind.Object => tracks,
            _ => default,
        };
        if (track.ValueKind != JsonValueKind.Object || !IsNowPlaying(track))
        {
            return new ListeningObservation(
                ListeningObservationStatus.NotPlaying,
                observedAt,
                null);
        }

        var title = GetBoundedText(track, "name", required: true);
        var artist = GetBoundedText(track, "artist", required: true);
        var album = GetBoundedText(track, "album", required: false);
        var trackUrl = GetSafeUri(GetBoundedText(track, "url", required: false));
        return new ListeningObservation(
            ListeningObservationStatus.Playing,
            observedAt,
            new NormalizedTrack(title!, artist!, album, null, trackUrl, null));
    }

    private static bool IsNowPlaying(JsonElement track) =>
        track.TryGetProperty("@attr", out var attributes) &&
        attributes.ValueKind == JsonValueKind.Object &&
        attributes.TryGetProperty("nowplaying", out var nowPlaying) &&
        string.Equals(nowPlaying.GetString(), "true", StringComparison.OrdinalIgnoreCase);

    private static string? GetBoundedText(JsonElement owner, string propertyName, bool required)
    {
        if (!owner.TryGetProperty(propertyName, out var property))
        {
            if (required)
                throw new LastFmRecentTracksException($"Last.fm omitted required track field '{propertyName}'.");
            return null;
        }

        var value = GetTextValue(property)?.Normalize(NormalizationForm.FormKC).Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (required)
                throw new LastFmRecentTracksException($"Last.fm returned an empty track field '{propertyName}'.");
            return null;
        }
        if (value.Length > 512 || value.Any(char.IsControl))
            throw new LastFmRecentTracksException($"Last.fm returned an invalid track field '{propertyName}'.");
        return value;
    }

    private static string? GetTextValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Object when element.TryGetProperty("#text", out var text) => text.GetString(),
        _ => null,
    };

    private static Uri? GetSafeUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return uri;
    }

    private static LastFmRecentTracksException CreateProviderError(JsonElement root)
    {
        var message = root.TryGetProperty("message", out var value) ? value.GetString() : null;
        return new LastFmRecentTracksException(
            string.IsNullOrWhiteSpace(message)
                ? "Last.fm rejected the recent-tracks request."
                : $"Last.fm rejected the recent-tracks request: {message}");
    }

    private static Uri BuildUri(Uri baseUri, IReadOnlyDictionary<string, string> parameters)
    {
        var query = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new UriBuilder(baseUri) { Query = query }.Uri;
    }
}
