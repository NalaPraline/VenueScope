using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using VenueScope.Models;

namespace VenueScope.Services;

public class PartyFinderService : IDisposable
{
    private const string ApiUrl = "https://api.venuescope.club/v1/partyfinder?limit=200";

    private readonly HttpClient _http;
    private readonly IPluginLog _log;

    public PartyFinderService(IPluginLog log)
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
            _log.Warning($"[PartyFinder] Could not fetch listings: {ex.Message}");
            return new List<VenueEvent>();
        }
    }

    private static VenueEvent? Map(JObject p)
    {
        var listed  = Time(p["listedAt"]);
        var expires = Time(p["expiresAt"]);
        if (listed == null) return null;

        var place    = p["place"] as JObject;
        var links    = p["links"] as JObject;
        var world    = place?["world"]?.ToString() ?? string.Empty;
        var district = place?["district"]?.ToString() ?? string.Empty;
        int ward     = place?["ward"]?.Value<int>() ?? 0;
        int plot     = place?["plot"]?.Value<int>() ?? 0;
        int apt      = place?["apartment"]?.Value<int>() ?? 0;
        bool sub     = place?["subdivision"]?.Value<bool>() ?? false;
        var spot     = apt > 0 ? $"Apt {apt}" : $"P{plot}";
        var code     = apt > 0 ? $"A{apt}" : $"P{plot}";
        var who      = p["recruiter"]?.ToString() ?? string.Empty;

        return new VenueEvent
        {
            Id             = p["id"]?.ToString() ?? $"pf-{Guid.NewGuid()}",
            Source         = EventSource.PartyFinder,
            Title          = p["title"]?.ToString() ?? $"Listing by {who}",
            Description    = p["description"]?.ToString() ?? string.Empty,
            Host           = who,
            TeamName       = who,
            StartTime      = listed.Value,
            EndTime        = expires,
            Server         = world,
            DataCenter     = place?["dataCenter"]?.ToString() ?? string.Empty,
            InGameLocation = ward > 0 ? string.Join(", ", new[] { district, $"W{ward}{(sub ? " (Sub)" : "")}", spot }.Where(s => s.Length > 0)) : string.Empty,
            LifestreamCode = ward > 0 && district.Length > 0 && world.Length > 0 ? $"{world} {district} W{ward} {code}" : string.Empty,
            Tags           = (p["tags"] as JArray)?.Select(t => t.ToString()).ToList() ?? new List<string>(),
            DiscordUrl     = links?["discord"]?.ToString() ?? string.Empty,
            WebsiteUrl     = links?["website"]?.ToString() ?? string.Empty,
            EventUrl       = links?["partake"]?.ToString() ?? string.Empty,
        };
    }

    // borrow a banner and a name from the venue the ad points to, when we know it
    public static void LinkToVenues(List<VenueEvent> all)
    {
        var partake = all.Where(e => e.Source == EventSource.Partake)
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var byAddress = all.Where(e => e.Source != EventSource.PartyFinder && e.LifestreamCode.Length > 0)
            .GroupBy(e => AddressKey(e.LifestreamCode))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Source == EventSource.FFXIVenue ? 0 : 1).First());

        foreach (var ad in all.Where(e => e.Source == EventSource.PartyFinder))
        {
            VenueEvent? hit = null;
            var m = System.Text.RegularExpressions.Regex.Match(ad.EventUrl, @"partake\.gg/events/(\d+)");
            if (m.Success) partake.TryGetValue($"partake-{m.Groups[1].Value}", out hit);
            if (hit == null && ad.LifestreamCode.Length > 0) byAddress.TryGetValue(AddressKey(ad.LifestreamCode), out hit);
            if (hit == null) continue;

            ad.BannerUrl   = hit.Images.FirstOrDefault() ?? hit.BannerUrl;
            ad.TeamIconUrl = hit.TeamIconUrl.Length > 0 ? hit.TeamIconUrl : hit.BannerUrl;
            ad.PlaceName   = hit.Source == EventSource.Partake && hit.TeamName.Length > 0 ? hit.TeamName : hit.VenueName;
            if (ad.Title.StartsWith("Listing by ")) ad.Title = hit.Source == EventSource.Partake ? hit.Title : hit.VenueName;
            if (ad.DiscordUrl.Length == 0) ad.DiscordUrl = hit.DiscordUrl;
        }
    }

    private static string AddressKey(string code) =>
        System.Text.RegularExpressions.Regex.Replace(code.ToLowerInvariant().Replace("the ", ""), @"\s+", " ").Trim();

    private static DateTime? Time(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        if (t.Type == JTokenType.Date) return t.Value<DateTime>().ToUniversalTime();
        return DateTimeOffset.TryParse(t.ToString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;
    }

    public void Dispose() => _http.Dispose();
}
