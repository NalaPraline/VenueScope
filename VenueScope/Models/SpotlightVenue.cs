using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json;

namespace VenueScope.Models;

public enum SpotlightStatus
{
    None,
    Upcoming,
    Live,
    Ended,
}

public class SpotlightLineupEntry
{
    [JsonProperty("time")]
    public string Time { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("link")]
    public string Link { get; set; } = string.Empty;
}

public class SpotlightVenue
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("imageUrl")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonProperty("tagline")]
    public string Tagline { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("server")]
    public string Server { get; set; } = string.Empty;

    [JsonProperty("district")]
    public string District { get; set; } = string.Empty;

    [JsonProperty("ward")]
    public int Ward { get; set; }

    [JsonProperty("plot")]
    public int Plot { get; set; }

    [JsonProperty("activities")]
    public List<string> Activities { get; set; } = new();

    [JsonProperty("lineup")]
    public List<SpotlightLineupEntry> Lineup { get; set; } = new();

    [JsonProperty("discordUrl")]
    public string DiscordUrl { get; set; } = string.Empty;

    [JsonProperty("websiteUrl")]
    public string WebsiteUrl { get; set; } = string.Empty;

    [JsonProperty("accentColor")]
    public string AccentColor { get; set; } = string.Empty;

    [JsonProperty("startAt")]
    public string StartAt { get; set; } = string.Empty;

    [JsonProperty("endAt")]
    public string EndAt { get; set; } = string.Empty;

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("synchell")]
    public SynchellEntry? Synchell { get; set; }

    public string BuildLifestreamCode()
    {
        if (string.IsNullOrEmpty(Server) || Ward <= 0 || Plot <= 0)
            return string.Empty;

        var parts = new List<string> { Server };
        if (!string.IsNullOrEmpty(District)) parts.Add(District);
        parts.Add($"W{Ward}");
        parts.Add($"P{Plot}");
        return string.Join(" ", parts);
    }

    public string BuildLocationLabel()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(District)) parts.Add(District);
        if (Ward > 0) parts.Add($"W{Ward}");
        if (Plot > 0) parts.Add($"P{Plot}");
        return string.Join(", ", parts);
    }

    public DateTimeOffset? StartTime => ParseTime(StartAt);
    public DateTimeOffset? EndTime   => ParseTime(EndAt);

    private static DateTimeOffset? ParseTime(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t : null;

    public SpotlightStatus GetStatus(DateTimeOffset now)
    {
        var start = StartTime;
        if (start == null) return SpotlightStatus.None;
        if (now < start.Value) return SpotlightStatus.Upcoming;
        var end = EndTime;
        if (end != null && now > end.Value) return SpotlightStatus.Ended;
        return SpotlightStatus.Live;
    }

    public Vector4? GetAccent()
    {
        var hex = AccentColor?.Trim().TrimStart('#');
        if (string.IsNullOrEmpty(hex) || hex.Length < 6) return null;
        if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return null;
        if (!int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return null;
        if (!int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return null;
        return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }
}
