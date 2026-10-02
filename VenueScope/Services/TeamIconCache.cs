using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VenueScope.Services;

// Downloads pictures once, keeps them on disk, and plays GIF, APNG and
// animated WebP files by switching frames over time.
public sealed class TeamIconCache : IDisposable
{
    private const int MaxSide        = 2048;
    private const int MaxFrames      = 150;
    private const int AnimatedPixels = 24_000_000;

    private readonly ITextureProvider _textures;
    private readonly IPluginLog       _log;
    private readonly HttpClient       _http     = new();
    private readonly string           _cacheDir;
    private readonly SemaphoreSlim    _decode   = new(2);
    private int                       _disposed = 0;

    private enum EntryState { Queued, Ready, Failed }

    private sealed class CacheEntry
    {
        public EntryState            State  = EntryState.Queued;
        public IDalamudTextureWrap[] Frames = [];
        public int[]                 Ends   = [];
        public int                   Length;
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

    public IDalamudTextureWrap? GetOrQueue(string? url)
    {
        if (string.IsNullOrEmpty(url) || _disposed != 0) return null;

        var entry = _entries.GetOrAdd(url, key =>
        {
            var e = new CacheEntry();
            Task.Run(() => LoadAsync(key, e));
            return e;
        });

        if (entry.State != EntryState.Ready || entry.Frames.Length == 0) return null;
        if (entry.Frames.Length == 1) return entry.Frames[0];

        long t = Environment.TickCount64 % entry.Length;
        for (int i = 0; i < entry.Ends.Length; i++)
            if (t < entry.Ends[i]) return entry.Frames[i];
        return entry.Frames[^1];
    }

    public bool HasFailed(string? url) =>
        !string.IsNullOrEmpty(url) && _entries.TryGetValue(url, out var e) && e.State == EntryState.Failed;

    private async Task LoadAsync(string url, CacheEntry entry)
    {
        try
        {
            string path = Path.Combine(_cacheDir, StableHash(url).ToString("x8") + ".img");
            byte[] bytes;
            if (File.Exists(path))
            {
                bytes = await File.ReadAllBytesAsync(path);
            }
            else
            {
                bytes = await _http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(path, bytes);
            }

            await _decode.WaitAsync();
            try
            {
                if (_disposed != 0) return;
                Decode(url, bytes, entry);
            }
            finally
            {
                _decode.Release();
            }

            if (_disposed != 0)
            {
                foreach (var f in entry.Frames) f.Dispose();
                return;
            }
            entry.State = EntryState.Ready;
        }
        catch (Exception ex)
        {
            _log.Warning($"[TeamIconCache] Failed to load {url}: {ex.Message}");
            entry.State = EntryState.Failed;
        }
    }

    private void Decode(string url, byte[] bytes, CacheEntry entry)
    {
        using var image = Image.Load<Rgba32>(bytes);
        int count = Math.Min(image.Frames.Count, MaxFrames);

        double scale = Math.Min(1.0, (double)MaxSide / Math.Max(image.Width, image.Height));
        if (count > 1)
            scale = Math.Min(scale, Math.Sqrt((double)AnimatedPixels / ((long)image.Width * image.Height * count)));
        if (scale < 1.0)
            image.Mutate(x => x.Resize(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))));

        var spec   = RawImageSpecification.Rgba32(image.Width, image.Height);
        var frames = new IDalamudTextureWrap[count];
        var ends   = new int[count];
        var pixels = new byte[image.Width * image.Height * 4];
        int time   = 0;

        for (int i = 0; i < count; i++)
        {
            var frame = image.Frames[i];
            frame.CopyPixelDataTo(pixels);
            frames[i] = _textures.CreateFromRaw(spec, pixels, $"VenueScope {url} #{i}");
            time += FrameDelay(frame);
            ends[i] = time;
        }

        entry.Frames = frames;
        entry.Ends   = ends;
        entry.Length = Math.Max(1, time);
    }

    // Browsers treat very short delays as 100 ms, so do the same.
    private static int FrameDelay(ImageFrame<Rgba32> frame)
    {
        int ms = 0;
        var meta = frame.Metadata;
        if (meta.TryGetGifMetadata(out var gif))
            ms = gif.FrameDelay * 10;
        else if (meta.TryGetPngMetadata(out var png) && png.FrameDelay.Denominator != 0)
            ms = (int)(png.FrameDelay.Numerator * 1000.0 / png.FrameDelay.Denominator);
        else if (meta.TryGetWebpFrameMetadata(out var webp))
            ms = (int)webp.FrameDelay;
        return ms <= 10 ? 100 : ms;
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
        foreach (var e in _entries.Values)
            foreach (var f in e.Frames)
                f.Dispose();
        _entries.Clear();
    }
}
