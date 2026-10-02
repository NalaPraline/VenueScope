using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
    private readonly EventCacheService       _cache;
    private readonly PartakeService          _partake;
    private readonly Configuration           _config;
    private readonly Action                  _openConfig;
    private readonly SpotlightService        _spotlights;
    private readonly Action<SpotlightVenue>  _openSpotlight;

    private int      _spotlightIndex    = 0;
    private DateTime _spotlightRotateAt = DateTime.MinValue;
    private const float SpotlightRotateSeconds = 10f;
    private const string SpotlightContactUrl  = "https://discordid.netlify.app/?id=249633834646241281";
    private const float  SpotlightHeroAspect  = 1440f / 220f;
    private readonly EventStringCache          _stringCache     = new();
    private readonly EventFilterCache          _filterCache     = new();
    private readonly Dictionary<string, float> _cardHeightCache = new();
    private float _lastContentWidth = 0f;

    private string          _searchText     = string.Empty;
    private TimeFilter      _timeFilter     = TimeFilter.All;
    private EventSource?    _sourceFilter   = null;
    private HashSet<string> _selectedDcKeys = new();

    private int      _shuffleSeed     = Environment.TickCount;
    private DateTime _lastSeenRefresh = DateTime.MinValue;
    private bool     _favoritesOnly   = false;

    private string   _hideBannerName  = string.Empty;
    private DateTime _hideBannerUntil = DateTime.MinValue;
    private const float HideBannerDuration = 6f;

    private enum TimeFilter { All = 0, LiveNow = 1, Today = 2, Upcoming = 3 }

    private static readonly Vector4 ColPartake   = new(0.33f, 0.58f, 0.96f, 1f);
    private static readonly Vector4 ColFFXIVenue = new(0.62f, 0.32f, 0.92f, 1f);
    private static readonly Vector4 ColAccent    = new(0.40f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSubtitle  = new(0.50f, 0.50f, 0.60f, 1f);
    private static readonly Vector4 ColTimeLive  = new(0.20f, 0.86f, 0.42f, 1f);
    private static readonly Vector4 ColDivider   = new(0.22f, 0.22f, 0.32f, 1f);

    private const float SidebarW = 178f;

    private string _tagKey = string.Empty;

    public MainWindow(EventCacheService cache, PartakeService partake, Configuration config, Action openConfig,
                      SpotlightService spotlights, Action<SpotlightVenue> openSpotlight)
        : base("VenueScope##main", ImGuiWindowFlags.None)
    {
        _cache         = cache;
        _partake       = partake;
        _config        = config;
        _openConfig    = openConfig;
        _spotlights    = spotlights;
        _openSpotlight = openSpotlight;

        EventRenderer.OnTagClicked = tag =>
        {
            if (_cache.TagsByDc.TryGetValue(_tagKey, out var tags) && tags.ContainsKey(tag))
            {
                tags[tag] = !tags[tag];
                _filterCache.Clear();
            }
        };
        EventRenderer.IsTagSelected = tag =>
            _cache.TagsByDc.TryGetValue(_tagKey, out var tags) && tags.TryGetValue(tag, out var on) && on;

        EventRenderer.OnHideVenue = name =>
        {
            _cache.TagsByDc.Clear();
            _hideBannerName  = name;
            _hideBannerUntil = DateTime.Now.AddSeconds(HideBannerDuration);
        };

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

        _selectedDcKeys = new HashSet<string>(_config.SelectedDataCenters);
        if (_selectedDcKeys.Count == 0 && !string.IsNullOrEmpty(_config.SelectedDataCenter))
            _selectedDcKeys.Add(_config.SelectedDataCenter);

        _regionCheckAt = DateTime.MinValue;
        FollowCharacterRegion();

        if (_cache.LastRefresh != DateTime.MinValue &&
            (DateTime.UtcNow - _cache.LastRefresh).TotalMinutes >= 5)
            Task.Run(_cache.RefreshNowAsync);
    }

    private DateTime _regionCheckAt = DateTime.MinValue;

    private void FollowCharacterRegion()
    {
        if (!_config.FollowCharacterRegion || DateTime.UtcNow < _regionCheckAt) return;
        _regionCheckAt = DateTime.UtcNow.AddSeconds(5);

        var region = Plugin.GetCurrentCharacterRegion();
        if (string.IsNullOrEmpty(region) || region == _config.LastAutoRegion) return;
        if (!ConfigWindow.FollowedRegions.TryGetValue(region, out var wanted)) return;

        var dcs = _partake.DataCenters.Values.Where(dc => wanted.Contains(dc.Region)).Select(dc => dc.Name).ToList();
        if (dcs.Count == 0) return;

        _selectedDcKeys             = dcs.ToHashSet();
        _config.SelectedDataCenters = dcs;
        _config.LastAutoRegion      = region;
        _config.Save();
        _filterCache.Clear();
    }

    public override void PreDraw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.WindowBg,         Palette.Window);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,          Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.PopupBg,          new Vector4(0.110f, 0.098f, 0.145f, 0.99f));
        ImGui.PushStyleColor(ImGuiCol.Border,           Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.Separator,        Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,          Palette.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,   Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,    Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.Button,           Palette.Surface);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,    Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,     Palette.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.Header,           Palette.Accent with { W = 0.16f });
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered,    Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive,     Palette.Accent with { W = 0.28f });
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,      Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,    Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.CheckMark,        Palette.Accent);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg,   Palette.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.TitleBg,          Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive,    Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Palette.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,     7f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding,     8f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize,     10f * gs);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(21);
    }

    public override void Draw()
    {
        float gs = ImGuiHelpers.GlobalScale;

        FollowCharacterRegion();
        DrawTopBar();
        {
            using var color   = ImRaii.PushColor(ImGuiCol.ChildBg, Palette.Sidebar);
            using var sidebar = ImRaii.Child("##sidebar", new Vector2(SidebarW * gs, 0f), false,
                                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (sidebar.Success) DrawSidebar();
        }

        ImGui.SameLine(0, 0);

        var lp0 = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(
            lp0, lp0 + new Vector2(0f, ImGui.GetContentRegionAvail().Y), Palette.U(Palette.Line), 1f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10f * gs);
        using var main = ImRaii.Child("##maincontent", Vector2.Zero, false);
        if (main.Success) DrawMainContent();
    }

    private bool _focusSearch;

    private void DrawTopBar()
    {
        float gs    = ImGuiHelpers.GlobalScale;
        float h     = ImGui.GetFrameHeight() + 8f * gs;
        float gap   = 6f * gs;
        float lineH = ImGui.GetTextLineHeight();
        var   dl    = ImGui.GetWindowDrawList();

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.F))
            _focusSearch = true;

        ImGui.Dummy(new Vector2(0f, 2f * gs));
        float rowY  = ImGui.GetCursorPosY();
        var   rowP0 = ImGui.GetCursorScreenPos();

        float mark  = h - 8f * gs;
        var   markP = rowP0 + new Vector2(4f * gs, 4f * gs);
        dl.AddRectFilled(markP, markP + new Vector2(mark), Palette.U(Palette.Accent with { W = 0.22f }), 8f * gs);
        Widgets.DrawIconCentered(dl, FontAwesomeIcon.Compass, markP + new Vector2(mark / 2f), Palette.AccentText, 0.95f);
        dl.AddText(new Vector2(markP.X + mark + 9f * gs, rowP0.Y + (h - lineH) / 2f), Palette.U(Palette.Text), "VenueScope");
        float brandW = 4f * gs + mark + 9f * gs + ImGui.CalcTextSize("VenueScope").X + 18f * gs;

        float iconW   = h;
        float dcW     = DcPillWidth(h);
        float rightW  = dcW + gap + iconW + gap + iconW;
        float avail   = ImGui.GetContentRegionAvail().X;
        float searchW = Math.Clamp(avail - brandW - rightW - 24f * gs, 160f * gs, 480f * gs);

        var  boxP0  = rowP0 + new Vector2(brandW, 0f);
        var  boxP1  = boxP0 + new Vector2(searchW, h);
        dl.AddRectFilled(boxP0, boxP1, Palette.U(Palette.Surface), h / 2f);
        Widgets.DrawIconCentered(dl, FontAwesomeIcon.Search, boxP0 + new Vector2(18f * gs, h / 2f), Palette.Muted, 0.85f);

        float clearW = string.IsNullOrEmpty(_searchText) ? 0f : h - 6f * gs;
        ImGui.SetCursorScreenPos(boxP0 + new Vector2(32f * gs, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f * gs, (h - lineH) / 2f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg,        Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  Vector4.Zero);
        ImGui.SetNextItemWidth(searchW - 32f * gs - clearW - 10f * gs);
        if (_focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusSearch = false;
        }
        if (ImGui.InputTextWithHint("##search", "Search events, venues, tags...", ref _searchText, 128))
            _filterCache.Clear();
        bool typing = ImGui.IsItemActive();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();

        if (typing)
            dl.AddRect(boxP0, boxP1, Palette.U(Palette.Accent with { W = 0.55f }), h / 2f, 0, 1.2f * gs);
        else if (string.IsNullOrEmpty(_searchText))
        {
            const string hint = "Ctrl F";
            var hs = ImGui.CalcTextSize(hint);
            dl.AddText(new Vector2(boxP1.X - hs.X - 16f * gs, boxP0.Y + (h - hs.Y) / 2f), Palette.U(Palette.Muted with { W = 0.55f }), hint);
        }

        if (clearW > 0f)
        {
            ImGui.SetCursorScreenPos(new Vector2(boxP1.X - clearW - 3f * gs, boxP0.Y + 3f * gs));
            if (Widgets.GhostIcon("##clrsearch", FontAwesomeIcon.Times, "Clear the search", clearW))
                _searchText = string.Empty;
        }

        float rightX = rowP0.X + avail - rightW;
        ImGui.SetCursorScreenPos(new Vector2(rightX, rowP0.Y));
        DrawDcPill(dcW, h);

        ImGui.SetCursorScreenPos(new Vector2(rightX + dcW + gap, rowP0.Y));
        string status = _cache.IsRefreshing
            ? "Loading..."
            : _cache.LastRefresh != DateTime.MinValue
                ? $"{_stringCache.GetLastUpdateString(_cache.LastRefresh.ToLocalTime())}\nClick to reload"
                : "Not loaded yet";
        var refreshP = ImGui.GetCursorScreenPos();
        if (Widgets.GhostIcon("##reload", _cache.IsRefreshing ? null : FontAwesomeIcon.Sync, status, iconW))
        {
            _stringCache.Clear();
            _filterCache.Clear();
            Task.Run(_cache.RefreshNowAsync);
        }
        if (_cache.IsRefreshing)
        {
            float t = (float)ImGui.GetTime() * 6f;
            var   c = refreshP + new Vector2(iconW / 2f);
            dl.PathArcTo(c, iconW * 0.24f, t, t + MathF.PI * 1.4f, 24);
            dl.PathStroke(Palette.U(Palette.Accent), ImDrawFlags.None, 2f * gs);
        }

        ImGui.SetCursorScreenPos(new Vector2(rightX + dcW + gap + iconW + gap, rowP0.Y));
        if (Widgets.GhostIcon("##cfg", FontAwesomeIcon.Cog, "Settings", iconW))
            _openConfig();

        ImGui.SetCursorPosY(rowY + h + 8f * gs);
        var l0 = ImGui.GetCursorScreenPos();
        dl.AddLine(
            new Vector2(ImGui.GetWindowPos().X, l0.Y),
            new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowSize().X, l0.Y),
            Palette.U(Palette.Line));
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private string DcComboLabel => _selectedDcKeys.Count switch
    {
        0 => "All data centers",
        1 => _selectedDcKeys.First(),
        _ => $"{_selectedDcKeys.Count} data centers",
    };

    private float DcPillWidth(float height)
    {
        float gs = ImGuiHelpers.GlobalScale;
        return 14f * gs + Widgets.IconSize(FontAwesomeIcon.Globe, 0.9f).X + 8f * gs
             + ImGui.CalcTextSize(DcComboLabel).X + 10f * gs
             + Widgets.IconSize(FontAwesomeIcon.ChevronDown, 0.7f).X + 14f * gs;
    }

    private void DrawDcPill(float width, float height)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   p0 = ImGui.GetCursorScreenPos();
        var   dl = ImGui.GetWindowDrawList();

        bool clicked = ImGui.InvisibleButton("##dcpill", new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();
        bool open    = ImGui.IsPopupOpen("##dcpopup");

        dl.AddRectFilled(p0, p0 + new Vector2(width, height),
            Palette.U(open ? Palette.Accent with { W = 0.20f } : hovered ? Palette.SurfaceHover : Palette.Surface), height / 2f);

        var   globe = Widgets.IconSize(FontAwesomeIcon.Globe, 0.9f);
        float x     = p0.X + 14f * gs;
        Widgets.DrawIcon(dl, FontAwesomeIcon.Globe, new Vector2(x, p0.Y + (height - globe.Y) / 2f),
            _config.FollowCharacterRegion && _config.LastAutoRegion.Length > 0 ? Palette.AccentText : Palette.TextSoft, 0.9f);
        x += globe.X + 8f * gs;
        var label = ImGui.CalcTextSize(DcComboLabel);
        dl.AddText(new Vector2(x, p0.Y + (height - label.Y) / 2f), Palette.U(Palette.Text), DcComboLabel);
        var chevron = Widgets.IconSize(FontAwesomeIcon.ChevronDown, 0.7f);
        Widgets.DrawIcon(dl, open ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown,
            new Vector2(p0.X + width - 14f * gs - chevron.X, p0.Y + (height - chevron.Y) / 2f + 1f * gs), Palette.Muted, 0.7f);

        if (hovered && !open && _selectedDcKeys.Count > 1)
            ImGui.SetTooltip(string.Join(", ", _selectedDcKeys.OrderBy(k => k)));

        if (clicked) ImGui.OpenPopup("##dcpopup");

        ImGui.SetNextWindowPos(new Vector2(p0.X + width, p0.Y + height + 6f * gs), ImGuiCond.Appearing, new Vector2(1f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 10f * gs);
        if (ImGui.BeginPopup("##dcpopup"))
        {
            DrawDcPopup();
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);
    }

    private void DrawDcPopup()
    {
        float gs = ImGuiHelpers.GlobalScale;

        if (Widgets.Chip("##dcall", "All data centers", _selectedDcKeys.Count == 0))
            ClearDcSelection();

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

            ImGui.Dummy(new Vector2(0f, 4f * gs));
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                ImGui.TextUnformatted(regionName);

            for (int i = 0; i < dcs.Count; i++)
            {
                if (i > 0) ImGui.SameLine(0, 4f * gs);
                if (Widgets.Chip($"##dc{dcs[i].Name}", dcs[i].Name, _selectedDcKeys.Contains(dcs[i].Name)))
                    ToggleDc(dcs[i].Name);
            }
        }

        ImGui.Dummy(new Vector2(0f, 6f * gs));
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(p0, p0 + new Vector2(ImGui.GetContentRegionAvail().X, 0f), Palette.U(Palette.Line));
        ImGui.Dummy(new Vector2(0f, 6f * gs));

        string region = Plugin.GetCurrentCharacterRegion() ?? string.Empty;
        if (region.Length > 0 && Widgets.PillButton("##dcmine", FontAwesomeIcon.Crosshairs, $"My region ({region})", Palette.Accent,
                "Show the data centers of the region you play in"))
        {
            _config.FollowCharacterRegion = true;
            _config.LastAutoRegion        = string.Empty;
            _regionCheckAt                = DateTime.MinValue;
            FollowCharacterRegion();
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

    private string _countsKey = string.Empty;
    private readonly int[] _timeCounts = new int[4];
    private int _partakeCount;
    private int _venueCount;
    private Dictionary<string, int> _tagCounts = new();
    private string _tagSearch = string.Empty;

    private void UpdateCounts()
    {
        string cacheKey = BuildCacheKey();
        EnsureTagsBuilt(cacheKey);
        _cache.TagsByDc.TryGetValue(cacheKey, out var tags);
        var selectedTags = tags?.Where(t => t.Value).Select(t => t.Key).ToList() ?? new List<string>();

        string key = $"{cacheKey}|{string.Join(",", selectedTags)}|{_cache.LastRefresh.Ticks}|{DateTime.Now:HHmm}|{_config.HideEndedEvents}";
        if (key == _countsKey) return;
        _countsKey = key;

        var events = WithoutEnded(GetBaseEvents());
        events = _filterCache.GetFiltered(cacheKey, events, selectedTags);
        foreach (TimeFilter filter in Enum.GetValues<TimeFilter>())
            _timeCounts[(int)filter] = ApplyTimeFilter(events, filter).Count;

        _tagCounts = WithoutEnded(GetBaseEvents())
            .SelectMany(e => e.Tags)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        var bySource = WithoutEnded(GetBaseEvents(ignoreSource: true));
        _partakeCount = bySource.Count(e => e.Source == EventSource.Partake);
        _venueCount   = bySource.Count(e => e.Source == EventSource.FFXIVenue);
    }

    private List<VenueEvent> WithoutEnded(List<VenueEvent> events)
    {
        if (!_config.HideEndedEvents) return events;
        var now = DateTime.UtcNow;
        return events.Where(e => e.EndTime == null || e.EndTime.Value.ToUniversalTime() > now).ToList();
    }

    private void DrawSidebar()
    {
        float gs = ImGuiHelpers.GlobalScale;
        UpdateCounts();

        ImGui.Dummy(new Vector2(0f, 2f * gs));
        ImGui.Indent(6f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 2f * gs));
        float w = ImGui.GetContentRegionAvail().X - 6f * gs;

        using (ImRaii.Child("##sidenav", new Vector2(w, 0f), false, ImGuiWindowFlags.NoScrollbar))
        {
            Widgets.SectionLabel("When");
            TimeItem(TimeFilter.LiveNow,  null,                      Palette.Live, "Live now");
            TimeItem(TimeFilter.Today,    FontAwesomeIcon.CalendarDay, null,       "Today");
            TimeItem(TimeFilter.Upcoming, FontAwesomeIcon.CalendarAlt, null,       "Upcoming");
            TimeItem(TimeFilter.All,      FontAwesomeIcon.List,      null,         "All");

            Widgets.SectionLabel("Source");
            SourceItem(null,                  FontAwesomeIcon.LayerGroup, null,              "All sources",  _partakeCount + _venueCount);
            SourceItem(EventSource.Partake,   null,                       Palette.Partake,   "Partake",      _partakeCount);
            SourceItem(EventSource.FFXIVenue, null,                       Palette.FFXIVenue, "FFXIV Venues", _venueCount);

            Widgets.SectionLabel("Yours");
            int followed = _config.FavoritePartakeTeamIds.Count + _config.FavoriteEventIds.Count;
            if (Widgets.NavItem("##favs", FontAwesomeIcon.Star, null, "Favorites", followed, _favoritesOnly, Palette.Gold))
            {
                _favoritesOnly = !_favoritesOnly;
                _filterCache.Clear();
                if (_favoritesOnly)
                {
                    ClearDcSelection();
                    _timeFilter   = TimeFilter.All;
                    _sourceFilter = null;
                }
            }

            Widgets.SectionLabel("Tags");
            ImGui.Dummy(new Vector2(0f, 2f * gs));
            DrawSidebarTags();
        }

        ImGui.PopStyleVar();
        ImGui.Unindent(6f * gs);
    }

    private void TimeItem(TimeFilter filter, FontAwesomeIcon? icon, Vector4? dot, string label)
    {
        bool active = _timeFilter == filter;
        if (Widgets.NavItem($"##tf{(int)filter}", icon, dot, label, _favoritesOnly ? null : _timeCounts[(int)filter], active,
                filter == TimeFilter.LiveNow ? Palette.Live : null))
            _timeFilter = filter;
    }

    private void SourceItem(EventSource? source, FontAwesomeIcon? icon, Vector4? dot, string label, int count)
    {
        bool active = _sourceFilter == source;
        if (Widgets.NavItem($"##src{source?.ToString() ?? "all"}", icon, dot, label, _favoritesOnly ? null : count, active, dot))
        {
            _sourceFilter = source;
            _filterCache.Clear();
        }
    }

    private void DrawSidebarTags()
    {
        float  gs       = ImGuiHelpers.GlobalScale;
        string cacheKey = BuildCacheKey();
        EnsureTagsBuilt(cacheKey);

        if (!_cache.TagsByDc.TryGetValue(cacheKey, out var tags) || tags.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10f * gs);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, Palette.Muted with { W = 0.6f });
            ImGui.TextUnformatted("None");
            return;
        }

        var active = tags.Where(t => t.Value).Select(t => t.Key).ToList();

        float w  = ImGui.GetContentRegionAvail().X;
        float h  = ImGui.GetFrameHeight() + 4f * gs;
        var   p0 = ImGui.GetCursorScreenPos();
        var   dl = ImGui.GetWindowDrawList();

        bool clicked = ImGui.InvisibleButton("##tagpicker", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        bool open    = ImGui.IsPopupOpen("##tagpopup");

        dl.AddRectFilled(p0, p0 + new Vector2(w, h),
            Palette.U(open ? Palette.Accent with { W = 0.20f } : hovered ? Palette.SurfaceHover : Palette.Surface), 8f * gs);
        Widgets.DrawIcon(dl, FontAwesomeIcon.Tags, p0 + new Vector2(10f * gs, (h - Widgets.IconSize(FontAwesomeIcon.Tags, 0.85f).Y) / 2f),
            active.Count > 0 ? Palette.AccentText : Palette.Muted, 0.85f);
        string label = active.Count switch
        {
            0 => "Pick tags",
            1 => active[0],
            _ => $"{active.Count} tags",
        };
        label = Widgets.Ellipsize(label, w - 56f * gs);
        dl.AddText(p0 + new Vector2(32f * gs, (h - ImGui.GetTextLineHeight()) / 2f), Palette.U(active.Count > 0 ? Palette.Text : Palette.TextSoft), label);
        var chevron = Widgets.IconSize(FontAwesomeIcon.ChevronDown, 0.7f);
        Widgets.DrawIcon(dl, open ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown,
            new Vector2(p0.X + w - 10f * gs - chevron.X, p0.Y + (h - chevron.Y) / 2f + 1f * gs), Palette.Muted, 0.7f);

        if (clicked)
        {
            _tagSearch = string.Empty;
            ImGui.OpenPopup("##tagpopup");
        }

        ImGui.SetNextWindowPos(new Vector2(p0.X, p0.Y + h + 4f * gs), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(260f * gs, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 10f) * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 10f * gs);
        if (ImGui.BeginPopup("##tagpopup"))
        {
            DrawTagPopup(tags);
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar(2);

        if (active.Count == 0) return;

        ImGui.Dummy(new Vector2(0f, 4f * gs));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f * gs, 4f * gs));
        float left  = ImGui.GetCursorPosX();
        float right = ImGui.GetContentRegionMax().X;
        for (int i = 0; i < active.Count; i++)
        {
            string chip = TruncTag(active[i], 16);
            if (i > 0)
            {
                ImGui.SameLine();
                if (ImGui.GetCursorPosX() + Widgets.ChipWidth(chip + "  x") > right)
                {
                    ImGui.NewLine();
                    ImGui.SetCursorPosX(left);
                }
            }
            if (Widgets.Chip($"##activetag{i}", chip + "  x", true))
            {
                tags[active[i]] = false;
                _filterCache.Clear();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Remove {active[i]}");
        }
        ImGui.PopStyleVar();
    }

    private void DrawTagPopup(SortedDictionary<string, bool> tags)
    {
        float gs = ImGuiHelpers.GlobalScale;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint("##tagsearch", "Find a tag", ref _tagSearch, 40);
        ImGui.Dummy(new Vector2(0f, 2f * gs));

        var ordered = tags.Keys
            .Where(t => _tagSearch.Length == 0 || t.Contains(_tagSearch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => tags[t])
            .ThenByDescending(t => _tagCounts.GetValueOrDefault(t))
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        float rowH  = ImGui.GetTextLineHeight() + 10f * gs;
        float listH = Math.Min(ordered.Count, 10) * rowH + 4f * gs;
        using (var list = ImRaii.Child("##taglist", new Vector2(0f, Math.Max(rowH, listH)), false))
        {
            if (list.Success)
            {
                foreach (var tag in ordered)
                {
                    bool on    = tags[tag];
                    float w    = ImGui.GetContentRegionAvail().X;
                    var   p0   = ImGui.GetCursorScreenPos();
                    if (ImGui.InvisibleButton($"##tagrow{tag}", new Vector2(w, rowH)))
                    {
                        tags[tag] = !on;
                        _filterCache.Clear();
                        on = !on;
                    }
                    bool hovered = ImGui.IsItemHovered();
                    var  dl      = ImGui.GetWindowDrawList();
                    if (hovered) dl.AddRectFilled(p0, p0 + new Vector2(w, rowH), Palette.U(Palette.SurfaceHover), 6f * gs);

                    var box = new Vector2(14f * gs);
                    var bp  = p0 + new Vector2(8f * gs, (rowH - box.Y) / 2f);
                    dl.AddRectFilled(bp, bp + box, Palette.U(on ? Palette.Accent : Palette.Surface), 4f * gs);
                    if (!on) dl.AddRect(bp, bp + box, Palette.U(Palette.Muted with { W = 0.5f }), 4f * gs);
                    else     Widgets.DrawIconCentered(dl, FontAwesomeIcon.Check, bp + box / 2f, Palette.Window, 0.6f);

                    float textY = p0.Y + (rowH - ImGui.GetTextLineHeight()) / 2f;
                    string count = _tagCounts.GetValueOrDefault(tag).ToString();
                    float  cw    = ImGui.CalcTextSize(count).X;
                    dl.AddText(new Vector2(p0.X + 30f * gs, textY), Palette.U(on ? Palette.Text : Palette.TextSoft),
                        Widgets.Ellipsize(tag, w - 48f * gs - cw));
                    dl.AddText(new Vector2(p0.X + w - cw - 8f * gs, textY), Palette.U(Palette.Muted), count);
                }

                if (ordered.Count == 0)
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                        ImGui.TextUnformatted("No tag matches.");
                }
            }
        }

        int activeCount = tags.Values.Count(v => v);
        if (activeCount == 0) return;

        ImGui.Dummy(new Vector2(0f, 4f * gs));
        if (Widgets.PillButton("##tagclear", FontAwesomeIcon.Times, activeCount == 1 ? "Clear" : $"Clear {activeCount}", Palette.Danger))
        {
            foreach (var k in tags.Keys.ToList()) tags[k] = false;
            _filterCache.Clear();
        }
    }

    private void DrawHideBanner()
    {
        if (DateTime.Now >= _hideBannerUntil || string.IsNullOrEmpty(_hideBannerName))
            return;

        float gs       = ImGuiHelpers.GlobalScale;
        float elapsed  = (float)(DateTime.Now - _hideBannerUntil.AddSeconds(-HideBannerDuration)).TotalSeconds;
        float progress = Math.Clamp(1f - elapsed / HideBannerDuration, 0f, 1f);

        var   dl   = ImGui.GetWindowDrawList();
        var   tl   = ImGui.GetCursorScreenPos();
        float w    = ImGui.GetContentRegionAvail().X;
        float padX = 12f * gs;
        float padY = 8f  * gs;
        float lineH = ImGui.GetTextLineHeight();
        float cardH = padY * 2f + lineH * 2f + 4f * gs;

        var br = tl + new Vector2(w, cardH);

        dl.AddRectFilled(tl, br,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.11f, 0.08f, 0.96f)), 10f * gs);
        dl.AddRectFilled(
            tl + new Vector2(0f, 4f * gs),
            new Vector2(tl.X + 3f * gs, br.Y - 4f * gs),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.12f, 0.90f)), 2f);

        dl.AddRectFilled(
            new Vector2(tl.X, br.Y - 3f * gs), br,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.18f, 0.06f, 1.00f)));
        dl.AddRectFilled(
            new Vector2(tl.X, br.Y - 3f * gs),
            new Vector2(tl.X + w * progress, br.Y),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.62f, 0.18f, 0.75f)));

        ImGui.SetCursorScreenPos(tl + new Vector2(padX, padY));
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1.00f, 0.72f, 0.30f, 1f)))
            ImGui.TextUnformatted($"\"{_hideBannerName}\" is now hidden");

        ImGui.SetCursorScreenPos(tl + new Vector2(padX, padY + lineH + 4f * gs));
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.60f, 0.60f, 0.68f, 1f)))
            ImGui.TextUnformatted("Their events won't appear anymore. To unhide, open");

        ImGui.SameLine(0, 4);
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1.00f, 0.72f, 0.30f, 1f)))
        {
            ImGui.TextUnformatted("Settings");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip("Open Settings → Hidden Venues");
            }
            if (ImGui.IsItemClicked()) _openConfig();
        }

        ImGui.SameLine(0, 2);
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.60f, 0.60f, 0.68f, 1f)))
            ImGui.TextUnformatted("→ Hidden Venues.");

        ImGui.SetCursorScreenPos(new Vector2(tl.X, br.Y));
        ImGui.Dummy(new Vector2(w, 6f * gs));
    }

    private void DrawSpotlightHero()
    {
        if (!_config.ShowSpotlight) return;

        var venues = _spotlights.Venues;

        float gs = ImGuiHelpers.GlobalScale;

        int total      = venues.Count + 1;
        int promoIndex = venues.Count;

        if (_spotlightIndex >= total) _spotlightIndex = 0;
        if (total > 1)
        {
            if (_spotlightRotateAt == DateTime.MinValue)
                _spotlightRotateAt = DateTime.Now.AddSeconds(SpotlightRotateSeconds);
            else if (DateTime.Now >= _spotlightRotateAt)
            {
                _spotlightIndex    = (_spotlightIndex + 1) % total;
                _spotlightRotateAt = DateTime.Now.AddSeconds(SpotlightRotateSeconds);
            }
        }

        bool isPromo = _spotlightIndex == promoIndex;

        ImGui.Dummy(new Vector2(0f, 4f * gs));
        float w = ImGui.GetContentRegionAvail().X - 4f * gs;
        float h = Math.Clamp(w / SpotlightHeroAspect, 70f * gs, 104f * gs);

        var   p0       = ImGui.GetCursorScreenPos();
        var   p1       = p0 + new Vector2(w, h);
        float rounding = 10f * gs;

        if (isPromo)
            DrawPromoBanner(p0, p1, w, h, rounding);
        else
            DrawVenueBanner(p0, p1, w, h, rounding, venues[_spotlightIndex]);

        int dotHit = DrawSpotlightDots(p0, p1, total, promoIndex);

        ImGui.Dummy(new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        if (hovered && dotHit < 0)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(isPromo ? "Open Discord" : "Open spotlight");
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (dotHit >= 0)
            {
                _spotlightIndex    = dotHit;
                _spotlightRotateAt = DateTime.Now.AddSeconds(SpotlightRotateSeconds);
            }
            else if (isPromo) Util.OpenLink(SpotlightContactUrl);
            else              _openSpotlight(venues[_spotlightIndex]);
        }

        ImGui.Dummy(new Vector2(0f, 6f * gs));
    }

    private int DrawSpotlightDots(Vector2 p0, Vector2 p1, int count, int promoIndex)
    {
        if (count <= 1) return -1;

        float gs     = ImGuiHelpers.GlobalScale;
        var   dl     = ImGui.GetWindowDrawList();
        float step   = 12f * gs;
        float radius = 3f * gs;
        var   start  = new Vector2(p1.X - 14f * gs - step * (count - 1), p1.Y - 12f * gs);
        var   mouse  = ImGui.GetMousePos();
        int   hit    = -1;

        for (int i = 0; i < count; i++)
        {
            var  c      = start + new Vector2(step * i, 0f);
            bool active = i == _spotlightIndex;
            bool over   = Vector2.Distance(mouse, c) <= step / 2f;
            if (over) hit = i;

            var col = active ? Palette.Text : over ? Palette.TextSoft : Palette.Text with { W = 0.35f };
            if (i == promoIndex)
                Widgets.DrawIconCentered(dl, FontAwesomeIcon.Star, c, col, 0.55f);
            else
                dl.AddCircleFilled(c, active ? radius + 0.8f * gs : radius, Palette.U(col));
        }

        if (hit >= 0) ImGui.SetTooltip(hit == promoIndex ? "Your event here" : _spotlights.Venues[hit].Name);
        return hit;
    }

    private void DrawVenueBanner(Vector2 p0, Vector2 p1, float w, float h, float rounding, SpotlightVenue venue)
    {
        float gs     = ImGuiHelpers.GlobalScale;
        var   dl     = ImGui.GetWindowDrawList();
        var   accent = venue.GetAccent() ?? ColAccent;

        var icon = !string.IsNullOrEmpty(venue.ImageUrl) ? EventRenderer.IconCache?.GetOrQueue(venue.ImageUrl) : null;
        if (icon != null && icon.Width > 0 && icon.Height > 0)
        {
            float boxAspect = w / h;
            float imgAspect = (float)icon.Width / icon.Height;
            var uv0 = Vector2.Zero;
            var uv1 = Vector2.One;
            if (imgAspect > boxAspect)
            {
                float crop   = boxAspect / imgAspect;
                float offset = (1f - crop) * 0.5f;
                uv0 = new Vector2(offset, 0f);
                uv1 = new Vector2(1f - offset, 1f);
            }
            else
            {
                float crop   = imgAspect / boxAspect;
                float offset = (1f - crop) * 0.5f;
                uv0 = new Vector2(0f, offset);
                uv1 = new Vector2(1f, 1f - offset);
            }
            dl.AddImageRounded(icon.Handle, p0, p1, uv0, uv1, 0xFFFFFFFF, rounding);
        }
        else
        {
            dl.AddRectFilled(p0, p1, ImGui.ColorConvertFloat4ToU32(new Vector4(0.16f, 0.13f, 0.24f, 1f)), rounding);
        }

        float fadeTop = p0.Y + h * 0.42f;
        uint  clear   = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0f));
        uint  dark    = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.78f));
        dl.AddRectFilledMultiColor(new Vector2(p0.X, fadeTop), p1, clear, clear, dark, dark);
        dl.AddRect(p0, p1, ImGui.ColorConvertFloat4ToU32(accent with { W = 0.30f }), rounding, 0, gs);

        dl.AddText(p0 + new Vector2(12f * gs, 10f * gs),
            ImGui.ColorConvertFloat4ToU32(accent with { W = 0.92f }), "SPOTLIGHT");

        DrawBannerStatusBadge(p0, p1, accent, venue);

        if (venue.HideBannerText) return;

        string title = !string.IsNullOrEmpty(venue.BannerTitle) ? venue.BannerTitle : venue.Name;

        string sub = !string.IsNullOrEmpty(venue.BannerSubtitle)
            ? venue.BannerSubtitle
            : (!string.IsNullOrEmpty(venue.Tagline)
                ? venue.Tagline
                : (!string.IsNullOrEmpty(venue.Server)
                    ? $"{venue.Server}  {venue.BuildLocationLabel()}".Trim()
                    : venue.BuildLocationLabel()));

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(sub)) return;

        float lineH   = ImGui.GetTextLineHeight();
        float bottom  = p1.Y - 12f * gs - lineH;
        var   namePos = new Vector2(p0.X + 12f * gs, bottom - lineH - 2f * gs);

        if (string.IsNullOrEmpty(title))
        {
            dl.AddText(new Vector2(p0.X + 12f * gs, bottom),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.80f, 0.80f, 0.88f, 0.92f)), sub);
            return;
        }

        dl.AddText(namePos,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.98f, 0.98f, 1.00f, 1f)), title);

        if (!string.IsNullOrEmpty(sub))
            dl.AddText(namePos + new Vector2(0f, lineH + 3f * gs),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.80f, 0.80f, 0.88f, 0.92f)), sub);
    }

    private void DrawBannerStatusBadge(Vector2 p0, Vector2 p1, Vector4 accent, SpotlightVenue venue)
    {
        var now    = DateTimeOffset.UtcNow;
        var status = venue.GetStatus(now);
        if (status != SpotlightStatus.Live && status != SpotlightStatus.Upcoming) return;

        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();

        bool    live = status == SpotlightStatus.Live;
        string  txt  = live ? "● EVENT IN PROGRESS" : $"STARTS {ShortUntil(venue.StartTime!.Value, now)}";
        Vector4 col  = live ? new Vector4(0.32f, 0.92f, 0.50f, 1f) : accent;

        var   ts = ImGui.CalcTextSize(txt);
        float px = 7f * gs;
        float py = 3f * gs;
        var   b1 = new Vector2(p1.X - 10f * gs, p0.Y + 9f * gs + ts.Y + py * 2f);
        var   b0 = new Vector2(b1.X - (ts.X + px * 2f), p0.Y + 9f * gs);
        float rad = (ts.Y + py * 2f) * 0.5f;

        dl.AddRectFilled(b0, b1, ImGui.ColorConvertFloat4ToU32(col with { W = live ? 0.26f : 0.20f }), rad);
        dl.AddRect(b0, b1, ImGui.ColorConvertFloat4ToU32(col with { W = 0.70f }), rad, 0, 1f);
        dl.AddText(b0 + new Vector2(px, py), ImGui.ColorConvertFloat4ToU32(col), txt);
    }

    private static string ShortUntil(DateTimeOffset target, DateTimeOffset now)
    {
        var s = target - now;
        if (s < TimeSpan.Zero) s = TimeSpan.Zero;
        if (s.TotalDays  >= 1) return $"IN {(int)s.TotalDays}D {s.Hours}H";
        if (s.TotalHours >= 1) return $"IN {(int)s.TotalHours}H {s.Minutes}M";
        return $"IN {Math.Max(1, s.Minutes)}M";
    }

    private static readonly Vector2[] PromoStars =
    [
        new(0.46f, 0.30f), new(0.53f, 0.68f), new(0.61f, 0.22f), new(0.68f, 0.55f),
        new(0.57f, 0.42f), new(0.50f, 0.18f), new(0.64f, 0.80f),
    ];

    private void DrawPromoBanner(Vector2 p0, Vector2 p1, float w, float h, float rounding)
    {
        float gs      = ImGuiHelpers.GlobalScale;
        var   dl      = ImGui.GetWindowDrawList();
        bool  hovered = ImGui.IsMouseHoveringRect(p0, p1);
        float midY    = p0.Y + h / 2f;
        float lineH   = ImGui.GetTextLineHeight();

        dl.AddRectFilled(p0, p1, Palette.U(new Vector4(0.15f, 0.11f, 0.27f, 1f)), rounding);
        foreach (var s in PromoStars)
            dl.AddCircleFilled(p0 + new Vector2(w * s.X, h * s.Y), 1.2f * gs, Palette.U(Palette.Text with { W = 0.45f }));
        dl.AddRect(p0, p1, Palette.U(Palette.Accent with { W = hovered ? 0.65f : 0.28f }), rounding, 0, 1.2f * gs);

        float radius = Math.Min(h * 0.30f, 26f * gs);
        var   circle = new Vector2(p0.X + 16f * gs + radius, midY);
        dl.AddCircleFilled(circle, radius, Palette.U(Palette.Accent with { W = 0.20f }), 32);
        dl.AddCircle(circle, radius, Palette.U(Palette.Accent with { W = 0.45f }), 32, 1f);
        Widgets.DrawIconCentered(dl, FontAwesomeIcon.Star, circle, Palette.AccentText, 1.1f);

        float textX = circle.X + radius + 14f * gs;
        dl.AddText(new Vector2(textX, midY - lineH - 3f * gs), Palette.U(Palette.Accent), "VENUE SPOTLIGHT");
        Widgets.TextWithSize(dl, new Vector2(textX, midY + 1f * gs), Palette.Text, "Want your event in the spotlight?", 1.15f);

        const string cta = "Reach me on Discord";
        var   ctaSz  = ImGui.CalcTextSize(cta);
        var   iconSz = Widgets.IconSize(FontAwesomeIcon.CommentDots, 0.9f);
        var   pill   = new Vector2(ctaSz.X + iconSz.X + 30f * gs, ctaSz.Y + 12f * gs);
        var   pillTL = new Vector2(p1.X - 18f * gs - pill.X, midY - pill.Y / 2f);
        if (pillTL.X > textX + Widgets.MeasureWithSize("Want your event in the spotlight?", 1.15f).X + 12f * gs)
        {
            dl.AddRectFilled(pillTL, pillTL + pill, Palette.U(Palette.Accent with { W = hovered ? 0.55f : 0.36f }), pill.Y / 2f);
            Widgets.DrawIcon(dl, FontAwesomeIcon.CommentDots, pillTL + new Vector2(12f * gs, (pill.Y - iconSz.Y) / 2f), Palette.Text, 0.9f);
            dl.AddText(pillTL + new Vector2(18f * gs + iconSz.X, 6f * gs), Palette.U(Palette.Text), cta);
        }
    }

    private void DrawMainContent()
    {
        DrawHideBanner();
        DrawSpotlightHero();

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
        _tagKey = cacheKey;

        var selectedTags = tags?.Where(t => t.Value).Select(t => t.Key).ToList() ?? new List<string>();
        var tagFiltered  = _filterCache.GetFiltered(cacheKey, baseEvents, selectedTags);
        var timeFiltered = ApplyTimeFilter(tagFiltered);
        var searched     = ApplySearch(timeFiltered);

        string tfLabel = _timeFilter switch
        {
            TimeFilter.LiveNow  => "live now",
            TimeFilter.Today    => "today",
            TimeFilter.Upcoming => "coming up",
            _                   => string.Empty,
        };
        string srcLabel = _sourceFilter switch
        {
            EventSource.Partake   => " on Partake",
            EventSource.FFXIVenue => " on FFXIV Venues",
            _                     => string.Empty,
        };
        string countLabel = $"{searched.Count} event{(searched.Count != 1 ? "s" : "")}{(tfLabel.Length > 0 ? " " + tfLabel : "")}{srcLabel}";
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
            ImGui.TextUnformatted($"{countLabel}  ·  {DcComboLabel.ToLowerInvariant()}");

        ImGui.Dummy(new Vector2(0f, 2f * gs));

        if (searched.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);
            ImGui.TextColored(ColSubtitle,
                baseEvents.Count == 0
                    ? "No events on this data center."
                    : "No events match the current filters.");
            return;
        }
        using var child = ImRaii.Child("##evlist", Vector2.Zero, false);
        if (!child.Success) return;

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
        float fallback = 80f * gs;

        var cumY = new float[searched.Count + 1];
        for (int i = 0; i < searched.Count; i++)
            cumY[i + 1] = cumY[i] + _cardHeightCache.GetValueOrDefault(searched[i].Id, fallback);

        float totalH = cumY[searched.Count];

        int first = searched.Count;
        int last  = -1;
        for (int i = 0; i < searched.Count; i++)
        {
            if (cumY[i + 1] >= visTop && first == searched.Count) first = i;
            if (cumY[i]     <= visBot) last = i;
        }

        if (first > 0)
            ImGui.Dummy(new Vector2(0f, cumY[first]));

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

        if (last < searched.Count - 1)
        {
            float remaining = totalH - (last >= 0 ? cumY[last + 1] : 0f);
            if (remaining > 0f)
                ImGui.Dummy(new Vector2(0f, remaining));
        }
    }

    private void DrawFavoritesGrouped()
    {
        float gs = ImGuiHelpers.GlobalScale;

        var favEvents = GetBaseEvents();

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
                if (!_config.FavoriteVenueCache.ContainsKey(key))
                {
                    _config.FavoriteVenueCache[key] = info;
                    cacheUpdated = true;
                }
                return (Key: key, Info: info, Events: g.ToList());
            })
            .ToList();

        if (cacheUpdated) _config.Save();

        if (_sourceFilter != null)
            groups = groups.Where(g => g.Info.Source == _sourceFilter).ToList();

        var existingKeys = groups.Select(g => g.Key).ToHashSet();
        foreach (var (key, info) in _config.FavoriteVenueCache)
        {
            bool isStillFav = info.Source == EventSource.FFXIVenue
                ? _config.FavoriteEventIds.Contains(info.VenueId)
                : _config.FavoritePartakeTeamIds.Contains(info.TeamId);
            if (!isStillFav || existingKeys.Contains(key)) continue;
            if (_sourceFilter != null && info.Source != _sourceFilter) continue;
            if (_selectedDcKeys.Count > 0 && !_selectedDcKeys.Contains(info.DataCenter)) continue;
            groups.Add((Key: key, Info: info, Events: new List<VenueEvent>()));
        }

        var sortRng = new Random(_shuffleSeed);
        groups = groups
            .Select(g => (g.Key, g.Info, g.Events,
                SortGroup: g.Events.Count > 0 ? g.Events.Min(e => GetSortGroup(e)) : 3,
                Rnd: sortRng.NextDouble()))
            .OrderBy(x => x.SortGroup)
            .ThenBy(x => x.Rnd)
            .Select(x => (x.Key, x.Info, x.Events))
            .ToList();

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

        string favCacheKey = BuildCacheKey();
        EnsureTagsBuilt(favCacheKey);
        if (_cache.TagsByDc.TryGetValue(favCacheKey, out var activeTags))
        {
            var selectedTags = activeTags.Where(t => t.Value).Select(t => t.Key).ToHashSet();
            if (selectedTags.Count > 0)
                groups = groups.Where(g => g.Events.Any(e => e.Tags.Any(t => selectedTags.Contains(t)))).ToList();
        }

        foreach (var (key, info, events) in groups)
        {
            ImGui.PushID(key);
            bool rendered = DrawVenueFolder(info, events);
            ImGui.PopID();
            if (rendered) ImGui.Dummy(new Vector2(0f, 5f * gs));
        }
    }

    private bool DrawVenueFolder(FavoriteVenueInfo info, List<VenueEvent> events)
    {
        float gs        = ImGuiHelpers.GlobalScale;
        var   srcColor  = info.Source == EventSource.Partake ? ColPartake : ColFFXIVenue;
        var   colCardBg = Palette.Card;

        var utcNow = DateTime.UtcNow;
        var visibleEvents = (_config.HideEndedEvents
            ? events.Where(e => e.EndTime == null || e.EndTime.Value.ToUniversalTime() > utcNow)
            : events.AsEnumerable())
            .OrderBy(e => e.StartTime)
            .ToList();
        if (_timeFilter != TimeFilter.All)
            visibleEvents = ApplyTimeFilter(visibleEvents);

        bool anyFilterActive = _timeFilter != TimeFilter.All || _selectedDcKeys.Count > 0;
        if (anyFilterActive && visibleEvents.Count == 0) return false;

        bool anyLive = visibleEvents.Any(e => _stringCache.GetOrCompute(e).IsLive);

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
        float spc        = ImGui.GetStyle().ItemSpacing.X;
        float unfollowW  = ImGui.CalcTextSize("\u2605 Unfollow").X + ImGui.GetStyle().FramePadding.X * 2f + spc;
        float liveBadgeW = anyLive ? (ImGui.CalcTextSize("\u25cf LIVE").X + spc * 2f) : 0f;
        float totalRight = unfollowW + liveBadgeW;
        float nameAvail  = ImGui.GetContentRegionAvail().X - totalRight - 8f * gs;
        float rightEdge  = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        float lineH      = ImGui.GetTextLineHeight();

        var p0 = ImGui.GetCursorScreenPos();
        ImGui.PushClipRect(p0, p0 + new Vector2(nameAvail, lineH + 2f), true);
        using (ImRaii.PushColor(ImGuiCol.Text, srcColor with { W = 1f }))
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
                if (ImGui.SmallButton($" {folderTags[i]} ##fvtag{unfollowKey}{i}"))
                {
                    _favoritesOnly = false;
                    _filterCache.Clear();
                    var newKey = BuildCacheKey();
                    EnsureTagsBuilt(newKey);
                    if (_cache.TagsByDc.TryGetValue(newKey, out var tagDict))
                        tagDict[folderTags[i]] = true;
                }
            }
        }
        ImGui.Spacing();
        {
            var lp0 = ImGui.GetCursorScreenPos();
            var lp1 = lp0 + new Vector2(ImGui.GetContentRegionAvail().X - padX, 1f);
            dl.AddRectFilled(lp0, lp1, ImGui.ColorConvertFloat4ToU32(ColDivider with { W = 0.50f }));
            ImGui.Dummy(new Vector2(0f, 4f * gs));
        }
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

                var serverDcInline = !string.IsNullOrEmpty(info.Server) && !string.IsNullOrEmpty(info.DataCenter)
                    ? $"{info.Server} · {info.DataCenter}"
                    : !string.IsNullOrEmpty(info.DataCenter) ? info.DataCenter : info.Server;
                if (!string.IsNullOrEmpty(serverDcInline))
                {
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColDivider))
                        ImGui.TextUnformatted("\u00b7");
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColSubtitle with { W = 0.70f }))
                        ImGui.TextUnformatted(serverDcInline);
                }

                if (!string.IsNullOrEmpty(cached.Location))
                {
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColDivider))
                        ImGui.TextUnformatted("\u00b7");
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.36f, 0.76f, 0.52f, 0.85f)))
                        ImGui.TextUnformatted(cached.Location);
                }

                if (ev.Source == EventSource.Partake && !string.IsNullOrEmpty(ev.Title))
                {
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColSubtitle with { W = 0.60f }))
                        ImGui.TextUnformatted(ev.Title);
                }

                float evRight = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
                float evBtnW  = 0f;
                if (!string.IsNullOrEmpty(ev.EventUrl))       evBtnW += 52f * gs + spc;
                if (!string.IsNullOrEmpty(ev.LifestreamCode)) evBtnW += 90f * gs + spc;
                if (ev.Source == EventSource.FFXIVenue)        evBtnW += 32f * gs + spc;

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
                    }

                    if (!string.IsNullOrEmpty(ev.LifestreamCode))
                    {
                        if (!string.IsNullOrEmpty(ev.EventUrl)) ImGui.SameLine(0, 4);
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

                    if (ev.Source == EventSource.FFXIVenue)
                    {
                        if (!string.IsNullOrEmpty(ev.EventUrl) || !string.IsNullOrEmpty(ev.LifestreamCode))
                            ImGui.SameLine(0, 4);
                        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.35f, 0.12f, 0.12f, 0.65f));
                        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.55f, 0.18f, 0.18f, 0.90f));
                        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.70f, 0.22f, 0.22f, 1.00f));
                        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(1.00f, 0.40f, 0.40f, 1.00f));
                        if (ImGui.SmallButton($" ! ##{ev.Id}fvflag"))
                            EventRenderer.OpenFlagPopup(ev.Id);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Report this venue");
                    }
                }
            }
            EventRenderer.DrawFlagPopup();
        }

        ImGui.Dummy(new Vector2(0f, padY));
        ImGui.Unindent(indent);
        var cardBR = new Vector2(cardTL.X + cardW, ImGui.GetCursorScreenPos().Y);
        dl.ChannelsSetCurrent(0);

        var bgColor     = anyLive ? new Vector4(0.10f, 0.15f, 0.13f, 1.00f) : colCardBg;
        var accentColor = anyLive ? ColTimeLive : srcColor;
        dl.AddRectFilled(cardTL, cardBR, ImGui.ColorConvertFloat4ToU32(bgColor), 10f * gs);
        dl.AddRectFilled(
            cardTL + new Vector2(0f, 4f * gs),
            new Vector2(cardTL.X + 3f * gs, cardBR.Y - 4f * gs),
            ImGui.ColorConvertFloat4ToU32(accentColor with { W = 0.90f }), 2f);
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
                if (imgAspect > 1f)
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
        return true;
    }

    private string BuildCacheKey()
    {
        string dcPart  = _selectedDcKeys.Count == 0
            ? "_global_all"
            : string.Join(",", _selectedDcKeys.OrderBy(x => x));
        string srcPart = (_sourceFilter != null && !_favoritesOnly)
            ? _sourceFilter.ToString()!
            : "all";
        string favPart = _favoritesOnly ? $"|fav{ComputeFavHash()}" : string.Empty;
        int    hidHash = ComputeHiddenHash();
        string hidPart = hidHash != 0 ? $"|hid{hidHash}" : string.Empty;
        return $"{dcPart}|{srcPart}{favPart}{hidPart}";
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

    private int ComputeHiddenHash()
    {
        unchecked
        {
            int h = 17;
            foreach (var id in _config.HiddenVenueIds.OrderBy(x => x))
                h = h * 31 + id.GetHashCode();
            foreach (var id in _config.HiddenPartakeTeamIds.OrderBy(x => x))
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

    private List<VenueEvent> GetBaseEvents(bool ignoreSource = false)
    {
        IEnumerable<VenueEvent> events;

        if (_selectedDcKeys.Count == 0)
            events = _cache.CachedEvents;
        else if (_selectedDcKeys.Count == 1)
            events = _cache.EventsByDc.GetValueOrDefault(_selectedDcKeys.First()) ?? Enumerable.Empty<VenueEvent>();
        else
            events = _selectedDcKeys.SelectMany(dc =>
                _cache.EventsByDc.GetValueOrDefault(dc) ?? Enumerable.Empty<VenueEvent>());

        if (_sourceFilter != null && !_favoritesOnly && !ignoreSource)
            events = events.Where(e => e.Source == _sourceFilter);

        if (_favoritesOnly)
            events = events.Where(e =>
                e.Source == EventSource.FFXIVenue
                    ? _config.FavoriteEventIds.Contains(e.Id)
                    : e.TeamId > 0 && _config.FavoritePartakeTeamIds.Contains(e.TeamId));

        events = events.Where(e =>
            e.Source == EventSource.FFXIVenue
                ? !_config.HiddenVenueIds.Contains(e.Id)
                : !(e.TeamId > 0 && _config.HiddenPartakeTeamIds.Contains(e.TeamId)));

        if (_cache.LastRefresh != _lastSeenRefresh)
        {
            _lastSeenRefresh = _cache.LastRefresh;
            _shuffleSeed     = _cache.LastRefresh.GetHashCode() ^ Environment.TickCount;
        }

        var rng  = new Random(_shuffleSeed);
        var list = events
            .Select(e => (ev: e, group: GetSortGroup(e), rnd: rng.NextDouble()))
            .OrderBy(x => x.group)
            .ThenBy(x => x.ev.StartTime)
            .ThenBy(x => x.rnd)
            .Select(x => x.ev)
            .ToList();

        return list;
    }

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

    private List<VenueEvent> ApplyTimeFilter(List<VenueEvent> events, TimeFilter? filter = null)
    {
        var utcNow    = DateTime.UtcNow;
        var todayDate = DateTime.Now.Date;

        return (filter ?? _timeFilter) switch
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

            TimeFilter.Upcoming => events.Where(e =>
            {
                var s = e.StartTime.ToUniversalTime();
                return s > utcNow && s <= utcNow.AddDays(14);
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
            e.TeamName.Contains(q, StringComparison.OrdinalIgnoreCase)       ||
            e.InGameLocation.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    private static string TruncTag(string tag, int maxLen) =>
        tag.Length <= maxLen ? tag : tag[..(maxLen - 1)] + "\u2026";

    public void Dispose() { }
}
