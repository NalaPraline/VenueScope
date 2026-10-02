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
using VenueScope.Helpers;
using VenueScope.Models;
using VenueScope.Services;

namespace VenueScope.UI;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration     _config;
    private readonly PartakeService    _partake;
    private readonly EventCacheService _cache;

    private enum Page { General, DataCenters, Notifications, Display, Travel, Hidden, About }
    private Page _page = Page.General;

    private static readonly string[] RegionNames      = ["Japan", "North America", "Europe", "Oceania"];
    private static readonly string[] CharacterRegions = ["Japan", "North America", "Europe"];

    internal static readonly Dictionary<string, int[]> FollowedRegions = new()
    {
        ["North America"] = [2, 4],
        ["Europe"]        = [3, 4],
        ["Japan"]         = [1, 4],
        ["Oceania"]       = [4, 2],
    };

    private const float SidebarW = 170f;

    public ConfigWindow(Configuration config, PartakeService partake, EventCacheService cache)
        : base("VenueScope Settings##cfg", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _config  = config;
        _partake = partake;
        _cache   = cache;

        Size            = new Vector2(700, 500);
        SizeCondition   = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 400),
            MaximumSize = new Vector2(1200, 1000),
        };
    }

    public override void PreDraw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.WindowBg,       Palette.Window);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,        Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.PopupBg,        new Vector4(0.110f, 0.098f, 0.145f, 0.99f));
        ImGui.PushStyleColor(ImGuiCol.Border,         Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,        Palette.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,  Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab,       Palette.Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Palette.AccentText);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,    Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,  Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Palette.Accent with { W = 0.30f });
        ImGui.PushStyleColor(ImGuiCol.TitleBg,          Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive,    Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Palette.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,     7f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding,      7f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize,     10f * gs);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(15);
    }

    public override void Draw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f) * gs);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Palette.Sidebar))
        using (var side = ImRaii.Child("##cfgside", new Vector2(SidebarW * gs, 0f), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (side.Success)
            {
                ImGui.Dummy(new Vector2(0f, 6f * gs));
                ImGui.Indent(6f * gs);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 2f * gs));
                using (ImRaii.Child("##cfgnav", new Vector2(ImGui.GetContentRegionAvail().X - 6f * gs, 0f), false))
                {
                    Nav(Page.General,       FontAwesomeIcon.SlidersH,   "General");
                    Nav(Page.DataCenters,   FontAwesomeIcon.Globe,      "Data centers");
                    Nav(Page.Notifications, FontAwesomeIcon.Bell,       "Notifications");
                    Nav(Page.Display,       FontAwesomeIcon.Eye,        "Display");
                    Nav(Page.Travel,        FontAwesomeIcon.PlaneDeparture, "Travel");
                    Nav(Page.Hidden,        FontAwesomeIcon.EyeSlash,   "Hidden venues", _config.HiddenVenueCache.Count);
                    Nav(Page.About,         FontAwesomeIcon.InfoCircle, "About");
                }
                ImGui.PopStyleVar();
                ImGui.Unindent(6f * gs);
            }
        }

        ImGui.SameLine(0, 0);
        var lp0 = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lp0, lp0 + new Vector2(0f, ImGui.GetContentRegionAvail().Y), Palette.U(Palette.Line));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 18f * gs);

        using var page = ImRaii.Child("##cfgpage", Vector2.Zero, false);
        if (!page.Success) return;

        ImGui.Dummy(new Vector2(0f, 6f * gs));
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X - 12f * gs);
        switch (_page)
        {
            case Page.General:       DrawGeneral();       break;
            case Page.DataCenters:   DrawDataCenters();   break;
            case Page.Notifications: DrawNotifications(); break;
            case Page.Display:       DrawDisplay();       break;
            case Page.Travel:        DrawTravel();        break;
            case Page.Hidden:        DrawHidden();        break;
            case Page.About:         DrawAbout();         break;
        }
        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, 12f * gs));
    }

    private void Nav(Page page, FontAwesomeIcon icon, string label, int? count = null)
    {
        if (Widgets.NavItem($"##nav{page}", icon, null, label, count is > 0 ? count : null, _page == page))
            _page = page;
    }

    private float FieldWidth => Math.Min(420f * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X - 12f * ImGuiHelpers.GlobalScale);

    private void DrawGeneral()
    {
        Widgets.PageTitle("General", "Where events come from and how often they are loaded again.");

        Widgets.Group("Sources");
        var showP = _config.ShowPartakeEvents;
        if (Widgets.Toggle("##srcp", "Partake", ref showP, "Community events posted on partake.gg"))
        {
            _config.ShowPartakeEvents = showP;
            _config.Save();
            Task.Run(_cache.RefreshNowAsync);
        }
        var showF = _config.ShowFFXIVenueEvents;
        if (Widgets.Toggle("##srcf", "FFXIV Venues", ref showF, "Venues and their weekly openings from ffxivvenues.com"))
        {
            _config.ShowFFXIVenueEvents = showF;
            _config.Save();
            Task.Run(_cache.RefreshNowAsync);
        }
        var showSpot = _config.ShowSpotlight;
        if (Widgets.Toggle("##spot", "Spotlight banner", ref showSpot, "Featured venues at the top of the list"))
        {
            _config.ShowSpotlight = showSpot;
            _config.Save();
        }

        Widgets.Group("Refresh");
        ImGui.SetNextItemWidth(FieldWidth);
        var interval = _config.RefreshIntervalMinutes;
        if (ImGui.SliderInt("##interval", ref interval, 1, 60, interval == 1 ? "Every minute" : $"Every {interval} minutes"))
        {
            _config.RefreshIntervalMinutes = interval;
            _config.Save();
        }

        var pCount = _cache.CachedEvents.Count(e => e.Source == EventSource.Partake);
        var fCount = _cache.CachedEvents.Count(e => e.Source == EventSource.FFXIVenue);
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
        {
            ImGui.TextUnformatted(_cache.IsRefreshing
                ? "Loading..."
                : $"{pCount} Partake events and {fCount} venue openings loaded.");
        }

        ImGui.Dummy(new Vector2(0f, 2f * ImGuiHelpers.GlobalScale));
        if (Widgets.PillButton("##force", FontAwesomeIcon.Sync, "Refresh now", Palette.Accent))
            Task.Run(_cache.RefreshNowAsync);
        ImGui.SameLine(0, 8f * ImGuiHelpers.GlobalScale);
        if (Widgets.PillButton("##resetnew", FontAwesomeIcon.Certificate, "Show everything as new", Palette.Soon,
                "Forgets which events you already saw. Every event gets the NEW badge on the next refresh."))
        {
            _config.LastKnownEventIds = "[]";
            _config.Save();
        }
    }

    private void DrawDataCenters()
    {
        float gs = ImGuiHelpers.GlobalScale;
        Widgets.PageTitle("Data centers", "Which data centers the list shows when it opens.");

        var follow = _config.FollowCharacterRegion;
        if (Widgets.Toggle("##follow", "Follow my character", ref follow, "Picks the data centers of the region you are playing in"))
        {
            _config.FollowCharacterRegion = follow;
            _config.LastAutoRegion        = string.Empty;
            _config.Save();
        }

        ImGui.Dummy(new Vector2(0f, 4f * gs));
        foreach (var (region, wanted) in FollowedRegions)
        {
            var names = _partake.DataCenters.Values
                .Where(dc => wanted.Contains(dc.Region))
                .OrderBy(dc => Array.IndexOf(wanted, dc.Region)).ThenBy(dc => dc.Name)
                .Select(dc => dc.Name);
            bool here = region == Plugin.GetCurrentCharacterRegion();
            using (ImRaii.PushColor(ImGuiCol.Text, here ? Palette.AccentText : Palette.TextSoft))
                ImGui.TextUnformatted(here ? $"{region}  (you are here)" : region);
            ImGui.SameLine(190f * gs);
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                ImGui.TextUnformatted(string.Join(", ", names));
        }

        ImGui.Dummy(new Vector2(0f, 6f * gs));
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
            ImGui.TextWrapped("You can still pick other data centers at the top of the list. Your pick stays until you play in another region.");

        if (_config.FollowCharacterRegion)
        {
            ImGui.Dummy(new Vector2(0f, 4f * gs));
            if (Widgets.PillButton("##applyregion", FontAwesomeIcon.Crosshairs, "Use my region now", Palette.Accent))
            {
                _config.LastAutoRegion = string.Empty;
                _config.Save();
            }
        }
    }

    private void DrawNotifications()
    {
        float gs = ImGuiHelpers.GlobalScale;
        Widgets.PageTitle("Notifications", "Small messages in the corner of the screen.");

        var enable = _config.EnableNotifications;
        if (Widgets.Toggle("##notif", "New events", ref enable, "Tells you when an event appears on your data centers"))
        {
            _config.EnableNotifications = enable;
            _config.Save();
        }
        var syncPopup = _config.EnableSyncshellPopup;
        if (Widgets.Toggle("##syncpop", "Syncshell when entering a venue", ref syncPopup, "Shows the venue's syncshell when you walk into its house"))
        {
            _config.EnableSyncshellPopup = syncPopup;
            _config.Save();
        }

        if (!_config.EnableNotifications) return;

        Widgets.Group("Only for these data centers", "None picked means every data center.");
        for (int regionIdx = 1; regionIdx <= RegionNames.Length; regionIdx++)
        {
            var dcs = _partake.DataCenters.Values
                .Where(dc => dc.Region == regionIdx)
                .OrderBy(dc => dc.Name)
                .ToList();
            if (dcs.Count == 0) continue;

            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSoft))
                ImGui.TextUnformatted(RegionNames[regionIdx - 1]);
            ImGui.SameLine(130f * gs);

            foreach (var dc in dcs)
            {
                bool picked = _config.NotifyForDataCenters.Contains(dc.Name);
                if (Widgets.Chip($"##notif{dc.Name}", dc.Name, picked))
                {
                    if (picked) _config.NotifyForDataCenters.Remove(dc.Name);
                    else        _config.NotifyForDataCenters.Add(dc.Name);
                    _config.Save();
                }
                ImGui.SameLine(0, 4f * gs);
            }
            ImGui.NewLine();
        }
    }

    private void DrawDisplay()
    {
        Widgets.PageTitle("Display", "What the list looks like when it opens.");

        var hideEnded = _config.HideEndedEvents;
        if (Widgets.Toggle("##hideended", "Hide ended events", ref hideEnded, "Events whose end time has passed disappear from the list"))
        {
            _config.HideEndedEvents = hideEnded;
            _config.Save();
            _cache.TagsByDc.Clear();
        }

        Widgets.Group("Start on");
        int time = Math.Clamp(_config.DefaultTimeFilter, 0, 2);
        if (Widgets.Segment("##deftf", ["Everything", "Live now", "Today"], ref time, FieldWidth))
        {
            _config.DefaultTimeFilter = time;
            _config.Save();
        }

        Widgets.Group("Source shown first");
        int source = _config.DefaultSourceFilter + 1;
        if (Widgets.Segment("##defsrc", ["Both", "Partake", "FFXIV Venues"], ref source, FieldWidth))
        {
            _config.DefaultSourceFilter = source - 1;
            _config.Save();
        }
    }

    private void DrawTravel()
    {
        float gs = ImGuiHelpers.GlobalScale;
        Widgets.PageTitle("Travel", "The Go button uses Lifestream to take you to the venue, switching character when the venue is in another region.");

        bool lifestream = Plugin.IsLifestreamAvailable();
        var  col        = lifestream ? Palette.Live : Palette.Danger;
        var  p0         = ImGui.GetCursorScreenPos();
        string status   = lifestream ? "Lifestream is installed" : "Lifestream is not installed";
        var  ts         = ImGui.CalcTextSize(status);
        var  size       = new Vector2(ts.X + 30f * gs, ts.Y + 10f * gs);
        var  dl         = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p0 + size, Palette.U(col with { W = 0.14f }), size.Y / 2f);
        dl.AddCircleFilled(p0 + new Vector2(12f * gs, size.Y / 2f), 3.5f * gs, Palette.U(col));
        dl.AddText(p0 + new Vector2(22f * gs, 5f * gs), Palette.U(col), status);
        ImGui.Dummy(size);
        if (!lifestream)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                ImGui.TextWrapped("Install it from the Dalamud plugin installer to use the Go button.");
        }

        Widgets.Group("One character per region", "Used to log in on the right character when a venue is in another region. Write it as Name@World.");

        var player     = Plugin.ObjectTable.LocalPlayer;
        string current = player != null ? $"{player.Name.TextValue}@{player.HomeWorld.Value.Name.ExtractText()}" : string.Empty;
        string? currentRegion = Plugin.GetCurrentCharacterRegion();

        foreach (var region in CharacterRegions)
        {
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSoft))
                ImGui.TextUnformatted(region);
            ImGui.SameLine(130f * gs);

            _config.CharacterPerRegion.TryGetValue(region, out var saved);
            var buf = saved ?? string.Empty;
            ImGui.SetNextItemWidth(Math.Min(260f * gs, ImGui.GetContentRegionAvail().X - 60f * gs));
            if (ImGui.InputTextWithHint($"##{region}char", "Nala Praline@Moogle", ref buf, 64))
            {
                if (string.IsNullOrWhiteSpace(buf)) _config.CharacterPerRegion.Remove(region);
                else                                _config.CharacterPerRegion[region] = buf;
                _config.Save();
            }

            if (current.Length > 0 && region == currentRegion && buf != current)
            {
                ImGui.SameLine(0, 6f * gs);
                if (Widgets.IconButton($"##usecurrent{region}", FontAwesomeIcon.UserCheck, $"Use {current}"))
                {
                    _config.CharacterPerRegion[region] = current;
                    _config.Save();
                }
            }
        }
    }

    private void DrawHidden()
    {
        float gs = ImGuiHelpers.GlobalScale;
        Widgets.PageTitle("Hidden venues", "Venues you hid from the list. Their events come back when you unhide them.");

        if (_config.HiddenVenueCache.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                ImGui.TextUnformatted("Nothing hidden. Use the ... menu on an event to hide a venue.");
            return;
        }

        foreach (var (key, info) in _config.HiddenVenueCache.ToList())
        {
            var   srcColor = info.Source == EventSource.Partake ? Palette.Partake : Palette.FFXIVenue;
            float rowH     = ImGui.GetFrameHeight() + 10f * gs;
            float width    = ImGui.GetContentRegionAvail().X - 12f * gs;
            var   p0       = ImGui.GetCursorScreenPos();
            var   dl       = ImGui.GetWindowDrawList();

            dl.AddRectFilled(p0, p0 + new Vector2(width, rowH), Palette.U(Palette.Card), 8f * gs);
            dl.AddCircleFilled(p0 + new Vector2(14f * gs, rowH / 2f), 3.5f * gs, Palette.U(srcColor));

            string name  = !string.IsNullOrEmpty(info.Name) ? info.Name : key;
            string where = string.Join(", ", new[] { info.Server, info.DataCenter }.Where(s => !string.IsNullOrEmpty(s)));
            float  textY = p0.Y + (rowH - ImGui.GetTextLineHeight()) / 2f;
            float  btnW  = Widgets.PillWidth(FontAwesomeIcon.Eye, "Unhide");
            string shown = Widgets.Ellipsize(name, width - btnW - 160f * gs);
            dl.AddText(new Vector2(p0.X + 28f * gs, textY), Palette.U(Palette.Text), shown);
            if (where.Length > 0)
                dl.AddText(new Vector2(p0.X + 36f * gs + ImGui.CalcTextSize(shown).X, textY), Palette.U(Palette.Muted), where);

            ImGui.SetCursorScreenPos(new Vector2(p0.X + width - btnW - 6f * gs, p0.Y + 5f * gs));
            if (Widgets.PillButton($"##unhide{key}", FontAwesomeIcon.Eye, "Unhide", Palette.Live, string.Empty, ImGui.GetFrameHeight()))
            {
                if (info.Source == EventSource.Partake) _config.HiddenPartakeTeamIds.Remove(info.TeamId);
                else                                    _config.HiddenVenueIds.Remove(info.VenueId);
                _config.HiddenVenueCache.Remove(key);
                _config.Save();
                _cache.TagsByDc.Clear();
            }

            ImGui.SetCursorScreenPos(new Vector2(p0.X, p0.Y + rowH + 4f * gs));
        }
    }

    private void DrawAbout()
    {
        float gs = ImGuiHelpers.GlobalScale;

        string version = "unknown";
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo
                .GetVersionInfo(Plugin.PluginInterface.AssemblyLocation.FullName);
            version = fvi.FileVersion ?? "unknown";
        }
        catch { }

        Widgets.PageTitle("VenueScope", $"Version {version}. Community events and venues of FFXIV, in game.");

        Widgets.Group("Sources");
        if (Widgets.PillButton("##lpartake", FontAwesomeIcon.ExternalLinkAlt, "Partake.gg", Palette.Partake))
            Dalamud.Utility.Util.OpenLink("https://www.partake.gg/");
        ImGui.SameLine(0, 8f * gs);
        if (Widgets.PillButton("##lvenues", FontAwesomeIcon.ExternalLinkAlt, "FFXIV Venues", Palette.FFXIVenue))
            Dalamud.Utility.Util.OpenLink("https://ffxivvenues.com/");

        Widgets.Group("Contact", "A bug, an idea, or your event in the spotlight.");
        if (Widgets.PillButton("##ldiscord", FontAwesomeIcon.CommentDots, "Discord", Palette.Accent))
            Dalamud.Utility.Util.OpenLink("https://discordid.netlify.app/?id=249633834646241281");
        ImGui.SameLine(0, 8f * gs);
        if (Widgets.PillButton("##lx", FontAwesomeIcon.ExternalLinkAlt, "X / Twitter", Palette.TextSoft))
            Dalamud.Utility.Util.OpenLink("https://x.com/MoroOkami");
    }

    public void Dispose() { }
}
