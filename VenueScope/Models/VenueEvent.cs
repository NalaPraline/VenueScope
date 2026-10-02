using System;
using System.Collections.Generic;

namespace VenueScope.Models;

public enum EventSource
{
    Partake,
    FFXIVenue,
    VenueScope
}

public class LineupSlot
{
    public DateTime? Start   { get; set; }
    public DateTime? End     { get; set; }
    public string    Name    { get; set; } = string.Empty;
    public string    Link    { get; set; } = string.Empty;
    public string    LogoUrl { get; set; } = string.Empty;
}

public class NightActivity
{
    public string    Kind    { get; set; } = string.Empty;
    public string    Label   { get; set; } = string.Empty;
    public DateTime? Start   { get; set; }
    public DateTime? End     { get; set; }
    public string    Title   { get; set; } = string.Empty;
    public string    Host    { get; set; } = string.Empty;
    public string    Summary { get; set; } = string.Empty;
    public string    Link    { get; set; } = string.Empty;
    public List<(string Label, string Value)> Details { get; set; } = new();
}

public class Opening
{
    public DateTime  Start  { get; set; }
    public DateTime? End    { get; set; }
    public bool      Closed { get; set; }
}

public class VenueEvent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Server { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string InGameLocation { get; set; } = string.Empty;

    public string LifestreamCode { get; set; } = string.Empty;

    public string BannerUrl    { get; set; } = string.Empty;
    public string TeamIconUrl  { get; set; } = string.Empty;
    public string TeamName     { get; set; } = string.Empty;
    public string TeamDescription { get; set; } = string.Empty;
    public int    TeamId       { get; set; } = 0;
    public List<string> Tags { get; set; } = new();
    public string EventUrl { get; set; } = string.Empty;
    public string DiscordUrl { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string InstagramUrl { get; set; } = string.Empty;
    public EventSource Source { get; set; }
    public int AttendeeCount { get; set; }
    public List<string> Images { get; set; } = new();
    public bool Hiring { get; set; }
    public List<Opening> Openings { get; set; } = new();

    public string VenueId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string RpStyle { get; set; } = string.Empty;
    public List<LineupSlot>    Lineup     { get; set; } = new();
    public List<NightActivity> Activities { get; set; } = new();

    public string VenueKey  => Source == EventSource.VenueScope ? $"venuescope-{VenueId}" : Id;
    public string VenueName => Source == EventSource.FFXIVenue ? Title : TeamName;
    public bool IsNew { get; set; }

    [System.NonSerialized]
    public SynchellEntry? LinkedSynchell;
}
