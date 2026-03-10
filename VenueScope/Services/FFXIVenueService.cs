using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using VenueScope.Models;

namespace VenueScope.Services;

public class FFXIVenueService : IDisposable
{
    private readonly HttpClient _http;
    private readonly IPluginLog _log;

    private const string ApiUrl = "https://api.ffxivvenues.com/v1.0/venue";

    public FFXIVenueService(IPluginLog log)
    {
        _log = log;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "Dalamud-VenueScope/1.0");
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<List<VenueEvent>> FetchEventsAsync()
    {
        try
        {
            var json   = await _http.GetStringAsync(ApiUrl);
            var events = ParseResponse(json);
            _log.Debug($"[FFXIVenue] Fetched {events.Count} venues with upcoming openings.");
            return events;
        }
        catch (Exception ex)
        {
            _log.Warning($"[FFXIVenue] Could not fetch events: {ex.Message}");
            return new List<VenueEvent>();
        }
    }

    private List<VenueEvent> ParseResponse(string json)
    {
        var result = new List<VenueEvent>();
        try
        {
            var arr = JArray.Parse(json);

            foreach (JObject item in arr.OfType<JObject>())
            {
                if (!ResolveNextOpening(item, out var startDto, out var endDto)) continue;

                var id   = item["id"]?.ToString()   ?? Guid.NewGuid().ToString();
                var name = item["name"]?.ToString()  ?? "Unknown Venue";

                var descArr = item["description"] as JArray;
                var desc    = descArr != null
                    ? string.Join("\n", descArr.Select(p => p.ToString()))
                    : item["description"]?.ToString() ?? string.Empty;

                var managersArr = item["managers"] as JArray;
                var host        = managersArr?.FirstOrDefault()?.ToString() ?? string.Empty;

                var locObj = item["location"] as JObject;
                var dc     = locObj?["dataCenter"]?.ToString() ?? string.Empty;
                var server = locObj?["world"]?.ToString()      ?? string.Empty;
                var locStr = BuildLocationString(locObj);

                var banner  = item["bannerUri"]?.ToString() ?? string.Empty;
                var website = item["website"]?.ToString()   ?? $"https://ffxivvenues.com/{id}";

                var tags = new List<string>();
                if (item["tags"] is JArray tagsArr)
                    foreach (var t in tagsArr)
                    {
                        var ts = t.ToString();
                        if (!string.IsNullOrEmpty(ts)) tags.Add(ts);
                    }

                var sfwToken = item["sfw"];
                if (sfwToken != null && sfwToken.Type == JTokenType.Boolean)
                    tags.Add(sfwToken.Value<bool>() ? "SFW" : "18+");

                result.Add(new VenueEvent
                {
                    Id             = $"ffxivenue-{id}",
                    Title          = name,
                    Description    = desc,
                    Host           = host,
                    StartTime      = startDto.UtcDateTime,
                    EndTime        = endDto?.UtcDateTime,
                    Server         = server,
                    DataCenter     = dc,
                    InGameLocation = locStr,
                    LifestreamCode = BuildLifestreamCode(locObj),
                    BannerUrl      = banner,
                    Tags           = tags,
                    EventUrl       = website,
                    Source         = EventSource.FFXIVenue,
                });
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"[FFXIVenue] Parse error: {ex.Message}");
        }
        return result;
    }

    private static bool ResolveNextOpening(JObject item,
        out DateTimeOffset start, out DateTimeOffset? end)
    {
        start = DateTimeOffset.MaxValue;
        end   = null;

        if (item["schedule"] is JArray schedules)
        {
            foreach (JObject sch in schedules.OfType<JObject>())
            {
                if (sch["resolution"] is not JObject res) continue;
                if (!DateTimeOffset.TryParse(res["start"]?.ToString(), out var s)) continue;

                if (s < start)
                {
                    start = s;
                    end   = DateTimeOffset.TryParse(res["end"]?.ToString(), out var e) ? e : null;
                }
            }
        }

        if (item["scheduleOverrides"] is JArray overrides)
        {
            foreach (JObject ov in overrides.OfType<JObject>())
            {
                if (!(ov["open"]?.Value<bool>() ?? false)) continue;
                if (!DateTimeOffset.TryParse(ov["start"]?.ToString(), out var s)) continue;

                var ovEndStr = ov["end"]?.ToString();
                if (DateTimeOffset.TryParse(ovEndStr, out var ovEnd) && ovEnd < DateTimeOffset.UtcNow) continue;
                // no end time + past start = unknown duration, skip
                if (string.IsNullOrEmpty(ovEndStr) && s < DateTimeOffset.UtcNow) continue;

                if (s < start)
                {
                    start = s;
                    end   = DateTimeOffset.TryParse(ov["end"]?.ToString(), out var e) ? e : null;
                }
            }
        }

        return start != DateTimeOffset.MaxValue;
    }

    private static string BuildLocationString(JObject? loc)
    {
        if (loc == null) return string.Empty;

        var ovr = loc["override"]?.ToString();
        if (!string.IsNullOrWhiteSpace(ovr)) return ovr;

        var parts    = new List<string>();
        var district = loc["district"]?.ToString();
        var ward     = loc["ward"]?.Value<int>();
        var plot     = loc["plot"]?.Value<int>();
        var apt      = loc["apartment"]?.Value<int>();
        var room     = loc["room"]?.Value<int>();
        var sub      = loc["subdivision"]?.Value<bool>() ?? false;

        if (!string.IsNullOrEmpty(district)) parts.Add(district);
        if (ward is > 0) parts.Add($"W{ward}{(sub ? " (Sub)" : "")}");
        if (plot is > 0)            parts.Add($"P{plot}");
        else if (apt is > 0)        parts.Add($"Apt {apt}");
        if (room is > 0)            parts.Add($"Room {room}");

        return string.Join(", ", parts);
    }

    private static string BuildLifestreamCode(JObject? loc)
    {
        if (loc == null) return string.Empty;

        var world    = loc["world"]?.ToString();
        if (string.IsNullOrEmpty(world)) return string.Empty;

        var ward     = loc["ward"]?.Value<int>();
        var plot     = loc["plot"]?.Value<int>();
        var apt      = loc["apartment"]?.Value<int>();
        var room     = loc["room"]?.Value<int>();
        var district = loc["district"]?.ToString();
        if (ward is > 0)
        {
            var parts = new List<string> { world };
            if (!string.IsNullOrEmpty(district)) parts.Add(district);
            parts.Add($"W{ward}");
            // "Sub" omitted: Lifestream doesn't handle it
            if (plot is > 0)       parts.Add($"P{plot}");
            else if (apt is > 0)   parts.Add($"Apt{apt}");
            if (room is > 0)       parts.Add($"R{room}");
            return string.Join(" ", parts);
        }

        var ovr = loc["override"]?.ToString();
        if (!string.IsNullOrWhiteSpace(ovr))
        {
            ovr = Regex.Replace(ovr, @"\bW(\d+)P(\d+)\b", "W$1 P$2", RegexOptions.IgnoreCase);
            return $"{world} - {ovr}";
        }

        return string.Empty;
    }

    public async Task<bool> FlagVenueAsync(string venueId, string category, string description)
    {
        try
        {
            var payload = new JObject
            {
                ["venueId"]     = venueId,
                ["category"]    = category,
                ["description"] = description,
            };
            var content  = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PutAsync(
                $"https://api.ffxivvenues.com/v1.0/venue/{venueId}/flag", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.Warning($"[FFXIVenue] Flag error: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
