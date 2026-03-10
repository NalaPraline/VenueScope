using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VenueScope.Models;

namespace VenueScope.Services;

public class PartakeService : IDisposable
{
    private readonly GraphQLHttpClient _graphQL;
    private readonly IPluginLog        _log;

    // 1=Japan, 2=NA, 3=Europe, 4=Oceania
    public static readonly List<string> RegionList = ["Unknown", "Japan", "North America", "Europe", "Oceania"];

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
                // region 7 = cloud/dev DCs
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

    public async Task<List<VenueEvent>> FetchAllEventsAsync()
    {
        var result = new List<VenueEvent>();

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

        // active events are a subset of upcoming, deduplicate
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
            var title       = SanitizeTitle(ev.Title ?? string.Empty);
            var description = SanitizeTitle(ev.Description ?? string.Empty);

            var serverName = ev.LocationData?.Server?.Name ?? string.Empty;
            var dcName     = ev.LocationData?.DataCenter?.Name ?? string.Empty;

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
                LifestreamCode   = NormalizeLifestreamCode(ev.Location, serverName),
                Tags             = tags,
                EventUrl         = $"https://www.partake.gg/events/{ev.Id}",
                Source           = EventSource.Partake,
                AttendeeCount    = ev.AttendeeCount,
                TeamName         = ev.Team?.Name ?? string.Empty,
                TeamIconUrl      = ev.Team?.IconUrl ?? string.Empty,
                TeamId           = ev.Team?.Id ?? 0,
            });
        }
        return result;
    }

    private static string NormalizeAgeRating(string raw) => raw?.ToUpperInvariant() switch
    {
        "ALL_AGES" or "ALLAGES"  => "All Ages",
        "18_PLUS"  or "18PLUS"   => "18+",
        "MATURE"                 => "Mature",
        "MINORS_OK"              => "Minors OK",
        _ when !string.IsNullOrEmpty(raw) => raw,
        _                        => string.Empty,
    };

    private static string SanitizeTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (c >= '\uFF01' && c <= '\uFF5E') // fullwidth ASCII → regular ASCII
            {
                sb.Append((char)(c - 0xFF01 + 0x21));
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, s[i + 1]);
                i++;
                char? mapped = MathLetterToAscii(cp);
                if (mapped.HasValue)
                    sb.Append(mapped.Value);
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    private static char? MathLetterToAscii(int cp)
    {
        ReadOnlySpan<int> upperBases =
        [
            0x1D400, // Bold
            0x1D434, // Italic
            0x1D468, // Bold Italic
            0x1D49C, // Script
            0x1D4D0, // Bold Script
            0x1D504, // Fraktur
            0x1D538, // Double-Struck
            0x1D56C, // Bold Fraktur
            0x1D5A0, // Sans-Serif
            0x1D5D4, // Sans-Serif Bold
            0x1D608, // Sans-Serif Italic
            0x1D63C, // Sans-Serif Bold Italic
            0x1D670, // Monospace
        ];

        ReadOnlySpan<int> lowerBases =
        [
            0x1D41A, // Bold
            0x1D44E, // Italic
            0x1D482, // Bold Italic
            0x1D4B6, // Script
            0x1D4EA, // Bold Script
            0x1D51E, // Fraktur
            0x1D552, // Double-Struck
            0x1D586, // Bold Fraktur
            0x1D5BA, // Sans-Serif
            0x1D5EE, // Sans-Serif Bold
            0x1D622, // Sans-Serif Italic
            0x1D656, // Sans-Serif Bold Italic
            0x1D68A, // Monospace
        ];

        foreach (int b in upperBases)
        {
            int off = cp - b;
            if ((uint)off < 26u) return (char)('A' + off);
        }
        foreach (int b in lowerBases)
        {
            int off = cp - b;
            if ((uint)off < 26u) return (char)('a' + off);
        }

        ReadOnlySpan<int> digitBases = [0x1D7CE, 0x1D7D8, 0x1D7E2, 0x1D7EC, 0x1D7F6];
        foreach (int b in digitBases)
        {
            int off = cp - b;
            if ((uint)off < 10u) return (char)('0' + off);
        }

        return null;
    }

    private string NormalizeLifestreamCode(string raw, string serverNameFromApi)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var (first, sep, tail) = SplitFirstToken(raw.Trim());
        string firstLower = first.ToLowerInvariant();

        string result;

        if (!string.IsNullOrEmpty(serverNameFromApi))
        {
            int dist = Levenshtein(firstLower, serverNameFromApi.ToLowerInvariant());

            if (dist == 0)
            {
                result = string.IsNullOrEmpty(tail) ? first : $"{first} {tail}";
            }
            else if (dist <= 2)
            {
                result = string.IsNullOrEmpty(tail)
                    ? serverNameFromApi
                    : $"{serverNameFromApi} {tail}";
            }
            else
            {
                bool firstIsAnyServer = Servers.Values
                    .Any(s => Levenshtein(firstLower, s.Name.ToLowerInvariant()) <= 1);

                if (firstIsAnyServer)
                {
                    result = string.IsNullOrEmpty(tail) ? first : $"{first} {tail}";
                }
                else
                {
                    string tailLower = tail.ToLowerInvariant();
                    var (_, __, lastPart) = SplitLastToken(tail);
                    bool tailEndsWithServer = !string.IsNullOrEmpty(lastPart) &&
                        Levenshtein(lastPart.ToLowerInvariant(), serverNameFromApi.ToLowerInvariant()) <= 2;

                    if (tailEndsWithServer)
                    {
                        // e.g. "Goblet W8 P12 | Mateus" → "Mateus Goblet W8 P12"
                        string locationPart = StripTrailingServer(tail, lastPart);
                        result = string.IsNullOrEmpty(locationPart)
                            ? $"{serverNameFromApi} {first}"
                            : $"{serverNameFromApi} {first} {locationPart}";
                    }
                    else
                    {
                        result = string.IsNullOrEmpty(tail)
                            ? $"{serverNameFromApi} {first}"
                            : $"{serverNameFromApi} {first} {tail}";
                    }
                }
            }
        }
        else
        {
            var best = Servers.Values
                .Select(s => (s.Name, Dist: Levenshtein(firstLower, s.Name.ToLowerInvariant())))
                .Where(x => x.Dist <= 2)
                .OrderBy(x => x.Dist)
                .FirstOrDefault();

            if (best.Name == null || best.Dist == 0)
                result = raw;
            else
                result = string.IsNullOrEmpty(tail)
                    ? best.Name
                    : $"{best.Name} {tail}";
        }

        return CleanLifestreamCode(result);
    }

    private static string CleanLifestreamCode(string s)
    {
        var ri = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bLavender Beds?\b",  "Lavender Beds",  ri);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bMists?\b",           "Mist",           ri);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bThe Goblet\b",       "The Goblet",     ri);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bGoblets?\b",         "The Goblet",     ri);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bEmpyreum\b",         "Empyreum",       ri);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bShirogane\b",        "Shirogane",      ri);

        // expand concatenated ward+plot: W7P5 → W7 P5
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\bW(\d+)P(\d+)\b", "W$1 P$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var sb = new StringBuilder(s.Length);
        bool lastWasSpace = false;
        foreach (char c in s)
        {
            if (c == '/' || c == '|' || c == ':')
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
            }
            else if (c == ' ')
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private static (string head, string sep, string last) SplitLastToken(string s)
    {
        foreach (var sep in new[] { " | ", " / ", " - ", " \u2013 ", ", " })
        {
            int i = s.LastIndexOf(sep, StringComparison.Ordinal);
            if (i >= 0) return (s[..i].Trim(), sep, s[(i + sep.Length)..].Trim());
        }
        int sp = s.LastIndexOf(' ');
        return sp >= 0 ? (s[..sp].Trim(), " ", s[(sp + 1)..].Trim()) : ("", "", s);
    }

    private static string StripTrailingServer(string tail, string serverToken)
    {
        int idx = tail.LastIndexOf(serverToken, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return tail;
        return tail[..idx].Trim(' ', '-', '|', '/', ',', '\u2013');
    }

    private static (string first, string sep, string tail) SplitFirstToken(string s)
    {
        foreach (var sep in new[] { " - ", " \u2013 ", ", ", " / ", " | " })
        {
            int i = s.IndexOf(sep, StringComparison.Ordinal);
            if (i > 0) return (s[..i].Trim(), sep, s[(i + sep.Length)..].Trim());
        }
        int sp = s.IndexOf(' ');
        return sp > 0 ? (s[..sp], " ", s[(sp + 1)..]) : (s, "", "");
    }

    private static int Levenshtein(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        return d[n, m];
    }

    public void Dispose() => _graphQL.Dispose();
}

public record DataCenterInfo(int Id, string Name, int Region);
public record ServerInfo(int Id, string Name, int DataCenterId);
