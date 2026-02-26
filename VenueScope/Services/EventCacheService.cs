using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using VenueScope.Models;

namespace VenueScope.Services;

/// <summary>
/// Combines events from Partake and FFXIVenue, caches them,
/// and auto-refreshes on a configurable interval.
/// Thread-safe via SemaphoreSlim.
/// </summary>
public class EventCacheService : IDisposable
{
    private readonly PartakeService   _partake;
    private readonly FFXIVenueService _ffxivenue;
    private readonly Configuration    _config;
    private readonly IPluginLog       _log;

    private readonly SemaphoreSlim         _lock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _bgTask;

    // ── Public state ──────────────────────────────────────────────────────────
    public List<VenueEvent> CachedEvents  { get; private set; } = new();

    // Events grouped by DC name (key = DC name, value = sorted list of events)
    public Dictionary<string, List<VenueEvent>> EventsByDc { get; private set; } = new();

    // Tag sets per DC for the filter checkboxes
    public Dictionary<string, SortedDictionary<string, bool>> TagsByDc { get; private set; } = new();

    public DateTime LastRefresh { get; private set; } = DateTime.MinValue;
    public bool     IsRefreshing { get; private set; }
    public string?  LastError    { get; private set; }

    public event Action<List<VenueEvent>>? OnNewEventsDetected;

    public EventCacheService(PartakeService partake, FFXIVenueService ffxivenue,
                             Configuration config, IPluginLog log)
    {
        _partake   = partake;
        _ffxivenue = ffxivenue;
        _config    = config;
        _log       = log;
    }

    public void Start()
    {
        _bgTask = Task.Run(BackgroundLoop, _cts.Token);
        _log.Debug("[Cache] Auto-refresh started.");
    }

    public async Task RefreshNowAsync() => await FetchAndUpdateAsync();

    // ── Background loop ───────────────────────────────────────────────────────
    private async Task BackgroundLoop()
    {
        await FetchAndUpdateAsync();
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _config.RefreshIntervalMinutes)), _cts.Token);
                await FetchAndUpdateAsync();
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex) { _log.Error($"[Cache] Background loop error: {ex.Message}"); }
        }
    }

    private async Task FetchAndUpdateAsync()
    {
        await _lock.WaitAsync();
        IsRefreshing = true;
        LastError    = null;
        try
        {
            var all = new List<VenueEvent>();

            if (_config.ShowPartakeEvents)
                all.AddRange(await _partake.FetchAllEventsAsync());
            if (_config.ShowFFXIVenueEvents)
                all.AddRange(await _ffxivenue.FetchEventsAsync());

            // Detect new events
            var knownIds = GetKnownIds();
            var newEvents = all.Where(e => !knownIds.Contains(e.Id)).ToList();
            foreach (var e in newEvents) e.IsNew = true;

            // Persist known IDs
            _config.LastKnownEventIds = JsonConvert.SerializeObject(all.Select(e => e.Id).ToList());
            _config.Save();

            // Build per-DC groups and tag sets
            // Events without a DC go into the special "_no_location_" bucket
            var byDc   = new Dictionary<string, List<VenueEvent>>();
            var tagsDc = new Dictionary<string, SortedDictionary<string, bool>>();

            foreach (var ev in all)
            {
                var key = string.IsNullOrEmpty(ev.DataCenter) ? "_no_location_" : ev.DataCenter;

                if (!byDc.ContainsKey(key))   byDc[key]   = new();
                if (!tagsDc.ContainsKey(key)) tagsDc[key] = new();
                byDc[key].Add(ev);

                foreach (var tag in ev.Tags)
                    if (!tagsDc[key].ContainsKey(tag))
                        tagsDc[key][tag] = false;
            }

            CachedEvents  = all;
            EventsByDc    = byDc;
            TagsByDc      = tagsDc;
            LastRefresh   = DateTime.UtcNow;

            _log.Debug($"[Cache] Refresh done: {all.Count} events, {newEvents.Count} new, {byDc.Count} DCs.");

            if (newEvents.Count > 0)
                OnNewEventsDetected?.Invoke(newEvents);
        }
        catch (Exception ex)
        {
            LastError = $"Error fetching events: {ex.Message}";
            _log.Error($"[Cache] {LastError}");
        }
        finally
        {
            IsRefreshing = false;
            _lock.Release();
        }
    }

    private HashSet<string> GetKnownIds()
    {
        try
        {
            var list = JsonConvert.DeserializeObject<List<string>>(_config.LastKnownEventIds);
            return list != null ? new HashSet<string>(list) : new HashSet<string>();
        }
        catch { return new HashSet<string>(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _bgTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
        _lock.Dispose();
    }
}
