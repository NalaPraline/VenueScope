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

    // Navigation
    public string       SelectedRegion      { get; set; } = string.Empty;
    public string       SelectedDataCenter  { get; set; } = string.Empty;
    public List<string> SelectedDataCenters { get; set; } = new();

    // Sources
    public bool ShowPartakeEvents   { get; set; } = true;
    public bool ShowFFXIVenueEvents { get; set; } = true;

    // Refresh
    public int RefreshIntervalMinutes { get; set; } = 5;

    // Last known event IDs for new-badge detection
    public string LastKnownEventIds { get; set; } = "[]";

    // Notifications
    public bool EnableNotifications { get; set; } = true;
    public List<string> NotifyForDataCenters { get; set; } = new();

    // Display
    public bool HideEndedEvents     { get; set; } = false;
    public int  DefaultTimeFilter   { get; set; } = 0;   // 0 = All, 1 = Live Now, 2 = Today
    public int  DefaultSourceFilter { get; set; } = -1;  // -1 = All, 0 = Partake, 1 = FFXIVenue

    // Favorites
    public HashSet<string> FavoriteEventIds      { get; set; } = new();
    public HashSet<int>    FavoritePartakeTeamIds { get; set; } = new();
    public Dictionary<string, FavoriteVenueInfo> FavoriteVenueCache { get; set; } = new();

    // Hidden venues
    public HashSet<string> HiddenVenueIds      { get; set; } = new();
    public HashSet<int>    HiddenPartakeTeamIds { get; set; } = new();
    public Dictionary<string, FavoriteVenueInfo> HiddenVenueCache { get; set; } = new();

    // One character per region for auto-switch on teleport
    // Key: "Japan" | "North America" | "Europe" | "Oceania"  Value: "Name@World"
    public Dictionary<string, string> CharacterPerRegion { get; set; } = new();

    // Pending teleport — stored before character switch, executed after login
    public string PendingVenueServer        { get; set; } = string.Empty;
    public string PendingVenueCode          { get; set; } = string.Empty;
    public string PendingExpectedCharacter  { get; set; } = string.Empty;

    // Pending ConnectAndTravel — stores travel target while waiting for title screen
    public string PendingTravelCharName     { get; set; } = string.Empty;
    public string PendingTravelHomeWorld    { get; set; } = string.Empty;
    public string PendingTravelDestination  { get; set; } = string.Empty;

    // Legacy
    public List<string> FavoriteDataCenters { get; set; } = new();
    public List<string> FavoriteServers     { get; set; } = new();

    // Not serialized
    [NonSerialized] public bool SelectedRegionSet     = false;
    [NonSerialized] public bool SelectedDataCenterSet = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
