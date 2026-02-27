using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using VenueScope.Helpers;
using VenueScope.Models;
using VenueScope.Services;

namespace VenueScope.UI;

public sealed class MapWindow : Window, IDisposable
{
    private readonly EventCacheService _cache;

    private HousingZone _zone      = HousingZone.Mist;
    private string?     _focusedId = null;

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Vector4 ColLive    = new(0.20f, 0.86f, 0.42f, 1f);
    private static readonly Vector4 ColSoon    = new(1.00f, 0.72f, 0.28f, 1f);
    private static readonly Vector4 ColFuture  = new(0.52f, 0.78f, 1.00f, 1f);
    private static readonly Vector4 ColMuted   = new(0.46f, 0.46f, 0.54f, 1f);
    private static readonly Vector4 ColTitle   = new(0.94f, 0.94f, 1.00f, 1f);
    private static readonly Vector4 ColCardBg  = new(0.10f, 0.10f, 0.16f, 0.96f);
    private static readonly Vector4 ColSideBg  = new(0.09f, 0.09f, 0.14f, 1f);

    private const float ListW = 260f; // unscaled

    public MapWindow(EventCacheService cache)
        : base("VenueScope \u2014 Map##mapwin", ImGuiWindowFlags.NoScrollbar)
    {
        _cache = cache;

        Size            = new Vector2(1060, 680);
        SizeCondition   = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(720, 500),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    public override void Draw()
    {
        float gs = ImGuiHelpers.GlobalScale;

        // ── Zone tabs ─────────────────────────────────────────────────────────
        using (ImRaii.PushColor(ImGuiCol.Tab,        new Vector4(0.12f, 0.12f, 0.18f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.TabActive,  new Vector4(0.18f, 0.24f, 0.40f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.TabHovered, new Vector4(0.16f, 0.20f, 0.34f, 1f)))
        {
            if (ImGui.BeginTabBar("##zonetabs"))
            {
                for (int i = 0; i < HousingMapService.ZoneNames.Length; i++)
                {
                    var z = (HousingZone)i;
                    if (ImGui.BeginTabItem(HousingMapService.ZoneNames[i] + $"##zt{i}"))
                    {
                        if (_zone != z) { _zone = z; _focusedId = null; }
                        DrawZoneContent(z);
                        ImGui.EndTabItem();
                    }
                }
                ImGui.EndTabBar();
            }
        }
    }

    // ══ Zone layout ══════════════════════════════════════════════════════════

    private void DrawZoneContent(HousingZone zone)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        float avail = ImGui.GetContentRegionAvail().X;
        float totalH = ImGui.GetContentRegionAvail().Y;
        float listW = ListW * gs;
        float mapW  = avail - listW - 6f * gs;

        // Map view
        using (var c = ImRaii.Child("##mapview", new Vector2(mapW, totalH), false,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (c.Success) DrawMapPanel(zone, mapW, totalH);
        }

        ImGui.SameLine(0, 6f * gs);

        // Side panel
        using var side = ImRaii.PushColor(ImGuiCol.ChildBg, ColSideBg);
        using var sc   = ImRaii.Child("##sidepanel", new Vector2(listW, totalH), false);
        if (sc.Success) DrawSidePanel(zone);
    }

    // ══ Map panel ════════════════════════════════════════════════════════════

    private void DrawMapPanel(HousingZone zone, float panelW, float panelH)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        var   dl   = ImGui.GetWindowDrawList();
        var   orig = ImGui.GetCursorScreenPos();

        // Dark background
        dl.AddRectFilled(orig, orig + new Vector2(panelW, panelH),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.07f, 0.10f, 1f)), 4f * gs);

        var tex = HousingMapService.GetMapTexture(zone);
        if (tex == null || !tex.TryGetWrap(out var wrap, out _) || wrap == null)
        {
            ImGui.SetCursorScreenPos(orig + new Vector2(12f * gs, 12f * gs));
            ImGui.TextColored(ColMuted, "Loading map texture\u2026");
            ImGui.SetCursorScreenPos(orig + new Vector2(0, panelH));
            return;
        }

        // Center the map inside the panel, preserving aspect ratio
        float scale  = Math.Min(panelW / wrap.Width, panelH / wrap.Height);
        var   size   = new Vector2(wrap.Width * scale, wrap.Height * scale);
        var   offset = new Vector2((panelW - size.X) * 0.5f, (panelH - size.Y) * 0.5f);
        var   imgTL  = orig + offset;

        ImGui.SetCursorScreenPos(imgTL);
        ImGui.Image(wrap.Handle, size);

        DrawEventDots(zone, imgTL, size, dl, gs);

        // Reset cursor to end of panel
        ImGui.SetCursorScreenPos(orig + new Vector2(0, panelH));
    }

