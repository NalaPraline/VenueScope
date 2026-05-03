using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using VenueScope.Models;

namespace VenueScope;

[Serializable]
public class FavoriteVenueInfo
{
    public string      VenueId    { get; set; } = string.Empty;
    public int         TeamId     { get; set; } = 0;
    public string      Name       { get; set; } = string.Empty;
    public string      Server     { get; set; } = string.Empty;
    public string      DataCenter { get; set; } = string.Empty;
    public string      IconUrl    { get; set; } = string.Empty;
    public EventSource Source     { get; set; }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string       SelectedRegion      { get; set; } = string.Empty;
    public string       SelectedDataCenter  { get; set; } = string.Empty;
    public List<string> SelectedDataCenters { get; set; } = new();

    public bool ShowPartakeEvents   { get; set; } = true;
    public bool ShowFFXIVenueEvents { get; set; } = true;

    public int RefreshIntervalMinutes { get; set; } = 5;

    public string LastKnownEventIds { get; set; } = "[]";

    public bool EnableNotifications      { get; set; } = true;
    public bool EnableSyncshellPopup     { get; set; } = true;
    public List<string> NotifyForDataCenters { get; set; } = new();

    public bool HideEndedEvents     { get; set; } = false;
    public int  DefaultTimeFilter   { get; set; } = 0;
    public int  DefaultSourceFilter { get; set; } = -1;

    public HashSet<string> FavoriteEventIds      { get; set; } = new();
    public HashSet<int>    FavoritePartakeTeamIds { get; set; } = new();
    public Dictionary<string, FavoriteVenueInfo> FavoriteVenueCache { get; set; } = new();

    public HashSet<string> HiddenVenueIds      { get; set; } = new();
    public HashSet<int>    HiddenPartakeTeamIds { get; set; } = new();
    public Dictionary<string, FavoriteVenueInfo> HiddenVenueCache { get; set; } = new();

    public Dictionary<string, string> CharacterPerRegion { get; set; } = new();

    public string PendingVenueServer        { get; set; } = string.Empty;
    public string PendingVenueCode          { get; set; } = string.Empty;
    public string PendingExpectedCharacter  { get; set; } = string.Empty;

    public string PendingTravelCharName     { get; set; } = string.Empty;
    public string PendingTravelHomeWorld    { get; set; } = string.Empty;
    public string PendingTravelDestination  { get; set; } = string.Empty;

    public List<string> FavoriteDataCenters { get; set; } = new();
    public List<string> FavoriteServers     { get; set; } = new();

    public string SynchellApiUrl { get; set; } = "https://venuescope-synchells.yunookami.workers.dev/synchells";

    [NonSerialized] public bool SelectedRegionSet     = false;
    [NonSerialized] public bool SelectedDataCenterSet = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
