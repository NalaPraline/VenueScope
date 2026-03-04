using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using VenueScope.Services;

namespace VenueScope.UI;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration     _config;
    private readonly PartakeService    _partake;
    private readonly EventCacheService _cache;

    private static readonly string[] RegionNames = ["Japan", "North America", "Europe", "Oceania"];

    private static readonly Vector4 ColAccent   = new(0.40f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSubtitle = new(0.55f, 0.55f, 0.65f, 1f);
    private static readonly Vector4 ColGreen    = new(0.22f, 0.80f, 0.44f, 1f);
    private static readonly Vector4 ColRed      = new(0.90f, 0.30f, 0.30f, 1f);
    private static readonly Vector4 ColOrange   = new(1.00f, 0.68f, 0.14f, 1f);

    public ConfigWindow(Configuration config, PartakeService partake, EventCacheService cache)
        : base("VenueScope — Settings##cfg", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar)
    {
        _config  = config;
        _partake = partake;
        _cache   = cache;

        Size            = new Vector2(480, 620);
        SizeCondition   = ImGuiCond.Always;
    }

    public override void Draw()
    {
        using var scrollChild = ImRaii.Child("##cfgscroll", Vector2.Zero, false);
        if (!scrollChild.Success) return;

        DrawSectionSources();
        ImGui.Spacing();
        DrawSectionRefresh();
        ImGui.Spacing();
        DrawSectionNotifications();
        ImGui.Spacing();
        DrawSectionDisplay();
        ImGui.Spacing();
        DrawSectionIntegrations();
        ImGui.Spacing();
        DrawSectionHiddenVenues();
        ImGui.Spacing();
        DrawSectionAbout();
    }

    // ══ Sources ═══════════════════════════════════════════════════════════

    private void DrawSectionSources()
    {
        if (!SectionHeader("  Sources")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        var showP = _config.ShowPartakeEvents;
        if (ImGui.Checkbox("Partake.gg", ref showP) && showP != _config.ShowPartakeEvents)
        {
            _config.ShowPartakeEvents = showP;
            _config.Save();
            Task.Run(_cache.RefreshNowAsync);
        }
        ImGui.SameLine(0, 8);
        ImGui.TextColored(ColSubtitle, "— upcoming community events");

        var showF = _config.ShowFFXIVenueEvents;
        if (ImGui.Checkbox("FFXIV Venues", ref showF) && showF != _config.ShowFFXIVenueEvents)
        {
            _config.ShowFFXIVenueEvents = showF;
            _config.Save();
            Task.Run(_cache.RefreshNowAsync);
        }
        ImGui.SameLine(0, 8);
        ImGui.TextColored(ColSubtitle, "— recurring venue openings");

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ Refresh ════════════════════════════════════════════════════════════

    private void DrawSectionRefresh()
    {
        if (!SectionHeader("  Refresh")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        ImGui.TextColored(ColSubtitle, "Auto-refresh interval");
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        var interval = _config.RefreshIntervalMinutes;
        if (ImGui.SliderInt("minutes##interval", ref interval, 1, 60))
        {
            _config.RefreshIntervalMinutes = interval;
            _config.Save();
        }

        ImGui.Spacing();

        var pCount = _cache.CachedEvents.Count(e => e.Source == Models.EventSource.Partake);
        var fCount = _cache.CachedEvents.Count(e => e.Source == Models.EventSource.FFXIVenue);
        ImGui.TextColored(ColSubtitle,
            _cache.IsRefreshing
                ? "  Refreshing..."
                : $"  {pCount} Partake  ·  {fCount} FFXIV Venues events cached");

        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.18f, 0.36f, 0.60f, 0.85f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.48f, 0.78f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.34f, 0.60f, 0.96f, 1.00f)))
        {
            if (ImGui.Button("  Force refresh now  ##forcerefresh"))
                Task.Run(_cache.RefreshNowAsync);
        }

        ImGui.SameLine(0, 12);

        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.45f, 0.28f, 0.08f, 0.85f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.62f, 0.38f, 0.10f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.80f, 0.50f, 0.12f, 1.00f)))
        {
            if (ImGui.Button("  Reset NEW badges  ##resetnew"))
            {
                _config.LastKnownEventIds = "[]";
                _config.Save();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clears the list of known event IDs.\nAll events will appear as [NEW] on next refresh.");

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ Notifications ══════════════════════════════════════════════════════

    private void DrawSectionNotifications()
    {
        if (!SectionHeader("  Notifications")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        var enable = _config.EnableNotifications;
        if (ImGui.Checkbox("Enable notifications for new events", ref enable))
        {
            _config.EnableNotifications = enable;
            _config.Save();
        }

        if (_config.EnableNotifications)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColSubtitle, "Notify for data centers:");
            ImGui.TextColored(ColSubtitle with { W = 0.6f },
                "(leave all unchecked to notify for every DC)");
            ImGui.Spacing();

            for (int regionIdx = 1; regionIdx <= RegionNames.Length; regionIdx++)
            {
                var regionName = RegionNames[regionIdx - 1];
                var dcs = _partake.DataCenters.Values
                    .Where(dc => dc.Region == regionIdx)
                    .OrderBy(dc => dc.Name)
                    .ToList();
                if (dcs.Count == 0) continue;

                ImGui.TextColored(ColAccent, regionName);
                ImGui.SameLine(0, 8);

                foreach (var dc in dcs)
                {
                    bool checked_ = _config.NotifyForDataCenters.Contains(dc.Name);
                    if (ImGui.Checkbox($"{dc.Name}##{dc.Name}notif", ref checked_))
                    {
                        if (checked_) _config.NotifyForDataCenters.Add(dc.Name);
                        else          _config.NotifyForDataCenters.Remove(dc.Name);
                        _config.Save();
                    }
                    ImGui.SameLine(0, 6);
                }
                ImGui.NewLine();
            }
        }

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ Display ════════════════════════════════════════════════════════════

    private void DrawSectionDisplay()
    {
        if (!SectionHeader("  Display")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        var hideEnded = _config.HideEndedEvents;
        if (ImGui.Checkbox("Hide ended events", ref hideEnded))
        {
            _config.HideEndedEvents = hideEnded;
            _config.Save();
            _cache.TagsByDc.Clear(); // Rebuild tag lists without ended events
        }

        ImGui.Spacing();
        ImGui.TextColored(ColSubtitle, "Default time filter on open:");
        if (DrawRadio("All##deftf",      _config.DefaultTimeFilter, 0)) { _config.DefaultTimeFilter = 0; _config.Save(); }
        ImGui.SameLine(0, 12);
        if (DrawRadio("Live Now##deftf", _config.DefaultTimeFilter, 1)) { _config.DefaultTimeFilter = 1; _config.Save(); }
        ImGui.SameLine(0, 12);
        if (DrawRadio("Today##deftf",    _config.DefaultTimeFilter, 2)) { _config.DefaultTimeFilter = 2; _config.Save(); }

        ImGui.Spacing();
        ImGui.TextColored(ColSubtitle, "Default source filter on open:");
        if (DrawRadio("All##defsrc",       _config.DefaultSourceFilter, -1)) { _config.DefaultSourceFilter = -1; _config.Save(); }
        ImGui.SameLine(0, 12);
        if (DrawRadio("Partake##defsrc",   _config.DefaultSourceFilter,  0)) { _config.DefaultSourceFilter =  0; _config.Save(); }
        ImGui.SameLine(0, 12);
        if (DrawRadio("FFXIV Venues##defsrc", _config.DefaultSourceFilter,  1)) { _config.DefaultSourceFilter =  1; _config.Save(); }

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ Integrations ═══════════════════════════════════════════════════════

    private static void DrawSectionIntegrations()
    {
        if (!SectionHeader("  Integrations")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        bool lifestreamAvail = Plugin.IsLifestreamAvailable();
        var  statusColor     = lifestreamAvail ? ColGreen : ColRed;
        var  statusLabel     = lifestreamAvail
            ? "Lifestream — installed"
            : "Lifestream — not installed";
        var  statusHint = lifestreamAvail
            ? "Teleport buttons will use Lifestream to travel directly to venue locations."
            : "Install Lifestream from the Dalamud plugin installer to enable in-game teleport buttons.";

        using (ImRaii.PushColor(ImGuiCol.Text, statusColor))
            ImGui.TextUnformatted(statusLabel);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(statusHint);

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ About ══════════════════════════════════════════════════════════════

    private void DrawSectionAbout()
    {
        if (!SectionHeader("  About")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        string version = "unknown";
        try
        {
            var fvi = System.Diagnostics.FileVersionInfo
                .GetVersionInfo(Plugin.PluginInterface.AssemblyLocation.FullName);
            version = fvi.FileVersion ?? "unknown";
        }
        catch { }

        ImGui.TextColored(ColSubtitle, $"VenueScope  v{version}");
        ImGui.Spacing();

        DrawLinkButton("Partake.gg",  "https://www.partake.gg/",  new Vector4(0.33f, 0.58f, 0.96f, 1f));
        ImGui.SameLine(0, 8);
        DrawLinkButton("FFXIV Venues", "https://ffxivvenues.com/", new Vector4(0.62f, 0.32f, 0.92f, 1f));

        ImGui.Spacing();

        DrawLinkButton("Discord", "https://discordid.netlify.app/?id=249633834646241281",
            new Vector4(0.44f, 0.54f, 0.90f, 1f));
        ImGui.SameLine(0, 8);
        DrawLinkButton("X / Twitter", "https://x.com/MoroOkami",
            new Vector4(0.80f, 0.80f, 0.80f, 1f));

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ Hidden Venues ══════════════════════════════════════════════════════

    private void DrawSectionHiddenVenues()
    {
        if (!SectionHeader("  Hidden Venues")) return;
        ImGui.Indent(12f * ImGuiHelpers.GlobalScale);

        if (_config.HiddenVenueCache.Count == 0)
        {
            ImGui.TextColored(ColSubtitle, "No hidden venues.");
            ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
            return;
        }

        foreach (var (key, info) in _config.HiddenVenueCache.ToList())
        {
            var    srcColor = info.Source == Models.EventSource.Partake
                ? new Vector4(0.33f, 0.58f, 0.96f, 1f)
                : new Vector4(0.62f, 0.32f, 0.92f, 1f);
            string srcLabel = info.Source == Models.EventSource.Partake ? "[Partake]" : "[FFXIV Venues]";

            using (ImRaii.PushColor(ImGuiCol.Text, srcColor with { W = 0.65f }))
                ImGui.TextUnformatted(srcLabel);
            ImGui.SameLine(0, 6);

            string displayName = !string.IsNullOrEmpty(info.Name) ? info.Name : key;
            ImGui.TextUnformatted(displayName);
            ImGui.SameLine(0, 8);

            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.14f, 0.28f, 0.14f, 0.70f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.42f, 0.20f, 0.90f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.28f, 0.56f, 0.28f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.50f, 1.00f, 0.55f, 1.00f));
            if (ImGui.SmallButton($" Unhide ##{key}"))
            {
                if (info.Source == Models.EventSource.Partake)
                    _config.HiddenPartakeTeamIds.Remove(info.TeamId);
                else
                    _config.HiddenVenueIds.Remove(info.VenueId);
                _config.HiddenVenueCache.Remove(key);
                _config.Save();
                _cache.TagsByDc.Clear();
            }
        }

        ImGui.Unindent(12f * ImGuiHelpers.GlobalScale);
    }

    // ══ Helpers ════════════════════════════════════════════════════════════

    /// <summary>Draws a colored collapsing header. Returns true if the section is open.</summary>
    private static bool SectionHeader(string label)
    {
        using var col  = ImRaii.PushColor(ImGuiCol.Header,        new Vector4(0.14f, 0.18f, 0.28f, 1f));
        using var col2 = ImRaii.PushColor(ImGuiCol.HeaderHovered, new Vector4(0.20f, 0.25f, 0.38f, 1f));
        using var col3 = ImRaii.PushColor(ImGuiCol.HeaderActive,  new Vector4(0.26f, 0.32f, 0.50f, 1f));
        using var col4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.80f, 0.85f, 1.00f, 1f));
        bool open = ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.Spacing();
        return open;
    }

    private static bool DrawRadio(string label, int current, int value)
        => ImGui.RadioButton(label, current == value);

    private static void DrawLinkButton(string label, string url, Vector4 color)
    {
        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        color with { W = 0.25f });
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, color with { W = 0.45f });
        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  color with { W = 0.65f });
        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          color);
        if (ImGui.SmallButton($" {label} ##{label}"))
            Dalamud.Utility.Util.OpenLink(url);
    }

    public void Dispose() { }
}
