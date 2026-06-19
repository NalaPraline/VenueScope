using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using VenueScope.Models;

namespace VenueScope.Services;

public class SpotlightService : IDisposable
{
    private readonly HttpClient _http;
    private readonly IPluginLog _log;
    private readonly string     _apiUrl;

    public List<SpotlightVenue> Venues { get; private set; } = new();

    public SpotlightService(IPluginLog log, string apiUrl)
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
            var entries = JsonConvert.DeserializeObject<List<SpotlightVenue>>(json);
            if (entries == null) return;

            Venues = entries
                .OrderByDescending(v => v.Priority)
                .ThenBy(v => v.Name)
                .ToList();

            _log.Debug($"[Spotlight] Loaded {Venues.Count} venues.");
        }
        catch (Exception ex)
        {
            _log.Warning($"[Spotlight] Could not fetch: {ex.Message}");
        }
    }

    public void Dispose() => _http.Dispose();
}
