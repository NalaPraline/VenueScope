using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VenueScope.Models;

namespace VenueScope.Services;

/// <summary>
/// Fetches FFXIV events from Partake.gg via the official GraphQL API.
/// Uses pagination (100 per page) to retrieve ALL events.
/// DC and server data is read from Lumina (live game data).
/// </summary>
public partial class PartakeService : IDisposable
{
    private readonly GraphQLHttpClient _graphQL;
    private readonly IPluginLog        _log;

    // Region index matches PartyVerseApi.RegionList order (1=Japan, 2=NA, 3=Europe, 4=Oceania)
    public static readonly List<string> RegionList = ["Unknown", "Japan", "North America", "Europe", "Oceania"];

    // Loaded from Lumina on construction
    public readonly Dictionary<int, DataCenterInfo> DataCenters = new();
    public readonly Dictionary<int, ServerInfo>     Servers     = new();

    public PartakeService(IPluginLog log, IDataManager dataManager)
    {
        _log = log;

        string version = "unknown";
        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(Plugin.PluginInterface.AssemblyLocation.FullName);
            version = fvi.FileVersion ?? "unknown";
        }
        catch (Exception ex) { _log.Warning($"[Partake] Could not read version: {ex.Message}"); }

        _graphQL = new GraphQLHttpClient("https://api.partake.gg/", new NewtonsoftJsonSerializer());
        _graphQL.HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Dalamud-VenueScope/{version}");

