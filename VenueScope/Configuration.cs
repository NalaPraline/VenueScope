using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace VenueScope;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ── Navigation memory ──────────────────────────────────────────────────
    public string       SelectedRegion      { get; set; } = string.Empty;
    public string       SelectedDataCenter  { get; set; } = string.Empty; // legacy single-DC
    public List<string> SelectedDataCenters { get; set; } = new();

    // ── Sources ────────────────────────────────────────────────────────────
    public bool ShowPartakeEvents   { get; set; } = true;
    public bool ShowFFXIVenueEvents { get; set; } = true;

    // ── Refresh ────────────────────────────────────────────────────────────
    public int RefreshIntervalMinutes { get; set; } = 5;

    // Last known event IDs for NEW-badge detection (JSON array)
    public string LastKnownEventIds { get; set; } = "[]";

    // ── Notifications ──────────────────────────────────────────────────────
    public bool EnableNotifications { get; set; } = true;
    // Empty list = notify for all DCs; non-empty = only these DCs
    public List<string> NotifyForDataCenters { get; set; } = new();

    // ── Display ────────────────────────────────────────────────────────────
    public bool HideEndedEvents    { get; set; } = false;
    // 0 = All, 1 = Live Now, 2 = Today
    public int  DefaultTimeFilter  { get; set; } = 0;
    // -1 = All, 0 = Partake, 1 = FFXIVenue
    public int  DefaultSourceFilter { get; set; } = -1;

    // ── Favorites ──────────────────────────────────────────────────────────
    public HashSet<string> FavoriteEventIds { get; set; } = new();
    public HashSet<string> FavoriteVenueIds { get; set; } = new();

    // ── Legacy (kept for compat, not exposed in UI) ────────────────────────
    public List<string> FavoriteDataCenters { get; set; } = new();
    public List<string> FavoriteServers     { get; set; } = new();

    // ── Runtime-only (not serialized) ─────────────────────────────────────
    [NonSerialized] public bool SelectedRegionSet     = false;
    [NonSerialized] public bool SelectedDataCenterSet = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
