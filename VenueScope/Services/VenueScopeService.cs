using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using VenueScope.Models;

namespace VenueScope.Services;

public class VenueScopeService : IDisposable
{
    private const string ApiUrl = "https://api.venuescope.club/v1/events?limit=200";

    private static readonly Dictionary<string, string> DetailLabels = new()
    {
        ["pot"]     = "Pot",
        ["price"]   = "Price",
        ["bets"]    = "Bets",
        ["buyIn"]   = "Entry",
        ["payout"]  = "Wins",
        ["prize"]   = "Prize",
        ["winners"] = "Winners",
        ["variant"] = "Game",
        ["rules"]   = "Rules",
        ["howTo"]   = "How to enter",
        ["note"]    = "Note",
    };

    private readonly HttpClient _http;
    private readonly IPluginLog _log;

    public VenueScopeService(IPluginLog log)
    {
        _log  = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Add("User-Agent", "Dalamud-VenueScope/1.0");
    }

    public async Task<List<VenueEvent>> FetchEventsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl);
            var data = JObject.Parse(json)["data"] as JArray ?? new JArray();
            return data.OfType<JObject>().Select(Map).Where(e => e != null).Select(e => e!).ToList();
        }
        catch (Exception ex)
        {
            _log.Warning($"[VenueScope] Could not fetch events: {ex.Message}");
            return new List<VenueEvent>();
        }
    }

    private static VenueEvent? Map(JObject e)
    {
        var start = Time(e["startsAt"]);
        if (start == null) return null;

        var venue = e["venue"] as JObject;
        var place = e["place"] as JObject;
        var links = e["links"] as JObject;

        var tags = new List<string>();
        tags.AddRange(Strings(e["highlights"]));
        var rating = e["rating"]?.ToString();
        if (rating == "adult") tags.Add("18+");
        if (rating == "nsfw")  tags.Add("NSFW");

        return new VenueEvent
        {
            Id             = $"venuescope-{e["id"]}",
            Source         = EventSource.VenueScope,
            Title          = e["title"]?.ToString() ?? string.Empty,
            Summary        = e["summary"]?.ToString() ?? string.Empty,
            Description    = e["description"]?.ToString() ?? string.Empty,
            BannerUrl      = e["bannerUrl"]?.ToString() ?? string.Empty,
            StartTime      = start.Value,
            EndTime        = Time(e["endsAt"]),
            VenueId        = venue?["id"]?.ToString() ?? string.Empty,
            TeamName       = venue?["name"]?.ToString() ?? string.Empty,
            TeamIconUrl    = venue?["logoUrl"]?.ToString() ?? string.Empty,
            Server         = Text(place?["world"]) ?? Text(venue?["world"]) ?? string.Empty,
            DataCenter     = Text(place?["dataCenter"]) ?? Text(venue?["dataCenter"]) ?? string.Empty,
            InGameLocation = Address(place),
            LifestreamCode = Lifestream(place),
            Tags           = tags,
            RpStyle        = e["rpStyle"]?.ToString() ?? string.Empty,
            EventUrl       = string.Empty,
            DiscordUrl     = links?["discord"]?.ToString() ?? string.Empty,
            WebsiteUrl     = links?["website"]?.ToString() ?? string.Empty,
            InstagramUrl   = links?["instagram"]?.ToString() ?? string.Empty,
            Lineup         = (e["lineup"] as JArray ?? new JArray()).OfType<JObject>().Select(l => new LineupSlot
            {
                Start   = Time(l["startsAt"]),
                End     = Time(l["endsAt"]),
                Name    = l["name"]?.ToString() ?? string.Empty,
                Link    = l["link"]?.ToString() ?? string.Empty,
                LogoUrl = l["logoUrl"]?.ToString() ?? string.Empty,
            }).ToList(),
            Activities     = (e["activities"] as JArray ?? new JArray()).OfType<JObject>().Select(a => new NightActivity
            {
                Kind    = a["kind"]?.ToString() ?? string.Empty,
                Label   = a["label"]?.ToString() ?? string.Empty,
                Start   = Time(a["startsAt"]),
                End     = Time(a["endsAt"]),
                Title   = a["title"]?.ToString() ?? string.Empty,
                Host    = a["host"]?.ToString() ?? string.Empty,
                Link    = a["link"]?.ToString() ?? string.Empty,
                Summary = a["summary"]?.ToString() ?? string.Empty,
                Details = Details(a["details"] as JObject),
            }).ToList(),
        };
    }

    private static List<(string, string)> Details(JObject? d)
    {
        var list = new List<(string, string)>();
        if (d == null) return list;
        foreach (var (key, label) in DetailLabels)
        {
            var v = d[key];
            if (v == null) continue;
            string text = v is JArray arr ? string.Join(", ", arr.Select(x => x.ToString())) : v.ToString();
            if (key == "winners" && (text == "0" || text == "1")) continue;
            if (!string.IsNullOrWhiteSpace(text)) list.Add((label, text));
        }
        return list;
    }

    private static string Address(JObject? p)
    {
        if (p == null) return string.Empty;
        var text = p["text"]?.ToString();
        if (!string.IsNullOrEmpty(text)) return text;
        int ward = p["ward"]?.Value<int>() ?? 0;
        if (ward <= 0) return string.Empty;
        int plot = p["plot"]?.Value<int>() ?? 0, apt = p["apartment"]?.Value<int>() ?? 0, room = p["room"]?.Value<int>() ?? 0;
        bool sub = p["subdivision"]?.Value<bool>() ?? false;
        var parts = new List<string> { p["district"]?.ToString() ?? string.Empty, $"W{ward}{(sub ? " (Sub)" : "")}" };
        if (plot > 0)     parts.Add($"P{plot}");
        else if (apt > 0) parts.Add($"Apt {apt}");
        if (room > 0)     parts.Add($"Room {room}");
        return string.Join(", ", parts.Where(s => s.Length > 0));
    }

    private static string Lifestream(JObject? p)
    {
        var world = p?["world"]?.ToString();
        int ward  = p?["ward"]?.Value<int>() ?? 0;
        if (string.IsNullOrEmpty(world) || ward <= 0) return string.Empty;
        int plot = p!["plot"]?.Value<int>() ?? 0, apt = p["apartment"]?.Value<int>() ?? 0, room = p["room"]?.Value<int>() ?? 0;
        var parts = new List<string> { world };
        var district = p["district"]?.ToString();
        if (!string.IsNullOrEmpty(district)) parts.Add(district);
        parts.Add($"W{ward}");
        if (plot > 0)     parts.Add($"P{plot}");
        else if (apt > 0) parts.Add($"A{apt}");
        if (room > 0)     parts.Add($"A{room}");
        return string.Join(" ", parts);
    }

    private static string? Text(JToken? t)
    {
        var s = t?.ToString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static IEnumerable<string> Strings(JToken? t) =>
        t is JArray arr ? arr.Select(x => x.ToString()).Where(s => s.Length > 0) : Enumerable.Empty<string>();

    private static DateTime? Time(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        if (t.Type == JTokenType.Date) return t.Value<DateTime>().ToUniversalTime();
        return DateTimeOffset.TryParse(t.ToString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;
    }

    public void Dispose() => _http.Dispose();
}