        // Load DC and server list from Lumina (same method as PartyPlanner)
        LoadLuminaData(dataManager);
    }

    private void LoadLuminaData(IDataManager dataManager)
    {
        var worldGroups = dataManager.GetExcelSheet<WorldDCGroupType>();
        if (worldGroups != null)
        {
            foreach (var wg in worldGroups)
            {
                var name = wg.Name.ExtractText();
                // Skip dev/cloud/unused DCs
                if (name != "Dev" && name != "Shadow" && wg.Region != 7)
                    DataCenters[(int)wg.RowId] = new DataCenterInfo((int)wg.RowId, name, wg.Region);
            }
        }

        var worlds = dataManager.GetExcelSheet<World>();
        if (worlds != null)
        {
            foreach (var w in worlds)
            {
                if (w.IsPublic && DataCenters.ContainsKey((int)w.DataCenter.RowId))
                    Servers[(int)w.RowId] = new ServerInfo((int)w.RowId, w.Name.ExtractText(), (int)w.DataCenter.RowId);
            }
        }

        _log.Debug($"[Partake] Loaded {DataCenters.Count} data centers and {Servers.Count} servers from Lumina.");
    }

    /// <summary>Fetches all upcoming events (paginated).</summary>
    public async Task<List<VenueEvent>> FetchAllEventsAsync()
    {
        var result = new List<VenueEvent>();

        // 1. Active events (currently ongoing)
        int page = 0;
        bool more = true;
        while (more)
        {
            try
            {
                var batch = await GetActiveEventsAsync(page);
                more = batch.Count >= 100;
                result.AddRange(batch);
                page++;
            }
            catch (Exception ex)
            {
                _log.Error($"[Partake] Error fetching active events page {page}: {ex.Message}");
                break;
            }
        }

        // 2. Upcoming events
        page = 0;
        more = true;
        while (more)
        {
            try
            {
                var batch = await GetEventsAsync(page);
                more = batch.Count >= 100;
                result.AddRange(batch);
                page++;
            }
            catch (Exception ex)
            {
                _log.Error($"[Partake] Error fetching events page {page}: {ex.Message}");
                break;
            }
        }

        // Deduplicate: active events are a subset of all events — same ID can appear twice
        var before = result.Count;
        var deduped = result.DistinctBy(e => e.Id).ToList();
        _log.Debug($"[Partake] Fetched {deduped.Count} events ({before - deduped.Count} duplicates removed).");
        return deduped;
    }

    private async Task<List<VenueEvent>> GetEventsAsync(int page)
    {
        var request = new GraphQLRequest
        {
            Query = $@"
            {{
                events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 100, offset: {page * 100}) {{
                    id, title, locationId, ageRating, attendeeCount,
                    startsAt, endsAt, location, tags,
                    description(type: PLAIN_TEXT)
                    team {{ id name iconUrl }}
                    locationData {{
                        server {{ id name dataCenterId }}
                        dataCenter {{ id name }}
                    }}
                }}
            }}"
        };

        var res = await _graphQL.SendQueryAsync<EventsResponseType>(request);
        if (res.Errors is { Length: > 0 })
        {
            foreach (var err in res.Errors)
                _log.Error($"[Partake] GraphQL error: {err.Message}");
        }
        return MapToVenueEvents(res.Data?.Events ?? new());
    }

    private async Task<List<VenueEvent>> GetActiveEventsAsync(int page)
    {
        var now = DateTime.UtcNow.ToString("o");
        var request = new GraphQLRequest
        {
            Query = $@"
            {{
                events(game: ""final-fantasy-xiv"", sortBy: STARTS_AT, limit: 100, offset: {page * 100},
                       startsBetween: {{ end: ""{now}"" }},
                       endsBetween:   {{ start: ""{now}"" }}) {{
                    id, title, locationId, ageRating, attendeeCount,
                    startsAt, endsAt, location, tags,
                    description(type: PLAIN_TEXT)
                    team {{ id name iconUrl }}
                    locationData {{
                        server {{ id name dataCenterId }}
                        dataCenter {{ id name }}
                    }}
                }}
            }}"
        };

        var res = await _graphQL.SendQueryAsync<EventsResponseType>(request);
        if (res.Errors is { Length: > 0 })
        {
            foreach (var err in res.Errors)
                _log.Error($"[Partake] GraphQL error (active): {err.Message}");
        }
        return MapToVenueEvents(res.Data?.Events ?? new());
    }

    private List<VenueEvent> MapToVenueEvents(List<PartakeEvent> events)
    {
        var result = new List<VenueEvent>(events.Count);
        foreach (var ev in events)
        {
            // Strip non-ASCII (emojis, symbols)
            var title       = CleanUnicode(ev.Title);
            var description = CleanUnicode(ev.Description);

            var serverName = ev.LocationData?.Server?.Name ?? string.Empty;
            var dcName     = ev.LocationData?.DataCenter?.Name ?? string.Empty;

            // Build full tag list: API tags + normalized ageRating
            var tags = new List<string>(ev.Tags);
            var ageTag = NormalizeAgeRating(ev.AgeRating);
            if (!string.IsNullOrEmpty(ageTag) && !tags.Contains(ageTag))
                tags.Add(ageTag);

            result.Add(new VenueEvent
            {
                Id               = $"partake-{ev.Id}",
                Title            = title,
                Description      = description,
                Host             = string.Empty,
                StartTime        = ev.StartsAt,
                EndTime          = ev.EndsAt == default ? null : ev.EndsAt,
                Server           = serverName,
                DataCenter       = dcName,
                InGameLocation   = ev.Location,
                LifestreamCode   = ev.Location,
                Tags             = tags,
                EventUrl         = $"https://www.partake.gg/events/{ev.Id}",
                Source           = EventSource.Partake,
                AttendeeCount    = ev.AttendeeCount,
                TeamName         = ev.Team?.Name ?? string.Empty,
                TeamIconUrl      = ev.Team?.IconUrl ?? string.Empty,
            });
        }
        return result;
    }

    /// <summary>Converts Partake ageRating enum values to human-readable tags.</summary>
    private static string NormalizeAgeRating(string raw) => raw?.ToUpperInvariant() switch
    {
        "ALL_AGES" or "ALLAGES"  => "All Ages",
        "18_PLUS"  or "18PLUS"   => "18+",
        "MATURE"                 => "Mature",
        "MINORS_OK"              => "Minors OK",
        _ when !string.IsNullOrEmpty(raw) => raw, // unknown value: pass through as-is
        _                        => string.Empty,
    };

    private static string CleanUnicode(string s)
        => CleanUnicodeRegex().Replace(s, string.Empty).Trim();

    [GeneratedRegex(@"[^\u0000-\u007F]+")]
    private static partial Regex CleanUnicodeRegex();

    public void Dispose() => _graphQL.Dispose();
}

// Lumina-derived info types
public record DataCenterInfo(int Id, string Name, int Region);
public record ServerInfo(int Id, string Name, int DataCenterId);
