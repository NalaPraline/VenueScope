using System.Collections.Generic;
using Newtonsoft.Json;

namespace VenueScope.Models;

public class SynchellLocation
{
    [JsonProperty("server")]
    public string Server { get; set; } = string.Empty;

    [JsonProperty("district")]
    public string District { get; set; } = string.Empty;

    [JsonProperty("ward")]
    public int Ward { get; set; }

    [JsonProperty("plot")]
    public int Plot { get; set; }
}

public class SynchellChannel
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("password")]
    public string Password { get; set; } = string.Empty;
}

public class SynchellEntry
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("venueName")]
    public string VenueName { get; set; } = string.Empty;

    [JsonProperty("locations")]
    public List<SynchellLocation> Locations { get; set; } = new();

    [JsonProperty("channels")]
    public List<SynchellChannel> Channels { get; set; } = new();
}
