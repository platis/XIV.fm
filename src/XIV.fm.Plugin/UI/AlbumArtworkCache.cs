using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using XIV.fm.Plugin.Core.Overlay;

namespace XIV.fm.Plugin.UI;

/// <summary>
/// Bounded asynchronous artwork loading. Rendering only reads completed textures and never starts network work.
/// </summary>
public sealed class AlbumArtworkCache : IDisposable
{
    private const int MaximumArtworkBytes = 2 * 1024 * 1024;
    private const int MaximumEntries = 64;
    private const int MaximumPreparedPerSnapshot = 16;

    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/gif",
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    private readonly ITextureProvider textureProvider;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim downloadSlots = new(2, 2);
    private readonly ConcurrentDictionary<Uri, CacheEntry> entries = new();
    private readonly Lock lifecycleGate = new();
    private CancellationTokenSource activeCancellation = new();
    private bool disposed;

    public AlbumArtworkCache(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
        this.httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        this.httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("XIV.fm", "0.1"));
    }

    public void Prepare(IEnumerable<Uri> artworkUris)
    {
        CancellationToken cancellationToken;
        lock (this.lifecycleGate)
        {
            if (this.disposed)
                return;
            cancellationToken = this.activeCancellation.Token;
        }

        foreach (var uri in artworkUris
                     .Where(ArtworkUriPolicy.IsAllowed)
                     .Distinct()
                     .Take(MaximumPreparedPerSnapshot))
        {
            if (this.entries.Count >= MaximumEntries && !this.entries.ContainsKey(uri))
                break;

            var entry = this.entries.GetOrAdd(uri, static _ => new CacheEntry());
            entry.EnsureLoaded(
                token => this.LoadAsync(uri, token),
                cancellationToken);
        }
    }

    public bool TryGet(Uri? artworkUri, out IDalamudTextureWrap? texture)
    {
        texture = null;
        return ArtworkUriPolicy.IsAllowed(artworkUri) &&
               this.entries.TryGetValue(artworkUri!, out var entry) &&
               entry.TryGet(out texture);
    }

    public void Suspend()
    {
        lock (this.lifecycleGate)
        {
            if (this.disposed)
                return;

            this.activeCancellation.Cancel();
            this.activeCancellation.Dispose();
            this.activeCancellation = new CancellationTokenSource();
        }

        foreach (var pair in this.entries)
        {
            if (this.entries.TryRemove(pair.Key, out var entry))
                entry.Dispose();
        }
    }

    public void Dispose()
    {
        lock (this.lifecycleGate)
        {
            if (this.disposed)
                return;

            this.disposed = true;
            this.activeCancellation.Cancel();
            this.activeCancellation.Dispose();
        }

        foreach (var entry in this.entries.Values)
            entry.Dispose();
        this.entries.Clear();
        this.httpClient.Dispose();
    }

    private async Task<IDalamudTextureWrap> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        await this.downloadSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await this.httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri is not Uri finalUri ||
                !ArtworkUriPolicy.IsAllowed(finalUri) ||
                response.Content.Headers.ContentType?.MediaType is not string mediaType ||
                !AllowedMediaTypes.Contains(mediaType) ||
                response.Content.Headers.ContentLength > MaximumArtworkBytes)
            {
                throw new HttpRequestException("The artwork response was not an approved bounded image.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var destination = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (destination.Length + read > MaximumArtworkBytes)
                    throw new HttpRequestException("The artwork response exceeded the size limit.");
                destination.Write(buffer, 0, read);
            }

            if (destination.Length == 0)
                throw new HttpRequestException("The artwork response was empty.");

            return await this.textureProvider.CreateFromImageAsync(
                destination.ToArray(),
                "XIV.fm album artwork",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.downloadSlots.Release();
        }
    }

    private sealed class CacheEntry : IDisposable
    {
        private readonly Lock gate = new();
        private IDalamudTextureWrap? texture;
        private Task? loadTask;
        private bool disposed;

        public void EnsureLoaded(
            Func<CancellationToken, Task<IDalamudTextureWrap>> loader,
            CancellationToken cancellationToken)
        {
            lock (this.gate)
            {
                if (this.disposed || this.texture is not null || this.loadTask is not null)
                    return;

                this.loadTask = this.LoadAsync(loader, cancellationToken);
            }
        }

        public bool TryGet(out IDalamudTextureWrap? value)
        {
            value = Volatile.Read(ref this.texture);
            return value is not null;
        }

        public void Dispose()
        {
            lock (this.gate)
            {
                this.disposed = true;
                Interlocked.Exchange(ref this.texture, null)?.Dispose();
            }
        }

        private async Task LoadAsync(
            Func<CancellationToken, Task<IDalamudTextureWrap>> loader,
            CancellationToken cancellationToken)
        {
            IDalamudTextureWrap? loaded = null;
            try
            {
                loaded = await loader(cancellationToken).ConfigureAwait(false);
                lock (this.gate)
                {
                    if (this.disposed || cancellationToken.IsCancellationRequested)
                        return;

                    this.texture = loaded;
                    loaded = null;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The card retains its local placeholder when artwork is unavailable.
            }
            finally
            {
                loaded?.Dispose();
            }
        }
    }
}
