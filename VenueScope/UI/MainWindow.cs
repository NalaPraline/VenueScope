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

    private string          _searchText    = string.Empty;
    private TimeFilter      _timeFilter    = TimeFilter.All;
    private EventSource?    _sourceFilter  = null;
    private HashSet<string> _selectedDcKeys = new(); // empty = All Data Centers

    private enum TimeFilter { All = 0, LiveNow = 1, Today = 2, Upcoming = 3 }

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Vector4 ColPartake   = new(0.33f, 0.58f, 0.96f, 1f);
    private static readonly Vector4 ColFFXIVenue = new(0.62f, 0.32f, 0.92f, 1f);
    private static readonly Vector4 ColAccent    = new(0.40f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSubtitle  = new(0.50f, 0.50f, 0.60f, 1f);
    private static readonly Vector4 ColSidebarBg = new(0.09f, 0.09f, 0.14f, 1f);
    private static readonly Vector4 ColTimeLive  = new(0.20f, 0.86f, 0.42f, 1f);
    private static readonly Vector4 ColTimeToday = new(0.52f, 0.78f, 1.00f, 1f);
    private static readonly Vector4 ColTimeUpcom = new(1.00f, 0.72f, 0.28f, 1f);
    private static readonly Vector4 ColDivider   = new(0.22f, 0.22f, 0.32f, 1f);

    private const float SidebarW = 165f; // unscaled px

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

        // Restore last DC selection (multi); fall back to legacy single if needed
        _selectedDcKeys = new HashSet<string>(_config.SelectedDataCenters);
        if (_selectedDcKeys.Count == 0 && !string.IsNullOrEmpty(_config.SelectedDataCenter))
            _selectedDcKeys.Add(_config.SelectedDataCenter);

        if (_cache.LastRefresh != DateTime.MinValue &&
            (DateTime.UtcNow - _cache.LastRefresh).TotalMinutes >= 5)
            Task.Run(_cache.RefreshNowAsync);
    }

    public override void Draw()
    {
        float gs = ImGuiHelpers.GlobalScale;

        DrawTopBar();
        ImGui.Separator();

        // ── Left sidebar (darker background, no scroll) ───────────────────────
        {
            using var color   = ImRaii.PushColor(ImGuiCol.ChildBg, ColSidebarBg);
            using var sidebar = ImRaii.Child("##sidebar", new Vector2(SidebarW * gs, 0f), false,
                                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (sidebar.Success) DrawSidebar();
        }

        ImGui.SameLine(0, 0);

        // Vertical divider
        var lp0 = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(
            lp0, lp0 + new Vector2(0f, ImGui.GetContentRegionAvail().Y),
            ImGui.ColorConvertFloat4ToU32(ColDivider), 1f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 1f);

        // ── Main content ──────────────────────────────────────────────────────
        using var main = ImRaii.Child("##maincontent", Vector2.Zero, false);
        if (main.Success) DrawMainContent();
    }

    // ══ Top Bar ══════════════════════════════════════════════════════════════

    private void DrawTopBar()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);

        ImGui.TextColored(ColAccent, "VenueScope");
        ImGui.SameLine(0, 8);
        ImGui.TextColored(ColDivider, "—");
        ImGui.SameLine(0, 8);

        DrawDcCombo();

        ImGui.SameLine(0, 8);

        ImGui.SetNextItemWidth(200f * gs);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.14f, 0.20f, 1f)))
            ImGui.InputTextWithHint("##search", "Search events...", ref _searchText, 128);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            ImGui.SameLine(0, 4);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ColSubtitle);
            if (ImGui.SmallButton("x##clrsearch")) _searchText = string.Empty;
        }

        // Right side
        float btnReloadW   = ImGui.CalcTextSize("  Reload  ").X  + ImGui.GetStyle().FramePadding.X * 2;
        float btnSettingsW = ImGui.CalcTextSize("  Settings  ").X + ImGui.GetStyle().FramePadding.X * 2;
        float statusW      = 120f * gs;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - btnReloadW - btnSettingsW - statusW - 12f * gs);

        if (_cache.IsRefreshing)
            ImGui.TextColored(new Vector4(1f, 0.82f, 0.20f, 1f), "Loading...");
        else if (_cache.LastRefresh != DateTime.MinValue)
            ImGui.TextColored(ColSubtitle, _stringCache.GetLastUpdateString(_cache.LastRefresh.ToLocalTime()));
        else
            ImGui.TextColored(ColSubtitle, "Not loaded");

        ImGui.SameLine(0, 8);

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

        ImGui.SameLine(0, 6);

        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.22f, 0.22f, 0.30f, 0.85f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.30f, 0.42f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.38f, 0.38f, 0.52f, 1.00f)))
        {
            if (ImGui.SmallButton("  Settings  ##cfg"))
                _openConfig();
        }

        ImGui.Spacing();
    }

    private string DcComboLabel => _selectedDcKeys.Count switch
    {
        0 => "All Data Centers",
        1 => _selectedDcKeys.First(),
        _ => $"{_selectedDcKeys.Count} Data Centers",
    };

    private void DrawDcCombo()
    {
        float gs = ImGuiHelpers.GlobalScale;

        ImGui.SetNextItemWidth(210f * gs);
        using (ImRaii.PushColor(ImGuiCol.FrameBg,  new Vector4(0.14f, 0.14f, 0.20f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.PopupBg,  new Vector4(0.12f, 0.12f, 0.18f, 1f)))
        {
            if (!ImGui.BeginCombo("##dccombo", DcComboLabel)) return;

            bool allSel = _selectedDcKeys.Count == 0;
            if (ImGui.Selectable("All Data Centers", allSel, ImGuiSelectableFlags.DontClosePopups))
                ClearDcSelection();
            if (allSel) ImGui.SetItemDefaultFocus();

            ImGui.Separator();

            var regions = new (int idx, string name)[]
            {
                (1, "Japan"), (2, "North America"), (3, "Europe"), (4, "Oceania"),
            };

            foreach (var (regionIdx, regionName) in regions)
            {
                var dcs = _partake.DataCenters.Values
                    .Where(dc => dc.Region == regionIdx)
                    .OrderBy(dc => dc.Name)
                    .ToList();
                if (dcs.Count == 0) continue;

                using (ImRaii.PushColor(ImGuiCol.Text, ColSubtitle))
                    ImGui.TextUnformatted($"  {regionName}");

                foreach (var dc in dcs)
                {
                    bool ticked = _selectedDcKeys.Contains(dc.Name);
                    if (ImGui.Checkbox($"  {dc.Name}##{dc.Name}dc", ref ticked))
                        ToggleDc(dc.Name);
                }
            }

            ImGui.EndCombo();
        }
    }

    private void ToggleDc(string dcName)
    {
        if (!_selectedDcKeys.Remove(dcName))
            _selectedDcKeys.Add(dcName);
        _filterCache.Clear();
        _config.SelectedDataCenters = _selectedDcKeys.ToList();
        _config.Save();
    }

    private void ClearDcSelection()
    {
        _selectedDcKeys.Clear();
        _filterCache.Clear();
        _config.SelectedDataCenters = new List<string>();
        _config.Save();
    }

    // ══ Sidebar ══════════════════════════════════════════════════════════════

    private void DrawSidebar()
    {
        float gs = ImGuiHelpers.GlobalScale;
        float w  = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();

        // ── BROWSE ────────────────────────────────────────────────────────────
        DrawSidebarLabel("BROWSE");
        DrawSidebarTimeItem("● Live Now",  TimeFilter.LiveNow,  ColTimeLive);
        DrawSidebarTimeItem("  Today",     TimeFilter.Today,    ColTimeToday);
        DrawSidebarTimeItem("  Upcoming",  TimeFilter.Upcoming, ColTimeUpcom);
        DrawSidebarTimeItem("  All",       TimeFilter.All,      ColSubtitle);

        ImGui.Spacing();
        DrawSidebarRule();
        ImGui.Spacing();

        // ── SOURCE ────────────────────────────────────────────────────────────
        DrawSidebarLabel("SOURCE");
        DrawSidebarSourceItem("  All Sources",    null,                  ColAccent);
        DrawSidebarSourceItem("  Partake.gg",     EventSource.Partake,   ColPartake);
        DrawSidebarSourceItem("  FFXIVenues.com", EventSource.FFXIVenue, ColFFXIVenue);

        ImGui.Spacing();
        DrawSidebarRule();
        ImGui.Spacing();

        // ── TAGS ──────────────────────────────────────────────────────────────
        DrawSidebarLabel("TAGS");
        ImGui.Spacing();
        DrawSidebarTags(w);
    }

    private void DrawSidebarLabel(string text)
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);
        using var _ = ImRaii.PushColor(ImGuiCol.Text, ColSubtitle with { W = 0.45f });
        ImGui.TextUnformatted(text);
    }

    private void DrawSidebarRule()
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   p0 = ImGui.GetCursorScreenPos() + new Vector2(6f * gs, 0f);
        var   p1 = p0 + new Vector2(ImGui.GetContentRegionAvail().X - 12f * gs, 1f);
        ImGui.GetWindowDrawList().AddRectFilled(p0, p1, ImGui.ColorConvertFloat4ToU32(ColDivider));
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private void DrawSidebarTimeItem(string label, TimeFilter filter, Vector4 color)
    {
        bool  active = _timeFilter == filter;
        float gs     = ImGuiHelpers.GlobalScale;
        float w      = ImGui.GetContentRegionAvail().X;
        float h      = ImGui.GetTextLineHeightWithSpacing();

        if (active)
        {
            var p0 = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p0 + new Vector2(w, h),
                ImGui.ColorConvertFloat4ToU32(color with { W = 0.12f }));
            dl.AddRectFilled(p0, p0 + new Vector2(3f * gs, h),
                ImGui.ColorConvertFloat4ToU32(color with { W = 0.85f }));
        }

        using (ImRaii.PushColor(ImGuiCol.Text,          active ? color : ColSubtitle with { W = 0.65f }))
        using (ImRaii.PushColor(ImGuiCol.Header,        Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.HeaderHovered, color with { W = 0.08f }))
        {
            if (ImGui.Selectable($"{label}##tf{(int)filter}", active,
                    ImGuiSelectableFlags.None, new Vector2(w, 0f)))
                _timeFilter = filter;
        }
    }

    private void DrawSidebarSourceItem(string label, EventSource? source, Vector4 color)
    {
        bool  active = _sourceFilter == source;
        float gs     = ImGuiHelpers.GlobalScale;
        float w      = ImGui.GetContentRegionAvail().X;
        float h      = ImGui.GetTextLineHeightWithSpacing();

        if (active)
        {
            var p0 = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p0 + new Vector2(w, h),
                ImGui.ColorConvertFloat4ToU32(color with { W = 0.12f }));
            dl.AddRectFilled(p0, p0 + new Vector2(3f * gs, h),
                ImGui.ColorConvertFloat4ToU32(color with { W = 0.85f }));
        }

        using (ImRaii.PushColor(ImGuiCol.Text,          active ? color : ColSubtitle with { W = 0.65f }))
        using (ImRaii.PushColor(ImGuiCol.Header,        Vector4.Zero))
        using (ImRaii.PushColor(ImGuiCol.HeaderHovered, color with { W = 0.08f }))
        {
            if (ImGui.Selectable($"{label}##src{source?.ToString() ?? "all"}", active,
                    ImGuiSelectableFlags.None, new Vector2(w, 0f)))
            {
                _sourceFilter = source;
                _filterCache.Clear();
            }
        }
    }

    private void DrawSidebarTags(float sidebarW)
    {
        float  gs       = ImGuiHelpers.GlobalScale;
        string cacheKey = BuildCacheKey();
        EnsureTagsBuilt(cacheKey);

        if (!_cache.TagsByDc.TryGetValue(cacheKey, out var tags) || tags.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ColSubtitle with { W = 0.30f });
            ImGui.TextUnformatted("None");
            return;
        }

        int activeCount = tags.Values.Count(v => v);
        if (activeCount > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4f * gs);
            using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.45f, 0.12f, 0.12f, 0.60f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.60f, 0.18f, 0.18f, 0.90f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.70f, 0.22f, 0.22f, 1.00f)))
            {
                if (ImGui.SmallButton("  Clear  ##clrtags"))
                {
                    foreach (var k in tags.Keys.ToList()) tags[k] = false;
                    _filterCache.Clear();
                }
            }
            ImGui.Spacing();
        }

        // Scrollable tag area (takes remaining sidebar height)
        float tagAreaH = ImGui.GetContentRegionAvail().Y - 4f * gs;
        using var tagChild = ImRaii.Child("##tagslist", new Vector2(0f, tagAreaH), false);
        if (!tagChild.Success) return;

        var tagKeys = tags.Keys.ToList();
        for (int i = 0; i < tagKeys.Count; i++)
        {
            var tag = tagKeys[i];
            var sel = tags[tag];
            var col = EventRenderer.GetTagColor(tag);
            var bg  = sel ? col with { W = 0.45f } : col with { W = 0.22f };
            var bgh = sel ? col with { W = 0.62f } : col with { W = 0.36f };
            var txt = sel ? col with { W = 1.00f } : col with { W = 0.85f };

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4f * gs);
            using (ImRaii.PushColor(ImGuiCol.Button,        bg))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, bgh))
            using (ImRaii.PushColor(ImGuiCol.ButtonActive,  col with { W = 0.55f }))
            using (ImRaii.PushColor(ImGuiCol.Text,          txt))
            {
                if (ImGui.SmallButton($" {TruncTag(tag, 15)} ##st{cacheKey}{i}"))
                {
                    tags[tag] = !tags[tag];
                    _filterCache.Clear();
                }
                if (ImGui.IsItemHovered() && tag.Length > 15)
                    ImGui.SetTooltip(tag);
            }
        }
    }

    // ══ Main Content ══════════════════════════════════════════════════════════

    private void DrawMainContent()
    {
        float gs = ImGuiHelpers.GlobalScale;

        var baseEvents = GetBaseEvents();
        if (_config.HideEndedEvents)
        {
            var now = DateTime.UtcNow;
            baseEvents = baseEvents.Where(e => e.EndTime == null || e.EndTime.Value.ToUniversalTime() > now).ToList();
        }

        if (baseEvents.Count == 0 && _cache.CachedEvents.Count == 0)
        {
            ImGui.Spacing();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);
            ImGui.TextColored(ColSubtitle,
                _cache.IsRefreshing
                    ? "Loading events, please wait..."
                    : "No events found. Click Reload to try again.");
            return;
        }

        string cacheKey = BuildCacheKey();
        EnsureTagsBuilt(cacheKey);
        _cache.TagsByDc.TryGetValue(cacheKey, out var tags);

        var selectedTags = tags?.Where(t => t.Value).Select(t => t.Key).ToList() ?? new List<string>();
        var tagFiltered  = _filterCache.GetFiltered(cacheKey, baseEvents, selectedTags);
        var timeFiltered = ApplyTimeFilter(tagFiltered);
        var searched     = ApplySearch(timeFiltered);

        // ── Summary ───────────────────────────────────────────────────────────
        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);

        string dcLabel = DcComboLabel;
        string tfLabel = _timeFilter switch
        {
            TimeFilter.LiveNow  => "Live Now",
            TimeFilter.Today    => "Today",
            TimeFilter.Upcoming => "Upcoming",
            _                   => "All",
        };
        string srcLabel = _sourceFilter switch
        {
            EventSource.Partake   => "  ·  Partake",
            EventSource.FFXIVenue => "  ·  FFXIVenues",
            _                     => string.Empty,
        };
        ImGui.TextColored(ColSubtitle,
            $"{dcLabel}  ·  {tfLabel}{srcLabel}  ·  {searched.Count} event{(searched.Count != 1 ? "s" : "")}");

        ImGui.Spacing();

        if (searched.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);
            ImGui.TextColored(ColSubtitle,
                baseEvents.Count == 0
                    ? "No events on this data center."
                    : "No events match the current filters.");
            return;
        }

        // ── Event list ────────────────────────────────────────────────────────
        using var child = ImRaii.Child("##evlist", Vector2.Zero, false);
        if (!child.Success) return;

        foreach (var ev in searched)
        {
            ImGui.PushID(ev.Id);
            EventRenderer.DrawEventCard(ev, _stringCache.GetOrCompute(ev));
            ImGui.PopID();
            ImGui.Dummy(new Vector2(0f, 5f * gs));
        }
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private string BuildCacheKey()
    {
        string dcPart  = _selectedDcKeys.Count == 0
            ? "_global_all"
            : string.Join(",", _selectedDcKeys.OrderBy(x => x));
        string srcPart = _sourceFilter?.ToString() ?? "all";
        return $"{dcPart}|{srcPart}";
    }

    private void EnsureTagsBuilt(string cacheKey)
    {
        if (_cache.TagsByDc.ContainsKey(cacheKey)) return;
        var tags = new SortedDictionary<string, bool>();
        foreach (var ev in GetBaseEvents())
            foreach (var tag in ev.Tags)
                if (!tags.ContainsKey(tag))
                    tags[tag] = false;
        _cache.TagsByDc[cacheKey] = tags;
    }

    private List<VenueEvent> GetBaseEvents()
    {
        IEnumerable<VenueEvent> events;

        if (_selectedDcKeys.Count == 0)
            events = _cache.CachedEvents;
        else if (_selectedDcKeys.Count == 1)
            events = _cache.EventsByDc.GetValueOrDefault(_selectedDcKeys.First()) ?? Enumerable.Empty<VenueEvent>();
        else
            events = _selectedDcKeys.SelectMany(dc =>
                _cache.EventsByDc.GetValueOrDefault(dc) ?? Enumerable.Empty<VenueEvent>());

        if (_sourceFilter != null)
            events = events.Where(e => e.Source == _sourceFilter);

        return events.OrderBy(e => e.StartTime).ToList();
    }

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

            TimeFilter.Upcoming => events.Where(e => e.StartTime.ToUniversalTime() > utcNow).ToList(),

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

    private static string TruncTag(string tag, int maxLen) =>
        tag.Length <= maxLen ? tag : tag[..(maxLen - 1)] + "\u2026";

    public void Dispose() { }
}
