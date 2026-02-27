using System;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System.Numerics;
using VenueScope.Models;

namespace VenueScope.Helpers;

/// <summary>
/// Renders a styled event card: thumbnail placeholder, dark background, source
/// accent bar, status dot, and right-aligned action buttons.
/// </summary>
public static class EventRenderer
{
    // ── Palette ───────────────────────────────────────────────────────────────
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
    private static readonly Vector4 ColCardBg    = new(0.13f, 0.13f, 0.20f, 1.00f);

    /// <summary>Set by Plugin on startup. Used to fetch team icon textures.</summary>
    public static Services.TeamIconCache? IconCache;

    private static IDalamudTextureWrap? GetIcon(VenueEvent ev) =>
        IconCache?.GetOrQueue(
            !string.IsNullOrEmpty(ev.TeamIconUrl) ? ev.TeamIconUrl : ev.BannerUrl);

    // ── Tag color palette ─────────────────────────────────────────────────────
    private static readonly Vector4[] TagPalette =
    [
        new(0.96f, 0.45f, 0.45f, 1f), // coral
        new(0.96f, 0.68f, 0.24f, 1f), // amber
        new(0.40f, 0.88f, 0.52f, 1f), // mint
        new(0.24f, 0.82f, 0.94f, 1f), // cyan
        new(0.66f, 0.50f, 1.00f, 1f), // lavender
        new(0.96f, 0.48f, 0.78f, 1f), // pink
        new(0.42f, 0.72f, 1.00f, 1f), // sky blue
        new(0.94f, 0.88f, 0.28f, 1f), // yellow
        new(0.92f, 0.58f, 0.28f, 1f), // orange
        new(0.42f, 0.94f, 0.80f, 1f), // turquoise
    ];

    public static Vector4 GetTagColor(string tag)
    {
        // Stable hash (GetHashCode is randomized per-session in .NET 5+)
        unchecked
        {
            int h = 17;
            foreach (char c in tag) h = h * 31 + c;
            return TagPalette[Math.Abs(h) % TagPalette.Length];
        }
    }

    // ── Layout constants (unscaled) ───────────────────────────────────────────
    private const float PadX    = 14f;
    private const float PadY    = 7f;
    private const float ThumbW  = 50f;
    private const float ThumbH  = 42f;
    private const float ThumbGap = 8f;

    // ── Public entry point ────────────────────────────────────────────────────

