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

/// <summary>
/// Fetches FFXIV events from Partake.gg via the official GraphQL API.
/// Uses pagination (100 per page) to retrieve ALL events.
/// DC and server data is read from Lumina (live game data).
/// </summary>
public class PartakeService : IDisposable
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
            var title       = SanitizeTitle(ev.Title ?? string.Empty);
            var description = SanitizeTitle(ev.Description ?? string.Empty);

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

    /// <summary>
    /// Sanitizes a title for ImGui display:
    ///   - Maps fullwidth ASCII (ＡＢＣ) → regular ASCII (ABC)
    ///   - Maps mathematical Unicode letters (𝒜𝓑𝕮 etc.) → ASCII equivalents
    ///   - Keeps all other BMP characters (accented Latin, CJK, symbols…)
    ///   - Drops unmappable supplementary-plane characters (emoji, unknown symbols)
    /// </summary>
    private static string SanitizeTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            // Fullwidth ASCII (U+FF01–U+FF5E) → regular ASCII (U+0021–U+007E)
            if (c >= '\uFF01' && c <= '\uFF5E')
            {
                sb.Append((char)(c - 0xFF01 + 0x21));
                continue;
            }

            // Surrogate pair = supplementary-plane character (U+10000+)
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int cp = char.ConvertToUtf32(c, s[i + 1]);
                i++; // consume low surrogate
                char? mapped = MathLetterToAscii(cp);
                if (mapped.HasValue)
                    sb.Append(mapped.Value);
                // else: discard (emoji, unknown supplementary character)
                continue;
            }

            // BMP character — keep as-is
            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Maps a codepoint in the Mathematical Alphanumeric Symbols block
    /// (U+1D400–U+1D7FF) to its ASCII equivalent letter or digit.
    /// Returns null for codepoints outside that block.
    /// </summary>
    private static char? MathLetterToAscii(int cp)
    {
        // Uppercase base codepoints (where 'A' lives) for each math style
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

        // Lowercase base codepoints (where 'a' lives) — base + 26 within each 52-char block
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

        // Mathematical digit styles (bold, double-struck, sans, sans-bold, monospace)
        ReadOnlySpan<int> digitBases = [0x1D7CE, 0x1D7D8, 0x1D7E2, 0x1D7EC, 0x1D7F6];
        foreach (int b in digitBases)
        {
            int off = cp - b;
            if ((uint)off < 10u) return (char)('0' + off);
        }

        return null;
    }

    // ── Lifestream code normalization ─────────────────────────────────────────

    /// <summary>
    /// Tries to correct the server name in a free-text Lifestream location string.
    /// Priority: use the validated server name from locationData (Partake dropdown).
    /// Fallback: fuzzy-match the first token against the Lumina server list.
    /// </summary>
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
                // Already correct server at start — just clean up the tail
                result = string.IsNullOrEmpty(tail) ? first : $"{first} {tail}";
            }
            else if (dist <= 2)
            {
                // Typo at start — replace with correct spelling
                result = string.IsNullOrEmpty(tail)
                    ? serverNameFromApi
                    : $"{serverNameFromApi} {tail}";
            }
            else
            {
                // First token doesn't match the authoritative server name.
                bool firstIsAnyServer = Servers.Values
                    .Any(s => Levenshtein(firstLower, s.Name.ToLowerInvariant()) <= 1);

                if (firstIsAnyServer)
                {
                    // Looks like a valid (different) server — keep it, just clean up
                    result = string.IsNullOrEmpty(tail) ? first : $"{first} {tail}";
                }
                else
                {
                    // Check if the server name is hiding at the end of the tail
                    string tailLower = tail.ToLowerInvariant();
                    var (_, __, lastPart) = SplitLastToken(tail);
                    bool tailEndsWithServer = !string.IsNullOrEmpty(lastPart) &&
                        Levenshtein(lastPart.ToLowerInvariant(), serverNameFromApi.ToLowerInvariant()) <= 2;

                    if (tailEndsWithServer)
                    {
                        // e.g. "Goblet W8 P12 | Mateus" — reorder to "Mateus Goblet W8 P12"
                        string locationPart = StripTrailingServer(tail, lastPart);
                        result = string.IsNullOrEmpty(locationPart)
                            ? $"{serverNameFromApi} {first}"
                            : $"{serverNameFromApi} {first} {locationPart}";
                    }
                    else
                    {
                        // No server found anywhere — prepend it
                        result = string.IsNullOrEmpty(tail)
                            ? $"{serverNameFromApi} {first}"
                            : $"{serverNameFromApi} {first} {tail}";
                    }
                }
            }
        }
        else
        {
            // No authoritative name — fuzzy match the first token
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

    /// <summary>Removes stray / and | characters left over after normalization.</summary>
    private static string CleanLifestreamCode(string s)
    {
        // Expand concatenated ward+plot (W7P5 → W7 P5) before other cleanup
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\bW(\d+)P(\d+)\b", "W$1 P$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Replace separator characters with a space, then collapse runs of whitespace
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

    /// <summary>Splits off the last token from a string using the same separator set.</summary>
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

    /// <summary>Removes the trailing server token from a location string.</summary>
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

// Lumina-derived info types
public record DataCenterInfo(int Id, string Name, int Region);
public record ServerInfo(int Id, string Name, int DataCenterId);
