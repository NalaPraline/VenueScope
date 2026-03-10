namespace VenueScope.Helpers;

public class CachedEventStrings
{
    public string StartsAtHumanized { get; set; } = string.Empty;
    public string EndsAtHumanized   { get; set; } = string.Empty;
    public string StartsAtLocal     { get; set; } = string.Empty;
    public string EndsAtLocal       { get; set; } = string.Empty;
    public string[] Tags            { get; set; } = [];
    public string Location          { get; set; } = string.Empty;
    public string ServerDc          { get; set; } = string.Empty;
    public string SourceBadge       { get; set; } = string.Empty;
    public int    AttendeeCount     { get; set; }
    public bool   IsLive            { get; set; }
    public bool   IsStartingSoon    { get; set; }
    public bool   HasEnded          { get; set; }
}
