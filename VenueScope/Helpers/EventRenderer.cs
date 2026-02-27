using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System.Numerics;
using VenueScope.Models;

namespace VenueScope.Helpers;

/// <summary>
/// Renders a styled event card: dark background panel, source-color left
/// accent, status dot, compact two-line layout, and right-aligned actions.
/// </summary>
public static class EventRenderer
{
    // ── Palette ──────────────────────────────────────────────────────────────
    private static readonly Vector4 ColPartake   = new(0.33f, 0.58f, 0.96f, 1f);
    private static readonly Vector4 ColFFXIVenue = new(0.62f, 0.32f, 0.92f, 1f);

    private static readonly Vector4 ColTitle     = new(0.94f, 0.94f, 1.00f, 1f);
    private static readonly Vector4 ColMuted     = new(0.46f, 0.46f, 0.54f, 1f);
    private static readonly Vector4 ColBullet    = new(0.28f, 0.28f, 0.36f, 1f);
    private static readonly Vector4 ColLocation  = new(0.36f, 0.76f, 0.52f, 1f);
    private static readonly Vector4 ColTimeLive  = new(0.20f, 0.86f, 0.42f, 1f);
    private static readonly Vector4 ColTimeSoon  = new(1.00f, 0.66f, 0.12f, 1f);
    private static readonly Vector4 ColTimeEnded = new(0.38f, 0.38f, 0.44f, 1f);
    private static readonly Vector4 ColTimeFut   = new(0.52f, 0.74f, 1.00f, 1f);
    private static readonly Vector4 ColNew       = new(1.00f, 0.80f, 0.16f, 1f);
    private static readonly Vector4 ColCardBg    = new(0.15f, 0.15f, 0.22f, 1.00f);

    // ── Public entry point ────────────────────────────────────────────────────

