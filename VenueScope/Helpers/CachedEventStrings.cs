namespace VenueScope.Helpers;

/// <summary>Pre-formatted display strings for a single event row.</summary>
public class CachedEventStrings
{
    public string StartsAtHumanized { get; set; } = string.Empty;
    public string EndsAtHumanized   { get; set; } = string.Empty;
    public string StartsAtLocal     { get; set; } = string.Empty;
    public string EndsAtLocal       { get; set; } = string.Empty;
    public string[] Tags            { get; set; } = [];
    public string Location          { get; set; } = string.Empty;  // "[Server] in-game loc"
    public string ServerDc          { get; set; } = string.Empty;  // "Balmung (Crystal)"
    public string SourceBadge       { get; set; } = string.Empty;
    public int    AttendeeCount     { get; set; }
    public bool   IsLive            { get; set; }  // ongoing right now
    public bool   IsStartingSoon    { get; set; }  // < 30 min
    public bool   HasEnded          { get; set; }  // past end time
}
