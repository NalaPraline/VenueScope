using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System;
using System.Numerics;
using VenueScope.Models;

namespace VenueScope.Helpers;

/// <summary>
/// Renders a styled event card with colored left border, source badge,
/// time status, tag pills, and action buttons.
/// </summary>
public static class EventRenderer
{
    // ── Palette ──────────────────────────────────────────────────────────────
    private static readonly Vector4 ColPartake   = new(0.33f, 0.58f, 0.96f, 1f); // blue
    private static readonly Vector4 ColFFXIVenue = new(0.62f, 0.32f, 0.92f, 1f); // purple

    private static readonly Vector4 ColTitle     = new(0.95f, 0.95f, 1.00f, 1f);
    private static readonly Vector4 ColHost      = new(0.70f, 0.70f, 0.75f, 1f);
    private static readonly Vector4 ColLocation  = new(0.42f, 0.82f, 0.56f, 1f);
    private static readonly Vector4 ColTimeLive  = new(0.22f, 0.90f, 0.44f, 1f); // green
    private static readonly Vector4 ColTimeSoon  = new(1.00f, 0.68f, 0.14f, 1f); // orange
    private static readonly Vector4 ColTimeEnded = new(0.45f, 0.45f, 0.50f, 1f); // grey
    private static readonly Vector4 ColTimeFut   = new(0.60f, 0.80f, 1.00f, 1f); // light blue
    private static readonly Vector4 ColNew       = new(1.00f, 0.82f, 0.18f, 1f); // yellow

    // Tag background colors (cycling palette)
    private static readonly Vector4[] TagColors =
    [
        new(0.18f, 0.36f, 0.60f, 0.80f),
        new(0.38f, 0.18f, 0.58f, 0.80f),
        new(0.18f, 0.50f, 0.38f, 0.80f),
        new(0.55f, 0.28f, 0.18f, 0.80f),
        new(0.20f, 0.44f, 0.55f, 0.80f),
    ];

    private static readonly Vector4 TagBgHov = new(0.30f, 0.30f, 0.38f, 0.90f);