    public static void DrawEventCard(VenueEvent ev, CachedEventStrings cached)
    {
        float   gs          = ImGuiHelpers.GlobalScale;
        Vector4 srcColor    = ev.Source == EventSource.Partake ? ColPartake : ColFFXIVenue;
        Vector4 statusColor = StatusColor(cached);

        // Capture card bounds before drawing anything
        float cardW  = ImGui.GetContentRegionAvail().X;
        var   cardTL = ImGui.GetCursorScreenPos();
        var   dl     = ImGui.GetWindowDrawList();
        float padX   = 16f * gs;
        float padY   = 7f  * gs;

        // Channel 1 = content (foreground), channel 0 = background
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        // ── Top padding + status dot ──────────────────────────────────────────
        ImGui.Dummy(new Vector2(0f, padY));

        var dotCenter = cardTL + new Vector2(8f * gs, padY + ImGui.GetTextLineHeight() * 0.52f);
        dl.AddCircleFilled(dotCenter, 3.5f * gs,
            ImGui.ColorConvertFloat4ToU32(statusColor));

        ImGui.Indent(padX);

        // ── Row 1 : title + action buttons ───────────────────────────────────
        DrawTitleRow(ev, cached, srcColor);

        // ── Row 2 : info line ─────────────────────────────────────────────────
        DrawInfoRow(ev, cached, srcColor, statusColor);

        // ── Row 3 : tags ─────────────────────────────────────────────────────
        if (cached.Tags.Length > 0)
            DrawTags(ev.Id, cached.Tags, srcColor);

        // ── Row 4 : description (collapsible) ────────────────────────────────
        if (!string.IsNullOrEmpty(ev.Description))
            DrawDescription(ev);

        ImGui.Dummy(new Vector2(0f, padY)); // bottom padding
        ImGui.Unindent(padX);

        // ── Background + left accent (channel 0 = rendered behind content) ───
        var cardBR = new Vector2(cardTL.X + cardW, ImGui.GetCursorScreenPos().Y);

        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(cardTL, cardBR,
            ImGui.ColorConvertFloat4ToU32(ColCardBg), 6f * gs);
        dl.AddRectFilled(
            cardTL + new Vector2(0f, 4f * gs),
            new Vector2(cardTL.X + 3f * gs, cardBR.Y - 4f * gs),
            ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.90f }), 2f);

        dl.ChannelsMerge();
    }

    // ── Rows ──────────────────────────────────────────────────────────────────

    private static void DrawTitleRow(VenueEvent ev, CachedEventStrings cached, Vector4 srcColor)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        float actW  = CalcActionsWidth(ev);
        float avail = ImGui.GetContentRegionAvail().X - actW - 6f * gs;
        float lineH = ImGui.GetTextLineHeight();

        // Clip title so it never wraps
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.PushClipRect(p0, p0 + new Vector2(avail, lineH + 2f), true);

        using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
            ImGui.TextUnformatted(ev.Title.Length > 0 ? ev.Title : "(no title)");

        ImGui.PopClipRect();

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(ev.EventUrl))
        {
            ImGui.SetTooltip("Click to open");
            if (ImGui.IsItemClicked())
                Util.OpenLink(ev.EventUrl);
        }

        if (ev.IsNew)
        {
            ImGui.SameLine(0, 8);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ColNew);
            ImGui.TextUnformatted("NEW");
        }

        DrawActions(ev, actW);
    }

    private static void DrawInfoRow(VenueEvent ev, CachedEventStrings cached,
                                    Vector4 srcColor, Vector4 statusColor)
    {
        // Source
        using (ImRaii.PushColor(ImGuiCol.Text, srcColor with { W = 0.50f }))
            ImGui.TextUnformatted(ev.Source == EventSource.Partake ? "Partake" : "FFXIVenue");

        // Server / DC
        if (!string.IsNullOrEmpty(cached.ServerDc))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, ColLocation))
                ImGui.TextUnformatted(cached.ServerDc);
        }

        // In-game location (click to copy)
        if (!string.IsNullOrEmpty(cached.Location))
        {
            Dot();
            float locW = ImGui.CalcTextSize(cached.Location).X;
            using (ImRaii.PushColor(ImGuiCol.Text, ColLocation with { W = 0.70f }))
            {
                if (ImGui.Selectable($"{cached.Location}##loc{ev.Id}", false,
                        ImGuiSelectableFlags.None, new Vector2(locW, 0)))
                    ImGui.SetClipboardText(string.IsNullOrEmpty(cached.ServerDc)
                        ? cached.Location
                        : $"{cached.ServerDc} \u2013 {cached.Location}");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Click to copy");
            }
        }

        // Host
        if (!string.IsNullOrEmpty(ev.Host))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextUnformatted($"by {ev.Host}");
        }

        // Attendees
        if (ev.AttendeeCount > 0)
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextUnformatted($"{ev.AttendeeCount} attending");
        }

        // Time
        Dot();
        string timeStr = $"{cached.StartsAtLocal} \u2192 {cached.EndsAtLocal}";
        using (ImRaii.PushColor(ImGuiCol.Text, statusColor with { W = 0.85f }))
            ImGui.TextUnformatted(timeStr);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Starts {cached.StartsAtHumanized}\nEnds {cached.EndsAtHumanized}");
    }

    private static void DrawTags(string evId, string[] tags, Vector4 srcColor)
    {
        ImGui.Spacing();
        var bg  = srcColor with { W = 0.16f };
        var bgh = srcColor with { W = 0.30f };
        var txt = srcColor with { W = 0.82f };

        for (int i = 0; i < tags.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, 4);
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        bg);
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, bgh);
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  bgh);
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          txt);
            ImGui.SmallButton($" {tags[i]} ##{evId}t{i}");
        }
    }

    private static void DrawDescription(VenueEvent ev)
    {
        ImGui.Spacing();
        using var c1 = ImRaii.PushColor(ImGuiCol.Header,        new Vector4(0.14f, 0.14f, 0.22f, 0.00f));
        using var c2 = ImRaii.PushColor(ImGuiCol.HeaderHovered, new Vector4(0.20f, 0.20f, 0.30f, 1.00f));
        using var c3 = ImRaii.PushColor(ImGuiCol.Text,          ColMuted);
        if (ImGui.CollapsingHeader($"Description##{ev.Id}"))
        {
            using var c4 = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.82f, 1f));
            ImGui.TextWrapped(ev.Description);
        }
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private static float CalcActionsWidth(VenueEvent ev)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        float spc = ImGui.GetStyle().ItemSpacing.X;
        float w   = 0f;
        if (!string.IsNullOrEmpty(ev.EventUrl))       w += 52f * gs + spc;
        if (!string.IsNullOrEmpty(ev.LifestreamCode)) w += 40f * gs + spc;
        return w;
    }

    private static void DrawActions(VenueEvent ev, float reservedW)
    {
        if (reservedW <= 0f) return;

        float rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(rightEdge - reservedW);

        if (!string.IsNullOrEmpty(ev.EventUrl))
        {
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.16f, 0.30f, 0.54f, 0.65f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.42f, 0.72f, 0.90f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.28f, 0.52f, 0.88f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.72f, 0.86f, 1.00f, 1.00f));
            if (ImGui.SmallButton($" Open ##{ev.Id}"))
                Util.OpenLink(ev.EventUrl);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open event page");
            ImGui.SameLine(0, 4);
        }

        if (!string.IsNullOrEmpty(ev.LifestreamCode))
        {
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.30f, 0.12f, 0.50f, 0.65f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.42f, 0.18f, 0.68f, 0.90f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.52f, 0.24f, 0.82f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.84f, 0.70f, 1.00f, 1.00f));
            if (ImGui.SmallButton($" /li ##{ev.Id}"))
                ImGui.SetClipboardText($"/li {ev.LifestreamCode}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"/li {ev.LifestreamCode}\n\nPaste in FFXIV chat to teleport.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void Dot()
    {
        ImGui.SameLine(0, 6);
        using (ImRaii.PushColor(ImGuiCol.Text, ColBullet))
            ImGui.TextUnformatted("\u00b7");
        ImGui.SameLine(0, 6);
    }

    private static Vector4 StatusColor(CachedEventStrings c) =>
        c.IsLive ? ColTimeLive : c.IsStartingSoon ? ColTimeSoon :
        c.HasEnded ? ColTimeEnded : ColTimeFut;
}
