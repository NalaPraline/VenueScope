using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace VenueScope.Services;

/// <summary>
/// Downloads team icon images from Partake.gg CDN asynchronously,
/// caches them on disk, and exposes Dalamud texture wraps for ImGui rendering.
/// </summary>
public sealed class TeamIconCache : IDisposable
{
    private readonly ITextureProvider _textures;
    private readonly IPluginLog       _log;
    private readonly HttpClient       _http     = new();
    private readonly string           _cacheDir;
    private int                       _disposed = 0;

    private enum EntryState { Queued, Ready, Failed }

    private sealed class CacheEntry
    {
        public EntryState              State = EntryState.Queued;
        public string?                 Path;
        public ISharedImmediateTexture? Tex;
    }

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public TeamIconCache(ITextureProvider textures, IPluginLog log)
    {
        _textures = textures;
        _log      = log;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Dalamud-VenueScope/1.0");
        _cacheDir = Path.Combine(Path.GetTempPath(), "VenueScope", "icons");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Returns an <see cref="IDalamudTextureWrap"/> if the image is ready,
    /// otherwise queues a background download and returns null.
    /// Safe to call every frame.
    /// </summary>
    public IDalamudTextureWrap? GetOrQueue(string? url)
    {
        if (string.IsNullOrEmpty(url) || _disposed != 0) return null;

        var entry = _entries.GetOrAdd(url, key =>
        {
            var e = new CacheEntry();
            Task.Run(() => DownloadAsync(key, e));
            return e;
        });

        if (entry.State == EntryState.Ready && entry.Tex != null)
        {
            entry.Tex.TryGetWrap(out var wrap, out _);
            return wrap;
        }

        return null;
    }

    private async Task DownloadAsync(string url, CacheEntry entry)
    {
        try
        {
            string ext  = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            string hash = StableHash(url).ToString("x8");
            string path = Path.Combine(_cacheDir, hash + ext);

            if (!File.Exists(path))
            {
                var bytes = await _http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(path, bytes);
            }

            if (_disposed != 0) return;

            entry.Path  = path;
            entry.Tex   = _textures.GetFromFile(path);
            entry.State = EntryState.Ready;
        }
        catch (Exception ex)
        {
            _log.Warning($"[TeamIconCache] Failed to load {url}: {ex.Message}");
            entry.State = EntryState.Failed;
        }
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return Math.Abs(h);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _http.Dispose();
        // ISharedImmediateTexture is managed by Dalamud — no manual dispose needed
        _entries.Clear();
    }
}
