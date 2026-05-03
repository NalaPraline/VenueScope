using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using VenueScope.Models;

namespace VenueScope.Services;

public class SynchellService : IDisposable
{
    private readonly HttpClient _http;
    private readonly IPluginLog _log;
    private readonly string     _apiUrl;

    private Dictionary<(string server, string district, int ward, int plot), SynchellEntry> _index = new();

    private static readonly Regex WardRx     = new(@"\bW(?:ard\s+)?(\d+)\b",                          RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlotRx     = new(@"\bP(?:lot\s+)?(\d+)\b",                          RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DistrictRx = new(@"\b(Mist|Lavender Beds|The Goblet|Shirogane|Empyreum)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SynchellService(IPluginLog log, string apiUrl)
    {
        _log    = log;
        _apiUrl = apiUrl;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("User-Agent", "Dalamud-VenueScope/1.0");
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiUrl)) return;
        try
        {
            var json    = await _http.GetStringAsync(_apiUrl);
            var entries = JsonConvert.DeserializeObject<List<SynchellEntry>>(json);
            if (entries == null) return;

            var index = new Dictionary<(string, string, int, int), SynchellEntry>();
            foreach (var e in entries)
                foreach (var loc in e.Locations)
                    index[(loc.Server.ToLowerInvariant(), loc.District.ToLowerInvariant(), loc.Ward, loc.Plot)] = e;

            _index = index;
            _log.Debug($"[Synchell] Loaded {entries.Count} entries.");
        }
        catch (Exception ex)
        {
            _log.Warning($"[Synchell] Could not fetch: {ex.Message}");
        }
    }

    public SynchellEntry? FindForEvent(string server, string lifestreamCode)
    {
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(lifestreamCode)) return null;

        var wm = WardRx.Match(lifestreamCode);
        var pm = PlotRx.Match(lifestreamCode);
        if (!wm.Success || !pm.Success) return null;

        int ward = int.Parse(wm.Groups[1].Value);
        int plot = int.Parse(pm.Groups[1].Value);

        var dm       = DistrictRx.Match(lifestreamCode);
        string district = dm.Success ? dm.Value.ToLowerInvariant() : string.Empty;

        if (!string.IsNullOrEmpty(district) &&
            _index.TryGetValue((server.ToLowerInvariant(), district, ward, plot), out var entry))
            return entry;

        return null;
    }

    private static readonly string[] Districts = ["mist", "lavender beds", "the goblet", "shirogane", "empyreum"];

    public SynchellEntry? FindByHousing(string server, int ward, int plot)
    {
        string serverLower = server.ToLowerInvariant();
        foreach (var district in Districts)
            if (_index.TryGetValue((serverLower, district, ward, plot), out var entry))
                return entry;
        return null;
    }

    public static bool IsHousingLocation(string lifestreamCode) =>
        !string.IsNullOrEmpty(lifestreamCode)
        && WardRx.IsMatch(lifestreamCode)
        && PlotRx.IsMatch(lifestreamCode);

    public void Dispose() => _http.Dispose();
}