    private void DrawEventDots(HousingZone zone, Vector2 imgTL, Vector2 imgSize,
                                ImDrawListPtr dl, float gs)
    {
        var events = GetZoneEvents(zone);
        var now    = DateTime.UtcNow;

        foreach (var ev in events)
        {
            if (!TryGetPixelPos(ev, imgTL, imgSize, out var px)) continue;

            bool   focused = ev.Id == _focusedId;
            var    col     = StatusColor(ev, now);
            uint   fill    = ImGui.ColorConvertFloat4ToU32(col);
            uint   ring    = ImGui.ColorConvertFloat4ToU32(focused
                                 ? new Vector4(1f, 1f, 1f, 1f)
                                 : col with { W = 0.55f });

            float r = focused ? 9f * gs : 6f * gs;
            dl.AddCircleFilled(px, r,      fill);
            dl.AddCircle      (px, r + 2f, ring, 0, 1.5f * gs);

            // Glow for live events
            if (col == ColLive)
                dl.AddCircleFilled(px, r + 5f,
                    ImGui.ColorConvertFloat4ToU32(col with { W = 0.18f }));

            // Hover / click
            if (Vector2.Distance(ImGui.GetMousePos(), px) <= r + 4f)
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(ColTitle, ev.Title);
                ImGui.TextColored(ColMuted, ev.InGameLocation);
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    _focusedId = _focusedId == ev.Id ? null : ev.Id;
            }
        }
    }

    // ══ Side panel ════════════════════════════════════════════════════════════

    private void DrawSidePanel(HousingZone zone)
    {
        float gs     = ImGuiHelpers.GlobalScale;
        var   events = GetZoneEvents(zone);
        var   now    = DateTime.UtcNow;

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);

        using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
            ImGui.TextUnformatted($"{HousingMapService.ZoneNames[(int)zone]}");

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);
        using (ImRaii.PushColor(ImGuiCol.Text, ColMuted with { W = 0.5f }))
            ImGui.TextUnformatted($"{events.Count} event{(events.Count != 1 ? "s" : "")} on this zone");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (events.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);
            ImGui.TextColored(ColMuted with { W = 0.40f }, "No events found here.");
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * gs);
            ImGui.TextColored(ColMuted with { W = 0.30f }, "Try reloading.");
            return;
        }

        using var scroll = ImRaii.Child("##evscroll", Vector2.Zero, false);
        if (!scroll.Success) return;

        foreach (var ev in events)
        {
            bool   focused = ev.Id == _focusedId;
            var    col     = StatusColor(ev, now);

            // Status bar on left
            var dl = ImGui.GetWindowDrawList();
            var p0 = ImGui.GetCursorScreenPos();
            float rowH = 52f * gs;
            dl.AddRectFilled(p0, p0 + new Vector2(3f * gs, rowH),
                ImGui.ColorConvertFloat4ToU32(col with { W = focused ? 1f : 0.6f }));

            // Card background when focused
            if (focused)
                dl.AddRectFilled(p0 + new Vector2(3f * gs, 0),
                    p0 + new Vector2(ImGui.GetContentRegionAvail().X, rowH),
                    ImGui.ColorConvertFloat4ToU32(ColCardBg), 3f * gs);

            ImGui.Dummy(new Vector2(3f * gs, 0));
            ImGui.SameLine(0, 6f * gs);

            using (ImRaii.PushColor(ImGuiCol.Header,        focused ? ColCardBg : Vector4.Zero))
            using (ImRaii.PushColor(ImGuiCol.HeaderHovered, ColCardBg with { W = 0.80f }))
            {
                if (ImGui.Selectable($"##sid{ev.Id}", focused,
                        ImGuiSelectableFlags.None, new Vector2(0, rowH - 4f * gs)))
                    _focusedId = focused ? null : ev.Id;
            }

            ImGui.SameLine(10f * gs);
            ImGui.BeginGroup();

            using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
            {
                string title = ev.Title.Length > 22
                    ? ev.Title[..21] + "\u2026" : ev.Title;
                ImGui.TextUnformatted(title);
            }

            using (ImRaii.PushColor(ImGuiCol.Text, col with { W = 0.80f }))
            {
                string loc = ev.InGameLocation.Length > 26
                    ? ev.InGameLocation[..25] + "\u2026" : ev.InGameLocation;
                ImGui.TextUnformatted(loc);
            }

            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted with { W = 0.60f }))
            {
                string src = ev.Source == EventSource.Partake ? "Partake" : "FFXIVenue";
                ImGui.TextUnformatted(src);
            }

            ImGui.EndGroup();
            ImGui.Spacing();
        }
    }

    // ══ Helpers ══════════════════════════════════════════════════════════════

    private List<VenueEvent> GetZoneEvents(HousingZone zone) =>
        _cache.CachedEvents
            .Where(e => LocationParser.TryParseHousing(
                e.InGameLocation, out var z, out _, out _) && z == zone)
            .OrderBy(e => e.StartTime)
            .ToList();

    private static bool TryGetPixelPos(VenueEvent ev, Vector2 imgTL, Vector2 imgSize,
                                        out Vector2 pixel)
    {
        pixel = default;
        if (!LocationParser.TryParseHousing(ev.InGameLocation, out _, out var ward, out _))
            return false;

        var mc = HousingMapService.WardMapCoord(ward);

        // FFXIV map coords are in 1–42 range
        float nx = (mc.X - 1f) / 41f;
        float ny = (mc.Y - 1f) / 41f;

        pixel = imgTL + new Vector2(nx * imgSize.X, ny * imgSize.Y);
        return true;
    }

    private static Vector4 StatusColor(VenueEvent ev, DateTime utcNow)
    {
        var start = ev.StartTime.ToUniversalTime();
        var end   = ev.EndTime?.ToUniversalTime();

        if (start <= utcNow && (end == null || end.Value > utcNow)) return ColLive;
        if (start > utcNow && (start - utcNow).TotalHours <= 2.0)  return ColSoon;
        return ColFuture;
    }

    public void Dispose() { }
}