    public static void DrawEventCard(VenueEvent ev, CachedEventStrings cached, Configuration config)
    {
        float   gs         = ImGuiHelpers.GlobalScale;
        Vector4 srcColor   = ev.Source == EventSource.Partake ? ColPartake : ColFFXIVenue;
        Vector4 statusColor = StatusColor(cached);

        float cardW  = ImGui.GetContentRegionAvail().X;
        var   cardTL = ImGui.GetCursorScreenPos();
        var   dl     = ImGui.GetWindowDrawList();

        float padX      = PadX * gs;
        float padY      = PadY * gs;
        float thumbW    = ThumbW  * gs;
        float thumbH    = ThumbH  * gs;
        float thumbGap  = ThumbGap * gs;
        float indent    = padX + thumbW + thumbGap;

        // Split channels: 1 = foreground (ImGui widgets), 0 = background (DrawList)
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        // Top padding
        ImGui.Dummy(new Vector2(0f, padY));

        // Status dot — sits in the left margin next to the accent bar
        var dotCenter = cardTL + new Vector2(8f * gs, padY + ImGui.GetTextLineHeight() * 0.52f);
        dl.AddCircleFilled(dotCenter, 3.5f * gs, ImGui.ColorConvertFloat4ToU32(statusColor));

        ImGui.Indent(indent);

        // ── Rows ─────────────────────────────────────────────────────────────
        DrawTitleRow(ev, cached, srcColor, config);
        DrawInfoRow(ev, cached, srcColor, statusColor);
        if (cached.Tags.Length > 0)
            DrawTags(ev.Id, cached.Tags, srcColor);
        if (!string.IsNullOrEmpty(ev.Description))
            DrawDescription(ev);

        ImGui.Dummy(new Vector2(0f, padY));
        ImGui.Unindent(indent);

        // ── Background layer ─────────────────────────────────────────────────
        var cardBR = new Vector2(cardTL.X + cardW, ImGui.GetCursorScreenPos().Y);

        dl.ChannelsSetCurrent(0);

        // Card background
        dl.AddRectFilled(cardTL, cardBR,
            ImGui.ColorConvertFloat4ToU32(ColCardBg), 6f * gs);

        // Left accent bar (source color)
        dl.AddRectFilled(
            cardTL + new Vector2(0f, 4f * gs),
            new Vector2(cardTL.X + 3f * gs, cardBR.Y - 4f * gs),
            ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.90f }), 2f);

        // Thumbnail
        float thumbTop  = cardTL.Y + padY;
        float thumbLeft = cardTL.X + padX;
        var   tTL       = new Vector2(thumbLeft, thumbTop);
        var   tBR       = tTL + new Vector2(thumbW, thumbH);

        var icon = GetIcon(ev);
        if (icon != null)
        {
            // Compute UV: center-crop if the image is wider than the thumbnail box
            var uv0 = Vector2.Zero;
            var uv1 = Vector2.One;
            if (icon.Width > 0 && icon.Height > 0)
            {
                float imgAspect = (float)icon.Width / icon.Height;
                float boxAspect = thumbW / thumbH;
                if (imgAspect > boxAspect)
                {
                    float cropFraction = boxAspect / imgAspect;
                    float offset = (1f - cropFraction) * 0.5f;
                    uv0 = new Vector2(offset, 0f);
                    uv1 = new Vector2(1f - offset, 1f);
                }
            }

            dl.AddImageRounded(icon.Handle, tTL, tBR, uv0, uv1, 0xFFFFFFFF, 4f * gs);
            dl.AddRect(tTL, tBR,
                ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.25f }), 4f * gs, 0, gs);
        }
        else
        {
            // Placeholder while loading or no icon
            dl.AddRectFilled(tTL, tBR,
                ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.13f }), 4f * gs);
            dl.AddRect(tTL, tBR,
                ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.30f }), 4f * gs, 0, gs);
            string initial = ev.Source == EventSource.Partake ? "P" : "V";
            var    initSz  = ImGui.CalcTextSize(initial);
            dl.AddText(
                tTL + new Vector2((thumbW - initSz.X) * 0.5f, (thumbH - initSz.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.40f }),
                initial);
        }

        dl.ChannelsMerge();
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    private static void DrawTitleRow(VenueEvent ev, CachedEventStrings cached, Vector4 srcColor, Configuration config)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        float actW  = CalcActionsWidth(ev);
        float avail = ImGui.GetContentRegionAvail().X - actW - 6f * gs;
        float lineH = ImGui.GetTextLineHeight();

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

        DrawActions(ev, actW, config);
    }

    private static void DrawInfoRow(VenueEvent ev, CachedEventStrings cached,
                                    Vector4 srcColor, Vector4 statusColor)
    {
        // Source badge
        using (ImRaii.PushColor(ImGuiCol.Text, srcColor with { W = 0.50f }))
            ImGui.TextUnformatted(ev.Source == EventSource.Partake ? "Partake" : "FFXIV Venues");

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
                    ImGui.SetTooltip("Click to copy location");
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
        for (int i = 0; i < tags.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, 4);
            var col = GetTagColor(tags[i]);
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        col with { W = 0.28f });
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, col with { W = 0.48f });
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  col with { W = 0.60f });
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          col with { W = 1.00f });
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

    private static readonly Vector4 ColFavOn  = new(1.00f, 0.82f, 0.14f, 1f);
    private static readonly Vector4 ColFavOff = new(0.44f, 0.44f, 0.52f, 1f);

    private static float CalcActionsWidth(VenueEvent ev)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        float spc = ImGui.GetStyle().ItemSpacing.X;
        float w   = 32f * gs + spc; // star button always present
        if (!string.IsNullOrEmpty(ev.EventUrl))       w += 52f * gs + spc;
        if (!string.IsNullOrEmpty(ev.LifestreamCode)) w += 90f * gs + spc;
        return w;
    }

    private static void DrawActions(VenueEvent ev, float reservedW, Configuration config)
    {
        if (reservedW <= 0f) return;

        float rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(rightEdge - reservedW);

        // ── Favorite toggle ───────────────────────────────────────────────────
        bool isFav = config.FavoriteEventIds.Contains(ev.Id);
        using (ImRaii.PushColor(ImGuiCol.Button,        isFav ? new Vector4(0.28f, 0.22f, 0.04f, 0.70f) : new Vector4(0.14f, 0.14f, 0.20f, 0.60f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, isFav ? new Vector4(0.40f, 0.32f, 0.06f, 0.90f) : new Vector4(0.22f, 0.22f, 0.30f, 0.90f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.50f, 0.40f, 0.08f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.Text,          isFav ? ColFavOn : ColFavOff))
        {
            if (ImGui.SmallButton($" {(isFav ? "\u2605" : "\u2606")} ##{ev.Id}fav"))
            {
                if (isFav) config.FavoriteEventIds.Remove(ev.Id);
                else       config.FavoriteEventIds.Add(ev.Id);
                config.Save();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(isFav ? "Remove from favorites" : "Add to favorites");
        ImGui.SameLine(0, 4);

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
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.18f, 0.36f, 0.22f, 0.65f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.52f, 0.30f, 0.90f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.30f, 0.64f, 0.38f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.62f, 1.00f, 0.70f, 1.00f));
            if (ImGui.SmallButton($" Teleport ##{ev.Id}"))
                Plugin.CommandManager.ProcessCommand($"/li {ev.LifestreamCode}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"/li {ev.LifestreamCode}");
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
