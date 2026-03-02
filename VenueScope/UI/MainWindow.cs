using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
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
    private readonly EventStringCache          _stringCache     = new();
    private readonly EventFilterCache          _filterCache     = new();
    private readonly Dictionary<string, float> _cardHeightCache = new();
    private float _lastContentWidth = 0f;

    private string          _searchText     = string.Empty;
    private TimeFilter      _timeFilter     = TimeFilter.All;
    private EventSource?    _sourceFilter   = null;
    private HashSet<string> _selectedDcKeys = new(); // empty = All Data Centers

    private int      _shuffleSeed     = Environment.TickCount;
    private DateTime _lastSeenRefresh = DateTime.MinValue;
    private bool     _favoritesOnly   = false;

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
        ImGui.SameLine(ImGui.GetContentRegionMax().X - btnReloadW - btnSettingsW - statusW - 32f * gs);

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

            // "All" clears selection — DontClosePopups keeps the dropdown open
            bool allSel = _selectedDcKeys.Count == 0;
            if (ImGui.Selectable(allSel ? "[x] All Data Centers" : "[ ] All Data Centers",
                    allSel, ImGuiSelectableFlags.DontClosePopups))
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
                    string tick  = ticked ? "[x]" : "[ ]";
                    if (ImGui.Selectable($"  {tick} {dc.Name}##{dc.Name}dc",
                            ticked, ImGuiSelectableFlags.DontClosePopups))
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
        DrawSidebarTimeItem("● Live Now", TimeFilter.LiveNow,  ColTimeLive);
        DrawSidebarTimeItem("Today",      TimeFilter.Today,    ColTimeToday);
        DrawSidebarTimeItem("Upcoming",   TimeFilter.Upcoming, ColTimeUpcom);
        DrawSidebarTimeItem("All",        TimeFilter.All,      ColSubtitle);

        ImGui.Spacing();
        DrawSidebarRule();
        ImGui.Spacing();

        // ── SOURCE ────────────────────────────────────────────────────────────
        DrawSidebarLabel("SOURCE");
        DrawSidebarSourceItem("All Sources",    null,                  ColAccent);
        DrawSidebarSourceItem("Partake.gg",     EventSource.Partake,   ColPartake);
        DrawSidebarSourceItem("FFXIV Venues",   EventSource.FFXIVenue, ColFFXIVenue);

        ImGui.Spacing();
        DrawSidebarRule();
        ImGui.Spacing();

        // ── FAVORITES ─────────────────────────────────────────────────────────
        DrawSidebarLabel("FAVORITES");
        DrawSidebarToggleItem("\u2605 Favorites", ref _favoritesOnly, new Vector4(1.00f, 0.82f, 0.14f, 1f));

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
        float w      = ImGui.GetContentRegionAvail().X - 10f * gs;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,   4f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        using (ImRaii.PushColor(ImGuiCol.Button,        active ? color with { W = 0.22f } : new Vector4(0.11f, 0.11f, 0.17f, 0.70f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, color with { W = active ? 0.30f : 0.13f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  color with { W = 0.40f }))
        using (ImRaii.PushColor(ImGuiCol.Text,          active ? color : ColSubtitle with { W = 0.70f }))
        using (ImRaii.PushColor(ImGuiCol.Border,        active ? color with { W = 0.65f } : color with { W = 0.20f }))
        {
            if (ImGui.Button($"{label}##tf{(int)filter}", new Vector2(w, 26f * gs)))
                _timeFilter = filter;
        }

        ImGui.PopStyleVar(2);
        ImGui.Spacing();
    }

    private void DrawSidebarToggleItem(string label, ref bool value, Vector4 color)
    {
        bool  active = value;
        float gs     = ImGuiHelpers.GlobalScale;
        float w      = ImGui.GetContentRegionAvail().X - 10f * gs;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,   4f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        using (ImRaii.PushColor(ImGuiCol.Button,        active ? color with { W = 0.22f } : new Vector4(0.11f, 0.11f, 0.17f, 0.70f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, color with { W = active ? 0.30f : 0.13f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  color with { W = 0.40f }))
        using (ImRaii.PushColor(ImGuiCol.Text,          active ? color : ColSubtitle with { W = 0.70f }))
        using (ImRaii.PushColor(ImGuiCol.Border,        active ? color with { W = 0.65f } : color with { W = 0.20f }))
        {
            if (ImGui.Button($"{label}##toggle{label}", new Vector2(w, 26f * gs)))
            {
                value = !value;
                _filterCache.Clear();
            }
        }

        ImGui.PopStyleVar(2);
        ImGui.Spacing();
    }

    private void DrawSidebarSourceItem(string label, EventSource? source, Vector4 color)
    {
        bool  active = _sourceFilter == source;
        float gs     = ImGuiHelpers.GlobalScale;
        float w      = ImGui.GetContentRegionAvail().X - 10f * gs;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,   4f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        using (ImRaii.PushColor(ImGuiCol.Button,        active ? color with { W = 0.22f } : new Vector4(0.11f, 0.11f, 0.17f, 0.70f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, color with { W = active ? 0.30f : 0.13f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  color with { W = 0.40f }))
        using (ImRaii.PushColor(ImGuiCol.Text,          active ? color : ColSubtitle with { W = 0.70f }))
        using (ImRaii.PushColor(ImGuiCol.Border,        active ? color with { W = 0.65f } : color with { W = 0.20f }))
        {
            if (ImGui.Button($"{label}##src{source?.ToString() ?? "all"}", new Vector2(w, 26f * gs)))
            {
                _sourceFilter = source;
                _filterCache.Clear();
            }
        }

        ImGui.PopStyleVar(2);
        ImGui.Spacing();
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
        if (_favoritesOnly)
        {
            DrawFavoritesGrouped();
            return;
        }

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
            EventSource.FFXIVenue => "  ·  FFXIV Venues",
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

        // ── Event list with virtual scrolling ────────────────────────────────────
        using var child = ImRaii.Child("##evlist", Vector2.Zero, false);
        if (!child.Success) return;

        // Invalidate height cache on window resize (card width affects text wrapping)
        float currentW = ImGui.GetContentRegionAvail().X;
        if (Math.Abs(currentW - _lastContentWidth) > 1f)
        {
            _cardHeightCache.Clear();
            _lastContentWidth = currentW;
        }

        float scrollY  = ImGui.GetScrollY();
        float windowH  = ImGui.GetWindowHeight();
        float visTop   = scrollY;
        float visBot   = scrollY + windowH;
        float fallback = 80f * gs; // estimated height for cards not yet measured

        // Build cumulative Y positions from cached heights
        var cumY = new float[searched.Count + 1];
        for (int i = 0; i < searched.Count; i++)
            cumY[i + 1] = cumY[i] + _cardHeightCache.GetValueOrDefault(searched[i].Id, fallback);

        float totalH = cumY[searched.Count];

        // Determine the visible range
        int first = searched.Count;
        int last  = -1;
        for (int i = 0; i < searched.Count; i++)
        {
            if (cumY[i + 1] >= visTop && first == searched.Count) first = i;
            if (cumY[i]     <= visBot) last = i;
        }

        // Top spacer — holds scroll position for items above the viewport
        if (first > 0)
            ImGui.Dummy(new Vector2(0f, cumY[first]));

        // Draw only the visible cards
        if (first <= last)
        {
            for (int i = first; i <= last; i++)
            {
                var   ev     = searched[i];
                float before = ImGui.GetCursorPosY();
                ImGui.PushID(ev.Id);
                EventRenderer.DrawEventCard(ev, _stringCache.GetOrCompute(ev), _config);
                ImGui.PopID();
                ImGui.Dummy(new Vector2(0f, 5f * gs));
                _cardHeightCache[ev.Id] = ImGui.GetCursorPosY() - before;
            }
        }

        // Bottom spacer — maintains total scroll height for items below the viewport
        if (last < searched.Count - 1)
        {
            float remaining = totalH - (last >= 0 ? cumY[last + 1] : 0f);
            if (remaining > 0f)
                ImGui.Dummy(new Vector2(0f, remaining));
        }
    }

    // ══ Favorites Grouped View ════════════════════════════════════════════════

    private void DrawFavoritesGrouped()
    {
        float gs = ImGuiHelpers.GlobalScale;

        var favEvents = GetBaseEvents();

        // Build groups from current cache events + populate venue info cache for new favorites
        bool cacheUpdated = false;
        var groups = favEvents
            .GroupBy(e => e.Source == EventSource.FFXIVenue
                ? $"ffxiv:{e.Id}"
                : $"partake:{e.TeamId}")
            .Select(g =>
            {
                var first = g.First();
                string key = g.Key;
                var info = new FavoriteVenueInfo
                {
                    VenueId    = first.Source == EventSource.FFXIVenue ? first.Id : string.Empty,
                    TeamId     = first.TeamId,
                    Name       = first.Source == EventSource.FFXIVenue ? first.Title : first.TeamName,
                    Server     = first.Server,
                    DataCenter = first.DataCenter,
                    IconUrl    = !string.IsNullOrEmpty(first.TeamIconUrl) ? first.TeamIconUrl : first.BannerUrl,
                    Source     = first.Source,
                };
                // Bootstrap cache for pre-existing favorites that don't have an entry yet
                if (!_config.FavoriteVenueCache.ContainsKey(key))
                {
                    _config.FavoriteVenueCache[key] = info;
                    cacheUpdated = true;
                }
                return (Key: key, Info: info, Events: g.ToList());
            })
            .ToList();

        if (cacheUpdated) _config.Save();

        // Add favorites from persistent cache that have no events in the current cache
        var existingKeys = groups.Select(g => g.Key).ToHashSet();
        foreach (var (key, info) in _config.FavoriteVenueCache)
        {
            bool isStillFav = info.Source == EventSource.FFXIVenue
                ? _config.FavoriteEventIds.Contains(info.VenueId)
                : _config.FavoritePartakeTeamIds.Contains(info.TeamId);
            if (isStillFav && !existingKeys.Contains(key))
                groups.Add((Key: key, Info: info, Events: new List<VenueEvent>()));
        }

        groups = groups.OrderBy(g => g.Info.Name).ToList();

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);

        if (groups.Count == 0)
        {
            ImGui.TextColored(ColSubtitle, "No favorites yet. Star a venue or team to follow it here.");
            return;
        }

        ImGui.TextColored(ColSubtitle,
            $"{groups.Count} followed venue{(groups.Count != 1 ? "s" : "")}");
        ImGui.Spacing();

        using var child = ImRaii.Child("##favlist", Vector2.Zero, false);
        if (!child.Success) return;

        foreach (var (key, info, events) in groups)
        {
            ImGui.PushID(key);
            DrawVenueFolder(info, events);
            ImGui.PopID();
            ImGui.Dummy(new Vector2(0f, 5f * gs));
        }
    }

    private void DrawVenueFolder(FavoriteVenueInfo info, List<VenueEvent> events)
    {
        float gs        = ImGuiHelpers.GlobalScale;
        var   srcColor  = info.Source == EventSource.Partake ? ColPartake : ColFFXIVenue;
        var   colCardBg = new Vector4(0.13f, 0.13f, 0.20f, 1.00f);

        // Apply HideEndedEvents filter for display only — card always stays visible
        var utcNow = DateTime.UtcNow;
        var visibleEvents = (_config.HideEndedEvents
            ? events.Where(e => e.EndTime == null || e.EndTime.Value.ToUniversalTime() > utcNow)
            : events.AsEnumerable())
            .OrderBy(e => e.StartTime)
            .ToList();

        bool anyLive = visibleEvents.Any(e => _stringCache.GetOrCompute(e).IsLive);

        // Deduplicated tags across all events for this venue
        var folderTags = events.SelectMany(e => e.Tags).Distinct().ToList();

        float cardW  = ImGui.GetContentRegionAvail().X;
        var   cardTL = ImGui.GetCursorScreenPos();
        var   dl     = ImGui.GetWindowDrawList();

        float padX   = 14f * gs;
        float padY   = 8f  * gs;
        float iconSz = 32f * gs;
        float indent = padX + iconSz + 10f * gs;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.Dummy(new Vector2(0f, padY));
        ImGui.Indent(indent);

        // ── Name + [● LIVE] + Unfollow ────────────────────────────────────────
        float spc        = ImGui.GetStyle().ItemSpacing.X;
        float unfollowW  = ImGui.CalcTextSize("\u2605 Unfollow").X + ImGui.GetStyle().FramePadding.X * 2f + spc;
        float liveBadgeW = anyLive ? (ImGui.CalcTextSize("\u25cf LIVE").X + spc * 2f) : 0f;
        float totalRight = unfollowW + liveBadgeW;
        float nameAvail  = ImGui.GetContentRegionAvail().X - totalRight - 8f * gs;
        float rightEdge  = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        float lineH      = ImGui.GetTextLineHeight();

        var p0 = ImGui.GetCursorScreenPos();
        ImGui.PushClipRect(p0, p0 + new Vector2(nameAvail, lineH + 2f), true);
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.94f, 0.94f, 1.00f, 1f)))
            ImGui.TextUnformatted(info.Name.Length > 0 ? info.Name : "(unnamed)");
        ImGui.PopClipRect();

        if (anyLive)
        {
            ImGui.SameLine(rightEdge - totalRight);
            using (ImRaii.PushColor(ImGuiCol.Text, ColTimeLive))
                ImGui.TextUnformatted("\u25cf LIVE");
            ImGui.SameLine(0, spc);
        }
        else
        {
            ImGui.SameLine(rightEdge - unfollowW);
        }

        string unfollowKey = info.Source == EventSource.FFXIVenue
            ? $"ffxiv:{info.VenueId}" : $"partake:{info.TeamId}";

        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.28f, 0.22f, 0.04f, 0.70f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.44f, 0.32f, 0.06f, 0.90f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.56f, 0.40f, 0.08f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.Text,          new Vector4(1.00f, 0.82f, 0.14f, 1f)))
        {
            if (ImGui.SmallButton($"\u2605 Unfollow##{unfollowKey}unfollow"))
            {
                if (info.Source == EventSource.FFXIVenue)
                {
                    _config.FavoriteEventIds.Remove(info.VenueId);
                    _config.FavoriteVenueCache.Remove($"ffxiv:{info.VenueId}");
                }
                else
                {
                    _config.FavoritePartakeTeamIds.Remove(info.TeamId);
                    _config.FavoriteVenueCache.Remove($"partake:{info.TeamId}");
                }
                _config.Save();
                _filterCache.Clear();
            }
        }

        // ── Server · DC ────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(info.DataCenter) || !string.IsNullOrEmpty(info.Server))
        {
            var serverDc = !string.IsNullOrEmpty(info.Server)
                ? (string.IsNullOrEmpty(info.DataCenter)
                    ? info.Server
                    : $"{info.Server} · {info.DataCenter}")
                : info.DataCenter;
            using (ImRaii.PushColor(ImGuiCol.Text, ColSubtitle))
                ImGui.TextUnformatted(serverDc);
        }

        // ── Tags (deduplicated, folder level) ──────────────────────────────────
        if (folderTags.Count > 0)
        {
            for (int i = 0; i < folderTags.Count; i++)
            {
                if (i > 0) ImGui.SameLine(0, 4);
                var col = EventRenderer.GetTagColor(folderTags[i]);
                using var c1 = ImRaii.PushColor(ImGuiCol.Button,        col with { W = 0.22f });
                using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, col with { W = 0.38f });
                using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  col with { W = 0.50f });
                using var c4 = ImRaii.PushColor(ImGuiCol.Text,          col with { W = 0.90f });
                ImGui.SmallButton($" {folderTags[i]} ##fvtag{unfollowKey}{i}");
            }
        }

        // ── Divider ────────────────────────────────────────────────────────────
        ImGui.Spacing();
        {
            var lp0 = ImGui.GetCursorScreenPos();
            var lp1 = lp0 + new Vector2(ImGui.GetContentRegionAvail().X - padX, 1f);
            dl.AddRectFilled(lp0, lp1, ImGui.ColorConvertFloat4ToU32(ColDivider with { W = 0.50f }));
            ImGui.Dummy(new Vector2(0f, 4f * gs));
        }

        // ── Event rows ─────────────────────────────────────────────────────────
        if (visibleEvents.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ColSubtitle with { W = 0.45f }))
                ImGui.TextUnformatted("No upcoming events");
        }
        else
        {
            foreach (var ev in visibleEvents)
            {
                var cached    = _stringCache.GetOrCompute(ev);
                var timeColor = cached.IsLive ? ColTimeLive : ColSubtitle with { W = 0.85f };

                using (ImRaii.PushColor(ImGuiCol.Text, timeColor))
                    ImGui.TextUnformatted(cached.StartsAtLocal);

                if (!string.IsNullOrEmpty(cached.Location))
                {
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColDivider))
                        ImGui.TextUnformatted("\u00b7");
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.36f, 0.76f, 0.52f, 0.85f)))
                        ImGui.TextUnformatted(cached.Location);
                }

                // Right-aligned Open / Teleport buttons
                float evRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                float evBtnW  = 0f;
                if (!string.IsNullOrEmpty(ev.EventUrl))       evBtnW += 52f * gs + spc;
                if (!string.IsNullOrEmpty(ev.LifestreamCode)) evBtnW += 90f * gs + spc;

                if (evBtnW > 0f)
                {
                    ImGui.SameLine(evRight - evBtnW);

                    if (!string.IsNullOrEmpty(ev.EventUrl))
                    {
                        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.16f, 0.30f, 0.54f, 0.65f));
                        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.42f, 0.72f, 0.90f));
                        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.28f, 0.52f, 0.88f, 1.00f));
                        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.72f, 0.86f, 1.00f, 1.00f));
                        if (ImGui.SmallButton($" Open ##{ev.Id}fv"))
                            Util.OpenLink(ev.EventUrl);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open event page");
                        if (!string.IsNullOrEmpty(ev.LifestreamCode)) ImGui.SameLine(0, 4);
                    }

                    if (!string.IsNullOrEmpty(ev.LifestreamCode))
                    {
                        bool lsAvail = Plugin.IsLifestreamAvailable();
                        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        lsAvail ? new Vector4(0.18f, 0.36f, 0.22f, 0.65f) : new Vector4(0.28f, 0.20f, 0.20f, 0.65f));
                        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, lsAvail ? new Vector4(0.24f, 0.52f, 0.30f, 0.90f) : new Vector4(0.40f, 0.26f, 0.26f, 0.90f));
                        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  lsAvail ? new Vector4(0.30f, 0.64f, 0.38f, 1.00f) : new Vector4(0.50f, 0.32f, 0.32f, 1.00f));
                        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          lsAvail ? new Vector4(0.62f, 1.00f, 0.70f, 1.00f) : new Vector4(0.80f, 0.50f, 0.50f, 1.00f));
                        if (ImGui.SmallButton($" Teleport ##{ev.Id}fvt") && lsAvail)
                            Plugin.CommandManager.ProcessCommand($"/li {ev.LifestreamCode}");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(lsAvail ? $"/li {ev.LifestreamCode}" : "Lifestream is not installed");
                    }
                }

            }
        }

        ImGui.Dummy(new Vector2(0f, padY));
        ImGui.Unindent(indent);

        // ── Card background ────────────────────────────────────────────────────
        var cardBR = new Vector2(cardTL.X + cardW, ImGui.GetCursorScreenPos().Y);
        dl.ChannelsSetCurrent(0);

        // Subtle green tint on the card when live
        var bgColor     = anyLive ? new Vector4(0.10f, 0.16f, 0.14f, 1.00f) : colCardBg;
        var accentColor = anyLive ? ColTimeLive : srcColor;
        dl.AddRectFilled(cardTL, cardBR, ImGui.ColorConvertFloat4ToU32(bgColor), 6f * gs);
        dl.AddRectFilled(
            cardTL + new Vector2(0f, 4f * gs),
            new Vector2(cardTL.X + 3f * gs, cardBR.Y - 4f * gs),
            ImGui.ColorConvertFloat4ToU32(accentColor with { W = 0.90f }), 2f);

        // ── Icon (team/venue image, or placeholder) ────────────────────────────
        var iTL = new Vector2(cardTL.X + padX, cardTL.Y + padY);
        var iBR = iTL + new Vector2(iconSz, iconSz);

        var icon = !string.IsNullOrEmpty(info.IconUrl) ? EventRenderer.IconCache?.GetOrQueue(info.IconUrl) : null;

        if (icon != null)
        {
            var uv0 = Vector2.Zero;
            var uv1 = Vector2.One;
            if (icon.Width > 0 && icon.Height > 0)
            {
                float imgAspect = (float)icon.Width / icon.Height;
                if (imgAspect > 1f) // wider than tall → crop sides
                {
                    float crop   = 1f / imgAspect;
                    float offset = (1f - crop) * 0.5f;
                    uv0 = new Vector2(offset, 0f);
                    uv1 = new Vector2(1f - offset, 1f);
                }
            }
            dl.AddImageRounded(icon.Handle, iTL, iBR, uv0, uv1, 0xFFFFFFFF, 4f * gs);
            dl.AddRect(iTL, iBR, ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.25f }), 4f * gs, 0, gs);
        }
        else
        {
            dl.AddRectFilled(iTL, iBR, ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.13f }), 4f * gs);
            dl.AddRect(      iTL, iBR, ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.30f }), 4f * gs, 0, gs);
            string initial = info.Source == EventSource.Partake ? "P" : "V";
            var    initSz  = ImGui.CalcTextSize(initial);
            dl.AddText(
                iTL + new Vector2((iconSz - initSz.X) * 0.5f, (iconSz - initSz.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.70f }),
                initial);
        }

        dl.ChannelsMerge();
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════

    private string BuildCacheKey()
    {
        string dcPart  = _selectedDcKeys.Count == 0
            ? "_global_all"
            : string.Join(",", _selectedDcKeys.OrderBy(x => x));
        string srcPart = (_sourceFilter != null && !_favoritesOnly)
            ? _sourceFilter.ToString()!
            : "all";
        string favPart = _favoritesOnly ? $"|fav{ComputeFavHash()}" : string.Empty;
        return $"{dcPart}|{srcPart}{favPart}";
    }

    private int ComputeFavHash()
    {
        unchecked
        {
            int h = 17;
            foreach (var id in _config.FavoriteEventIds.OrderBy(x => x))
                h = h * 31 + id.GetHashCode();
            foreach (var id in _config.FavoritePartakeTeamIds.OrderBy(x => x))
                h = h * 31 + id;
            return h;
        }
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

        // Source filter is ignored when favorites-only is active
        // (favorites can come from any source)
        if (_sourceFilter != null && !_favoritesOnly)
            events = events.Where(e => e.Source == _sourceFilter);

        if (_favoritesOnly)
            events = events.Where(e =>
                e.Source == EventSource.FFXIVenue
                    ? _config.FavoriteEventIds.Contains(e.Id)
                    : e.TeamId > 0 && _config.FavoritePartakeTeamIds.Contains(e.TeamId));


        // Reroll shuffle seed each time the cache is refreshed
        if (_cache.LastRefresh != _lastSeenRefresh)
        {
            _lastSeenRefresh = _cache.LastRefresh;
            _shuffleSeed     = _cache.LastRefresh.GetHashCode() ^ Environment.TickCount;
        }

        var rng  = new Random(_shuffleSeed);
        var list = events
            .Select(e => (ev: e, group: GetSortGroup(e), rnd: rng.NextDouble()))
            .OrderBy(x => x.group)
            .ThenBy(x => x.rnd)
            .Select(x => x.ev)
            .ToList();

        return list;
    }

    // 0 = live now · 1 = opening within 2h · 2 = everything else
    private static int GetSortGroup(VenueEvent e)
    {
        var now   = DateTime.UtcNow;
        var start = e.StartTime.ToUniversalTime();
        var end   = e.EndTime?.ToUniversalTime();

        if (start <= now && (end == null || end.Value > now))
            return 0;

        if (start > now && (start - now).TotalHours <= 2.0)
            return 1;

        return 2;
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
            e.TeamName.Contains(q, StringComparison.OrdinalIgnoreCase)       ||
            e.InGameLocation.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    private static string TruncTag(string tag, int maxLen) =>
        tag.Length <= maxLen ? tag : tag[..(maxLen - 1)] + "\u2026";

    public void Dispose() { }
}
