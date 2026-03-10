using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace VenueScope.Models;

public class EventsResponseType
{
    [JsonProperty("events")]
    public List<PartakeEvent> Events { get; set; } = new();
}

public class PartakeEvent
{
    [JsonProperty("id")]           public int    Id           { get; set; }
    [JsonProperty("title")]        public string Title        { get; set; } = string.Empty;
    [JsonProperty("description")]  public string Description  { get; set; } = string.Empty;
    [JsonProperty("location")]     public string Location     { get; set; } = string.Empty;
    [JsonProperty("tags")]         public string[] Tags       { get; set; } = Array.Empty<string>();
    [JsonProperty("ageRating")]    public string   AgeRating  { get; set; } = string.Empty;
    [JsonProperty("startsAt")]     public DateTime StartsAt   { get; set; }
    [JsonProperty("endsAt")]       public DateTime EndsAt     { get; set; }
    [JsonProperty("attendeeCount")] public int AttendeeCount  { get; set; }
    [JsonProperty("locationData")] public PartakeLocationData? LocationData { get; set; }

    [JsonProperty("team")] public PartakeTeam? Team { get; set; }

    private HashSet<string>? _tagsSet;
    [JsonIgnore] public HashSet<string> TagsSet => _tagsSet ??= new HashSet<string>(Tags);
}

public class PartakeTeam
{
    [JsonProperty("id")]      public int    Id      { get; set; }
    [JsonProperty("name")]    public string Name    { get; set; } = string.Empty;
    [JsonProperty("iconUrl")] public string? IconUrl { get; set; }
}

public class PartakeLocationData
{
    [JsonProperty("server")]     public PartakeServerData?     Server     { get; set; }
    [JsonProperty("dataCenter")] public PartakeDataCenterData? DataCenter { get; set; }
}

public class PartakeServerData
{
    [JsonProperty("id")]           public int    Id           { get; set; }
    [JsonProperty("name")]         public string Name         { get; set; } = string.Empty;
    [JsonProperty("dataCenterId")] public int    DataCenterId { get; set; }
}

public class PartakeDataCenterData
{
    [JsonProperty("id")]   public int    Id   { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
}
