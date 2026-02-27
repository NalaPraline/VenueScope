using System;
using System.Collections.Generic;

namespace VenueScope.Models;

public enum EventSource
{
    Partake,
    FFXIVenue
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

    // Code Lifestream pour téléportation rapide (ex: "Goblet W8 P12")
    public string LifestreamCode { get; set; } = string.Empty;

    public string BannerUrl    { get; set; } = string.Empty;
    public string TeamIconUrl  { get; set; } = string.Empty;
    public string TeamName     { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string EventUrl { get; set; } = string.Empty;
    public EventSource Source { get; set; }
    public int AttendeeCount { get; set; }
    public bool IsNew { get; set; }
}