    /// <summary>
    /// Draws a full event card. Call inside a scrollable child region.
    /// </summary>
    public static void DrawEventCard(VenueEvent ev, CachedEventStrings cached)
    {
        var sourceColor  = ev.Source == EventSource.Partake ? ColPartake : ColFFXIVenue;
        var sourceColorU = ImGui.ColorConvertFloat4ToU32(sourceColor);

        // ── capture top-left for the left border ─────────────────────────────
        float borderX  = ImGui.GetCursorScreenPos().X;
        float startY   = ImGui.GetCursorScreenPos().Y;
        float gs       = ImGuiHelpers.GlobalScale;

        ImGui.Indent(10f * gs);

        // ── Row 1 : Source badge  +  Title  +  action buttons ────────────────
        DrawRow1(ev, cached, sourceColor);

        // ── Row 2 : Host ─────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(ev.Host))
        {
            ImGui.TextColored(ColHost, $"  Hosted by {ev.Host}");
        }

        // ── Row 3 : Server / DC  +  Location ─────────────────────────────────
        DrawLocationRow(ev, cached);

        // ── Row 4 : Time ─────────────────────────────────────────────────────
        DrawTimeRow(ev, cached);

        // ── Row 5 : Tags + attendee count ────────────────────────────────────
        if (cached.Tags.Length > 0 || ev.AttendeeCount > 0)
        {
            if (ev.AttendeeCount > 0)
            {
                ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.70f, 1f),
                    $"  {ev.AttendeeCount} attending");
                if (cached.Tags.Length > 0) ImGui.SameLine(0, 12);
            }
            if (cached.Tags.Length > 0)
                DrawTagPills(ev.Id, cached.Tags);
        }

        // ── Row 6 : Description (collapsible) ────────────────────────────────
        if (!string.IsNullOrEmpty(ev.Description))
        {
            ImGui.Spacing();
            using var col = ImRaii.PushColor(ImGuiCol.Header,        new Vector4(0.15f, 0.15f, 0.22f, 1f));
            using var co2 = ImRaii.PushColor(ImGuiCol.HeaderHovered, new Vector4(0.20f, 0.20f, 0.30f, 1f));
            if (ImGui.CollapsingHeader($"  Description##{ev.Id}"))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.82f, 0.82f, 0.86f, 1f));
                ImGui.TextWrapped(ev.Description);
                ImGui.PopStyleColor();
            }
        }

        ImGui.Spacing();
        ImGui.Unindent(10f * gs);

        // ── Draw the left colored border retroactively ───────────────────────
        float endY = ImGui.GetCursorScreenPos().Y;
        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(borderX - 1f, startY),
            new Vector2(borderX + 3f * gs, endY),
            sourceColorU, 2f);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void DrawRow1(VenueEvent ev, CachedEventStrings cached, Vector4 sourceColor)
    {
        // Source badge (small pill)
        using (ImRaii.PushColor(ImGuiCol.Button,        sourceColor with { W = 0.30f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, sourceColor with { W = 0.45f }))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  sourceColor with { W = 0.55f }))
        using (ImRaii.PushColor(ImGuiCol.Text,          sourceColor))
        {
            ImGui.SmallButton($" {cached.SourceBadge} ##src{ev.Id}");
        }

        // NEW badge
        if (ev.IsNew)
        {
            ImGui.SameLine(0, 4);
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ColNew);
            ImGui.Text("[NEW]");
        }

        ImGui.SameLine(0, 8);

        // Title (clickable)
        var titleAvail = ImGui.GetContentRegionAvail().X
                         - CalcActionButtonsWidth(ev) - 12f * ImGuiHelpers.GlobalScale;
        ImGui.PushClipRect(ImGui.GetCursorScreenPos(),
            ImGui.GetCursorScreenPos() + new Vector2(titleAvail, ImGui.GetTextLineHeight() + 4f), true);

        using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
        {
            ImGui.TextUnformatted(ev.Title);
        }

        ImGui.PopClipRect();

        bool titleHovered = ImGui.IsItemHovered();
        if (titleHovered && !string.IsNullOrEmpty(ev.EventUrl))
        {
            ImGui.SetTooltip("Click to open event page");
            if (ImGui.IsItemClicked())
                Util.OpenLink(ev.EventUrl);
        }

        // Action buttons — right-aligned
        DrawActionButtons(ev);
    }

    private static float CalcActionButtonsWidth(VenueEvent ev)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        float w   = 0f;
        var   sty = ImGui.GetStyle();
        if (!string.IsNullOrEmpty(ev.EventUrl))      w += 52f * gs + sty.ItemSpacing.X;
        if (!string.IsNullOrEmpty(ev.LifestreamCode)) w += 70f * gs + sty.ItemSpacing.X;
        return w;
    }

    private static void DrawActionButtons(VenueEvent ev)
    {
        float w = CalcActionButtonsWidth(ev);
        if (w <= 0f) return;

        // Move cursor to right-align buttons
        float rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(rightEdge - w);

        float gs = ImGuiHelpers.GlobalScale;

        // [↗ Open]
        if (!string.IsNullOrEmpty(ev.EventUrl))
        {
            using var col = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.18f, 0.36f, 0.60f, 0.80f));
            using var co2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.48f, 0.78f, 0.90f));
            using var co3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.30f, 0.56f, 0.90f, 1.00f));
            if (ImGui.SmallButton($"Open##{ev.Id}"))
                Util.OpenLink(ev.EventUrl);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open on the web");
            ImGui.SameLine(0, 4);
        }

        // [/li Copy]
        if (!string.IsNullOrEmpty(ev.LifestreamCode))
        {
            using var col = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.38f, 0.18f, 0.60f, 0.80f));
            using var co2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.50f, 0.24f, 0.78f, 0.90f));
            using var co3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.60f, 0.30f, 0.90f, 1.00f));
            if (ImGui.SmallButton($"/li Copy##{ev.Id}"))
                ImGui.SetClipboardText($"/li {ev.LifestreamCode}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Copies to clipboard:\n/li {ev.LifestreamCode}\n\nPaste in FFXIV chat to teleport.");
        }
    }

    private static void DrawLocationRow(VenueEvent ev, CachedEventStrings cached)
    {
        if (string.IsNullOrEmpty(cached.ServerDc) && string.IsNullOrEmpty(cached.Location))
            return;

        ImGui.TextColored(ColLocation, "  ");
        ImGui.SameLine(0, 0);

        // Server (DC) as selectable — clicking copies location to clipboard
        var fullLoc = string.IsNullOrEmpty(cached.Location)
            ? cached.ServerDc
            : $"{cached.ServerDc}  -  {cached.Location}";

        using var col = ImRaii.PushColor(ImGuiCol.Text, ColLocation);
        if (ImGui.Selectable($"{fullLoc}##loc{ev.Id}", false, ImGuiSelectableFlags.None,
                              new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            ImGui.SetClipboardText(fullLoc);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to copy location");
    }

    private static void DrawTimeRow(VenueEvent ev, CachedEventStrings cached)
    {
        Vector4 timeColor;
        string  timeLabel;
        string  timeRange = $"  {cached.StartsAtLocal}  ->  {cached.EndsAtLocal}";

        if (cached.IsLive)
        {
            timeColor = ColTimeLive;
            timeLabel = "  LIVE  ";
        }
        else if (cached.IsStartingSoon)
        {
            timeColor = ColTimeSoon;
            timeLabel = "  SOON  ";
        }
        else if (cached.HasEnded)
        {
            timeColor = ColTimeEnded;
            timeLabel = "  ENDED ";
        }
        else
        {
            timeColor = ColTimeFut;
            timeLabel = "  ";
        }

        ImGui.TextColored(timeColor, $"{timeLabel}{timeRange}");

        // Hover tooltip with humanized times
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Starts {cached.StartsAtHumanized}\nEnds {cached.EndsAtHumanized}");
    }

    private static void DrawTagPills(string evId, string[] tags)
    {
        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 2f);

        for (int i = 0; i < tags.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, 4);

            var bg  = TagColors[i % TagColors.Length];
            var bgh = bg with { W = 1.0f };

            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        bg);
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, bgh);
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  bgh);
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          Vector4.One);

            // Non-interactive pill — SmallButton just for the visual
            ImGui.SmallButton($" {tags[i]} ##{evId}{i}");
        }

        ImGui.Spacing();
    }
}
