using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using VenueScope.Helpers;
using VenueScope.Models;
using VenueScope.Services;

namespace VenueScope.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly EventCacheService _cache;
    private readonly PartakeService    _partake;
    private readonly Configuration     _config;
    private readonly Action            _openConfig;
    private readonly EventStringCache  _stringCache = new();
    private readonly EventFilterCache  _filterCache = new();

    private string      _searchText   = string.Empty;
    private TimeFilter  _timeFilter   = TimeFilter.All;
    private EventSource? _sourceFilter = null; // null = all sources

    private enum TimeFilter { All, LiveNow, Today }

    private static readonly string[] Regions = ["Japan", "North America", "Europe", "Oceania"];

    private static readonly Vector4 ColPartake   = new(0.33f, 0.58f, 0.96f, 1f);
    private static readonly Vector4 ColFFXIVenue = new(0.62f, 0.32f, 0.92f, 1f);
    private static readonly Vector4 ColAccent    = new(0.40f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSubtitle  = new(0.55f, 0.55f, 0.65f, 1f);

    public MainWindow(EventCacheService cache, PartakeService partake, Configuration config, Action openConfig)
        : base("VenueScope##main", ImGuiWindowFlags.None)
    {
        _cache      = cache;
        _partake    = partake;
        _config     = config;
        _openConfig = openConfig;

        SizeCondition   = ImGuiCond.FirstUseEver;
        Size            = new Vector2(1050, 620);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 480),
            MaximumSize = new Vector2(1600, 1100),
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();

        // Apply default filters from settings
        _timeFilter = _config.DefaultTimeFilter switch
        {
            1 => TimeFilter.LiveNow,
            2 => TimeFilter.Today,
            _ => TimeFilter.All,
        };
        _sourceFilter = _config.DefaultSourceFilter switch
        {
            0 => EventSource.Partake,
            1 => EventSource.FFXIVenue,
            _ => null,
        };

        if (_cache.LastRefresh != DateTime.MinValue &&
            (DateTime.UtcNow - _cache.LastRefresh).TotalMinutes >= 5)
            Task.Run(_cache.RefreshNowAsync);
    }

    public override void Draw()
    {
        DrawHeader();
        DrawBody();
    }

    // ══ Header ═══════════════════════════════════════════════════════════════

    private void DrawHeader()
    {
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + new Vector2(ImGui.GetContentRegionAvail().X,
                                  ImGui.GetTextLineHeightWithSpacing() * 2.2f);
        ImGui.GetWindowDrawList().AddRectFilled(
            p0, p1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.16f, 1f)));

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * ImGuiHelpers.GlobalScale);
        ImGui.TextColored(ColAccent, "VenueScope");
        ImGui.SameLine(0, 6);
        ImGui.TextColored(ColSubtitle, "FFXIV Community Event Browser");

        float rightSlot = ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(rightSlot - 320f * ImGuiHelpers.GlobalScale + ImGui.GetCursorPosX());

        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.18f, 0.36f, 0.60f, 0.85f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.48f, 0.78f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.34f, 0.60f, 0.96f, 1.00f)))
        {
            if (ImGui.SmallButton("  Reload  "))
            {
                _stringCache.Clear();
                _filterCache.Clear();
                Task.Run(_cache.RefreshNowAsync);
            }
        }

        ImGui.SameLine(0, 10);

        if (_cache.IsRefreshing)
            ImGui.TextColored(new Vector4(1f, 0.82f, 0.20f, 1f), "Loading...");
        else if (_cache.LastError != null)
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Error");
        else if (_cache.LastRefresh != DateTime.MinValue)
            ImGui.TextColored(ColSubtitle, _stringCache.GetLastUpdateString(_cache.LastRefresh.ToLocalTime()));
        else
            ImGui.TextColored(ColSubtitle, "Not loaded yet");

        // Second line: counts on the left, Settings button on the right
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * ImGuiHelpers.GlobalScale);
        var pCount = _cache.CachedEvents.Count(e => e.Source == EventSource.Partake);
        var fCount = _cache.CachedEvents.Count(e => e.Source == EventSource.FFXIVenue);
        ImGui.TextColored(ColPartake,   $"{pCount} Partake");
        ImGui.SameLine(0, 6);
        ImGui.TextColored(ColSubtitle,  "·");
        ImGui.SameLine(0, 6);
        ImGui.TextColored(ColFFXIVenue, $"{fCount} FFXIVenue");

        // Settings button: right-aligned using CalcTextSize for scale-independent positioning
        float btnW = ImGui.CalcTextSize("  Settings  ").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - btnW);
        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.22f, 0.22f, 0.30f, 0.85f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.42f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.38f, 0.38f, 0.52f, 1.00f)))
        {
            if (ImGui.SmallButton("  Settings  ##cfg"))
                _openConfig();
        }

        ImGui.Spacing();
        ImGui.Separator();
    }

    // ══ Body ═════════════════════════════════════════════════════════════════

    private void DrawBody()
    {
        if (_cache.CachedEvents.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColSubtitle,
                _cache.IsRefreshing
                    ? "  Loading events, please wait..."
                    : "  No events found. Click Reload to try again.");
            return;
        }

        // Source filter pills
        ImGui.Spacing();
        DrawSourceFilterButton("All",        null);
        ImGui.SameLine(0, 4);
        DrawSourceFilterButton("Partake",    EventSource.Partake);
        ImGui.SameLine(0, 4);
        DrawSourceFilterButton("FFXIVenue",  EventSource.FFXIVenue);
        ImGui.Spacing();
        ImGui.Separator();

        // Region tabs (unchanged from working version)
        using var tabs = ImRaii.TabBar("##regions");
        if (!tabs.Success) return;

        for (int regionIdx = 1; regionIdx <= Regions.Length; regionIdx++)
        {
            var regionName = Regions[regionIdx - 1];

            var flags = ImGuiTabItemFlags.None;
            if (!_config.SelectedRegionSet && _config.SelectedRegion == regionName)
            {
                flags |= ImGuiTabItemFlags.SetSelected;
                _config.SelectedRegionSet = true;
            }

            var open = true;
            using var tab = ImRaii.TabItem(regionName, ref open, flags);
            if (!tab.Success) continue;

            if (_config.SelectedRegion != regionName)
            {
                _config.SelectedRegion = regionName;
                _config.Save();
            }

            DrawRegion(regionIdx);
        }
    }

    private void DrawSourceFilterButton(string label, EventSource? source)
    {
        bool active = _sourceFilter == source;

        Vector4 accentColor = source switch
        {
            EventSource.Partake   => ColPartake,
            EventSource.FFXIVenue => ColFFXIVenue,
            _                     => ColAccent,
        };

        var bg  = active ? accentColor with { W = 0.80f } : new Vector4(0.18f, 0.18f, 0.26f, 0.85f);
        var bgh = active ? accentColor with { W = 1.00f } : new Vector4(0.26f, 0.26f, 0.36f, 1.00f);
        var txt = active ? Vector4.One : new Vector4(0.65f, 0.65f, 0.70f, 1f);

        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        bg);
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, bgh);
        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  accentColor);
        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          txt);

        if (ImGui.SmallButton($" {label} ##sf{source?.ToString() ?? "all"}"))
        {
            _sourceFilter = source;
            _filterCache.Clear();
            // Clear source-keyed tag entries so they rebuild from the new event set
            var keysToRemove = _cache.TagsByDc.Keys
                .Where(k => k.Contains('_') && (k.EndsWith("_Partake") || k.EndsWith("_FFXIVenue")))
                .ToList();
            foreach (var k in keysToRemove) _cache.TagsByDc.Remove(k);
        }
    }

    // ══ Region / DC ══════════════════════════════════════════════════════════

    private void DrawRegion(int regionIdx)
    {
        var dcs = _partake.DataCenters.Values
            .Where(dc => dc.Region == regionIdx)
            .OrderBy(dc => dc.Name)
            .ToList();

        if (dcs.Count == 0)
        {
            ImGui.TextColored(ColSubtitle, "No data centers found for this region.");
            return;
        }

        var regionEvents = dcs
            .SelectMany(dc => _cache.EventsByDc.GetValueOrDefault(dc.Name) ?? new List<VenueEvent>())
            .Where(e => _sourceFilter == null || e.Source == _sourceFilter)
            .OrderBy(e => e.StartTime)
            .ToList();

        var noLocEvents = (_cache.EventsByDc.GetValueOrDefault("_no_location_") ?? new List<VenueEvent>())
            .Where(e => _sourceFilter == null || e.Source == _sourceFilter)
            .ToList();

        using var dcTabs = ImRaii.TabBar($"##dc_{regionIdx}");
        if (!dcTabs.Success) return;

        // All tab
        var allKey   = $"_all_{regionIdx}";
        var allFlags = ImGuiTabItemFlags.None;
        if (!_config.SelectedDataCenterSet && _config.SelectedDataCenter == allKey)
        {
            allFlags |= ImGuiTabItemFlags.SetSelected;
            _config.SelectedDataCenterSet = true;
        }
        var allOpen = true;
        using (var allTab = ImRaii.TabItem($"All ({regionEvents.Count})##all{regionIdx}", ref allOpen, allFlags))
        {
            if (allTab.Success)
            {
                _config.SelectedDataCenter = allKey;
                DrawDataCenter(allKey, regionEvents);
            }
        }

        // Per-DC tabs
        foreach (var dc in dcs)
        {
            var dcEvents = (_cache.EventsByDc.GetValueOrDefault(dc.Name) ?? new List<VenueEvent>())
                .Where(e => _sourceFilter == null || e.Source == _sourceFilter)
                .ToList();

            int count = dcEvents.Count;
            var label = count > 0
                ? $"{dc.Name} ({count})##{dc.Name}"
                : $"{dc.Name}##{dc.Name}";

            var flags = ImGuiTabItemFlags.None;
            if (!_config.SelectedDataCenterSet && _config.SelectedDataCenter == dc.Name)
            {
                flags |= ImGuiTabItemFlags.SetSelected;
                _config.SelectedDataCenterSet = true;
            }

            var open = true;
            using var dcTab = ImRaii.TabItem(label, ref open, flags);
            if (!dcTab.Success) continue;

            _config.SelectedDataCenter = dc.Name;
            DrawDataCenter(dc.Name, dcEvents);
        }

        // No Location tab
        if (noLocEvents.Count > 0)
        {
            var open = true;
            using var tab = ImRaii.TabItem($"No Location ({noLocEvents.Count})##noloc{regionIdx}", ref open);
            if (tab.Success)
                DrawDataCenter("_no_location_", noLocEvents);
        }
    }

    private void DrawDataCenter(string dcKey, List<VenueEvent> events)
    {
        // Apply HideEndedEvents before anything else
        if (_config.HideEndedEvents)
        {
            var now = DateTime.UtcNow;
            events = events.Where(e => e.EndTime == null || e.EndTime.Value.ToUniversalTime() > now).ToList();
        }

        // Include source filter in cache key so tags stay source-specific
        var cacheKey = _sourceFilter == null ? dcKey : $"{dcKey}_{_sourceFilter}";

        if (!_cache.TagsByDc.TryGetValue(cacheKey, out var tags))
        {
            tags = new SortedDictionary<string, bool>();
            foreach (var ev in events)
                foreach (var tag in ev.Tags)
                    if (!tags.ContainsKey(tag))
                        tags[tag] = false;
            _cache.TagsByDc[cacheKey] = tags;
        }

        ImGui.Spacing();
        DrawFilterBar(cacheKey, tags);

        var timeCount = ApplyTimeFilter(events).Count;
        var summary   = _timeFilter != TimeFilter.All
            ? $"  Showing {timeCount} of {events.Count} events"
            : $"  {events.Count} event{(events.Count != 1 ? "s" : "")}";
        ImGui.TextColored(ColSubtitle, summary);
        ImGui.Separator();

        if (events.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColSubtitle, "  No events on this data center.");
            return;
        }

        var selectedTags   = tags.Where(t => t.Value).Select(t => t.Key).ToList();
        var tagFiltered    = _filterCache.GetFiltered(cacheKey, events, selectedTags);
        var timeFiltered   = ApplyTimeFilter(tagFiltered);
        var searchFiltered = ApplySearch(timeFiltered);

        if (searchFiltered.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColSubtitle, "  No events match the current filters.");
            return;
        }

        using var child = ImRaii.Child($"##ev_{cacheKey}", Vector2.Zero, false);
        if (!child.Success) return;

        foreach (var ev in searchFiltered)
        {
            ImGui.Spacing();
            ImGui.PushID(ev.Id);
            EventRenderer.DrawEventCard(ev, _stringCache.GetOrCompute(ev));
            ImGui.PopID();
            ImGui.Spacing();

            var dl  = ImGui.GetWindowDrawList();
            var sep = ImGui.GetCursorScreenPos();
            dl.AddLine(
                new Vector2(sep.X + 20f, sep.Y),
                new Vector2(sep.X + ImGui.GetContentRegionAvail().X - 20f, sep.Y),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.25f, 0.32f, 1f)));
            ImGui.Dummy(new Vector2(0, 2f));
        }
    }

    // ══ Filter bar ═══════════════════════════════════════════════════════════

    private const int TagInlineCap = 8; // above this, use popup instead of inline pills

    private void DrawFilterBar(string dcKey, SortedDictionary<string, bool> tags)
    {
        DrawTimeFilterButton("All",      TimeFilter.All);     ImGui.SameLine(0, 4);
        DrawTimeFilterButton("Live Now", TimeFilter.LiveNow); ImGui.SameLine(0, 4);
        DrawTimeFilterButton("Today",    TimeFilter.Today);

        ImGui.SameLine(0, 16);

        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.14f, 0.20f, 1f)))
            ImGui.InputTextWithHint("##search", "Search events...", ref _searchText, 128);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            ImGui.SameLine(0, 6);
            using var col = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.60f, 0.60f, 0.65f, 1f));
            if (ImGui.SmallButton("x##clrsearch")) _searchText = string.Empty;
        }

        if (tags.Count == 0) return;

        if (tags.Count <= TagInlineCap)
        {
            // ── Inline pills (Partake — few tags) ────────────────────────────
            ImGui.SameLine(0, 18);
            ImGui.TextColored(ColSubtitle, "Tags:");
            int i = 0;
            foreach (var tag in tags.Keys.ToList())
            {
                ImGui.SameLine(0, i == 0 ? 6 : 4);
                DrawTagTogglePill(tag, tags, dcKey, i);
                i++;
            }
        }
        else
        {
            // ── Popup button (FFXIVenue — many tags) ─────────────────────────
            DrawTagPopupButton(dcKey, tags);
        }
    }

    private void DrawTagPopupButton(string dcKey, SortedDictionary<string, bool> tags)
    {
        ImGui.SameLine(0, 18);

        int activeCount = tags.Values.Count(v => v);
        var popupId = $"##tagpopup_{dcKey}";

        var label = activeCount > 0
            ? $" Tags  ({activeCount} active) ##tagbtn_{dcKey}"
            : $" Tags  ({tags.Count}) ##tagbtn_{dcKey}";

        var bg = activeCount > 0
            ? new Vector4(0.28f, 0.55f, 0.95f, 0.85f)
            : new Vector4(0.20f, 0.20f, 0.28f, 0.85f);
        var bgh = bg with { W = 1f };

        using (ImRaii.PushColor(ImGuiCol.Button,        bg))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, bgh))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  bgh))
        {
            if (ImGui.SmallButton(label))
                ImGui.OpenPopup(popupId);
        }

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(300f * ImGuiHelpers.GlobalScale, 80f  * ImGuiHelpers.GlobalScale),
            new Vector2(520f * ImGuiHelpers.GlobalScale, 400f * ImGuiHelpers.GlobalScale));

        using var popup = ImRaii.Popup(popupId);
        if (!popup.Success) return;

        ImGui.TextColored(ColSubtitle, $"Filter by tag  —  {tags.Count} available");
        ImGui.Separator();
        ImGui.Spacing();

        if (activeCount > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.45f, 0.15f, 0.15f, 0.80f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.60f, 0.20f, 0.20f, 1.00f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.70f, 0.25f, 0.25f, 1.00f)))
            {
                if (ImGui.SmallButton("  Clear all  ##clrtags"))
                {
                    foreach (var k in tags.Keys.ToList()) tags[k] = false;
                    _filterCache.Clear();
                }
            }
            ImGui.Spacing();
        }

        // Tags as wrapping pills — 3 per row
        var tagKeys = tags.Keys.ToList();
        float popupWidth    = ImGui.GetContentRegionAvail().X;
        float pillEstimate  = 110f * ImGuiHelpers.GlobalScale;
        int   cols          = Math.Max(1, (int)(popupWidth / pillEstimate));

        for (int i = 0; i < tagKeys.Count; i++)
        {
            if (i % cols != 0) ImGui.SameLine(0, 6);
            DrawTagTogglePill(tagKeys[i], tags, dcKey, i);
        }

        ImGui.Spacing();
    }

    private void DrawTagTogglePill(string tag, SortedDictionary<string, bool> tags, string dcKey, int i)
    {
        var sel = tags[tag];
        var bg  = sel ? new Vector4(0.28f, 0.55f, 0.95f, 0.85f) : new Vector4(0.20f, 0.20f, 0.28f, 0.85f);
        var bgh = sel ? new Vector4(0.38f, 0.65f, 1.00f, 1.00f) : new Vector4(0.28f, 0.28f, 0.38f, 1.00f);

        using (ImRaii.PushColor(ImGuiCol.Button,        bg))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, bgh))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  bgh))
        {
            if (ImGui.SmallButton($" {tag} ##t{dcKey}{i}"))
            {
                tags[tag] = !tags[tag];
                _filterCache.Clear();
            }
        }
    }

    private void DrawTimeFilterButton(string label, TimeFilter filter)
    {
        bool active = _timeFilter == filter;
        var bgOn    = new Vector4(0.22f, 0.50f, 0.88f, 1.00f);
        var bgOff   = new Vector4(0.18f, 0.18f, 0.26f, 0.90f);
        var bgHov   = active ? bgOn with { W = 0.85f } : new Vector4(0.26f, 0.26f, 0.36f, 1f);

        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        active ? bgOn : bgOff);
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, bgHov);
        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  bgOn);

        if (ImGui.SmallButton($" {label} ##tf{(int)filter}"))
            _timeFilter = filter;
    }

    // ══ Filtering ════════════════════════════════════════════════════════════

    private List<VenueEvent> ApplyTimeFilter(List<VenueEvent> events)
    {
        var utcNow    = DateTime.UtcNow;
        var todayDate = DateTime.Now.Date;

        return _timeFilter switch
        {
            TimeFilter.LiveNow => events.Where(e =>
            {
                var s  = e.StartTime.ToUniversalTime();
                var en = e.EndTime?.ToUniversalTime();
                return s <= utcNow && (en == null || en.Value > utcNow);
            }).ToList(),

            TimeFilter.Today => events.Where(e =>
            {
                var startDate = e.StartTime.ToLocalTime().Date;
                var endDate   = e.EndTime?.ToLocalTime().Date;
                return startDate == todayDate ||
                       (endDate.HasValue && startDate <= todayDate && endDate.Value >= todayDate);
            }).ToList(),

            _ => events,
        };
    }

    private List<VenueEvent> ApplySearch(List<VenueEvent> events)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return events;
        var q = _searchText.Trim();
        return events.Where(e =>
            e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)          ||
            e.Host.Contains(q, StringComparison.OrdinalIgnoreCase)           ||
            e.InGameLocation.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    public void Dispose() { }
}
