using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using VenueScope.Models;

namespace VenueScope.Helpers;

public static class EventRenderer
{
    private static readonly Vector4 ColTitle     = Palette.Text;
    private static readonly Vector4 ColMuted     = Palette.Muted;
    private static readonly Vector4 ColBullet    = new(0.32f, 0.30f, 0.40f, 1f);

    public static Services.TeamIconCache? IconCache;
    public static Services.FFXIVenueService? FlagService;
    public static Action<string>? OnHideVenue;
    public static Action<VenueEvent>? OnOpenEvent;

    private static string _flagVenueId  = string.Empty;
    private static int    _flagCategory = 0;
    private static string _flagDesc     = string.Empty;
    private static bool   _flagBusy     = false;
    private static string _flagStatus   = string.Empty;


    private static IDalamudTextureWrap? GetIcon(VenueEvent ev) =>
        IconCache?.GetOrQueue(
            !string.IsNullOrEmpty(ev.TeamIconUrl) ? ev.TeamIconUrl : ev.BannerUrl);

    private static readonly Vector4[] TagPalette =
    [
        new(0.96f, 0.45f, 0.45f, 1f),
        new(0.96f, 0.68f, 0.24f, 1f),
        new(0.40f, 0.88f, 0.52f, 1f),
        new(0.24f, 0.82f, 0.94f, 1f),
        new(0.66f, 0.50f, 1.00f, 1f),
        new(0.96f, 0.48f, 0.78f, 1f),
        new(0.42f, 0.72f, 1.00f, 1f),
        new(0.94f, 0.88f, 0.28f, 1f),
        new(0.92f, 0.58f, 0.28f, 1f),
        new(0.42f, 0.94f, 0.80f, 1f),
    ];

    public static Vector4 GetTagColor(string tag)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in tag) h = h * 31 + c;
            return TagPalette[Math.Abs(h) % TagPalette.Length];
        }
    }

    public static Action<string>?     OnTagClicked;
    public static Func<string, bool>? IsTagSelected;

    private const float CardPad   = 12f;
    private const float ThumbW    = 104f;
    private const float ThumbH    = 78f;
    private const float SideWidth = 176f;

    private static string _flagNextId = string.Empty;

    public static void DrawEventCard(VenueEvent ev, CachedEventStrings cached, Configuration config)
    {
        float   gs       = ImGuiHelpers.GlobalScale;
        Vector4 srcColor = ev.Source == EventSource.Partake ? Palette.Partake : Palette.FFXIVenue;

        float cardW  = ImGui.GetContentRegionAvail().X;
        var   cardTL = ImGui.GetCursorScreenPos();
        var   dl     = ImGui.GetWindowDrawList();

        float pad   = CardPad   * gs;
        var   thumb = new Vector2(ThumbW, ThumbH) * gs;
        float side  = SideWidth * gs;
        float midX  = cardTL.X + pad + thumb.X + 14f * gs;
        float sideX = cardTL.X + cardW - pad - side;
        float midW  = Math.Max(60f * gs, sideX - 10f * gs - midX);

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(new Vector2(midX, cardTL.Y + pad));
        ImGui.BeginGroup();
        DrawTitleLine(ev, config, midW);
        DrawMetaLine(ev, cached, srcColor, midX, midW);
        if (cached.Tags.Length > 0)
            DrawTagLine(ev.Id, cached.Tags, midW);
        DrawViewLink(ev);
        ImGui.EndGroup();
        float bottom = ImGui.GetItemRectMax().Y;

        float sideBottom = DrawSideColumn(ev, cached, config, new Vector2(sideX, cardTL.Y + pad), side);
        bottom = Math.Max(bottom, Math.Max(sideBottom, cardTL.Y + pad + thumb.Y));

        var cardBR = new Vector2(cardTL.X + cardW, bottom + pad);

        bool hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(cardTL, cardBR);
        if (hovered && !ImGui.IsAnyItemHovered() && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                OnOpenEvent?.Invoke(ev);
        }

        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(cardTL, cardBR, Palette.U(hovered ? Palette.CardHover : Palette.Card), 10f * gs);
        dl.AddRectFilled(
            cardTL + new Vector2(0f, 10f * gs),
            new Vector2(cardTL.X + 3f * gs, cardBR.Y - 10f * gs),
            Palette.U(srcColor with { W = 0.85f }), 2f * gs);

        DrawThumb(dl, ev, srcColor, new Vector2(cardTL.X + pad, cardTL.Y + pad), thumb);

        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(cardTL.X, cardBR.Y));
        ImGui.Dummy(new Vector2(cardW, 0f));

        if (_flagNextId == ev.Id)
        {
            _flagNextId = string.Empty;
            OpenFlagPopup(ev.Id);
        }
        DrawFlagPopup();
    }

    private static void DrawThumb(ImDrawListPtr dl, VenueEvent ev, Vector4 srcColor, Vector2 tTL, Vector2 size)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        var   tBR = tTL + size;

        var icon = GetIcon(ev);
        if (icon != null)
        {
            var uv0 = Vector2.Zero;
            var uv1 = Vector2.One;
            if (icon.Width > 0 && icon.Height > 0)
            {
                float imgAspect = (float)icon.Width / icon.Height;
                float boxAspect = size.X / size.Y;
                if (imgAspect > boxAspect)
                {
                    float offset = (1f - boxAspect / imgAspect) * 0.5f;
                    uv0 = new Vector2(offset, 0f);
                    uv1 = new Vector2(1f - offset, 1f);
                }
                else if (imgAspect < boxAspect)
                {
                    float offset = (1f - imgAspect / boxAspect) * 0.5f;
                    uv0 = new Vector2(0f, offset);
                    uv1 = new Vector2(1f, 1f - offset);
                }
            }
            dl.AddImageRounded(icon.Handle, tTL, tBR, uv0, uv1, 0xFFFFFFFF, 8f * gs);
            return;
        }

        dl.AddRectFilled(tTL, tBR, Palette.U(srcColor with { W = 0.14f }), 8f * gs);
        string initial = ev.Title.Length > 0 ? ev.Title[..1].ToUpperInvariant() : "?";
        var    initSz  = ImGui.CalcTextSize(initial) * 1.6f;
        Widgets.TextWithSize(dl, tTL + (size - initSz) / 2f, srcColor with { W = 0.75f }, initial, 1.6f);
    }

    private static void DrawViewLink(VenueEvent ev)
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.Dummy(new Vector2(0f, 1f * gs));

        const string label = "View event";
        var  textSz  = ImGui.CalcTextSize(label);
        var  iconSz  = Widgets.IconSize(Dalamud.Interface.FontAwesomeIcon.ChevronRight, 0.7f);
        var  p0      = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton($"##view{ev.Id}", new Vector2(textSz.X + iconSz.X + 6f * gs, textSz.Y));
        bool hovered = ImGui.IsItemHovered();
        var  col     = hovered ? Palette.AccentText : Palette.Accent;
        var  dl      = ImGui.GetWindowDrawList();

        dl.AddText(p0, Palette.U(col), label);
        Widgets.DrawIcon(dl, Dalamud.Interface.FontAwesomeIcon.ChevronRight,
            p0 + new Vector2(textSz.X + 5f * gs, (textSz.Y - iconSz.Y) / 2f + 1f * gs), col, 0.7f);

        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked) OnOpenEvent?.Invoke(ev);
    }

    private static bool IsFollowed(VenueEvent ev, Configuration config) =>
        ev.Source == EventSource.Partake
            ? ev.TeamId > 0 && config.FavoritePartakeTeamIds.Contains(ev.TeamId)
            : config.FavoriteEventIds.Contains(ev.Id);

    private static void DrawTitleLine(VenueEvent ev, Configuration config, float width)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        bool  isFav = IsFollowed(ev, config);
        float star  = ImGui.GetTextLineHeight() + 4f * gs;
        float badge = ev.IsNew ? ImGui.CalcTextSize("NEW").X + 18f * gs : 0f;

        string title = ev.Title.Length > 0 ? ev.Title : "(no title)";
        string shown = Widgets.Ellipsize(title, width - star - badge - 8f * gs);

        using (ImRaii.PushColor(ImGuiCol.Text, Palette.Text))
            ImGui.TextUnformatted(shown);
        if (ImGui.IsItemHovered())
        {
            if (shown != title || !string.IsNullOrEmpty(ev.EventUrl))
                ImGui.SetTooltip(string.IsNullOrEmpty(ev.EventUrl) ? title : $"{title}\nClick to open the event page");
            if (!string.IsNullOrEmpty(ev.EventUrl))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsItemClicked())
                    Util.OpenLink(ev.EventUrl);
            }
        }

        if (ev.IsNew)
        {
            ImGui.SameLine(0, 8f * gs);
            var p0 = ImGui.GetCursorScreenPos();
            var ts = ImGui.CalcTextSize("NEW");
            var sz = new Vector2(ts.X + 10f * gs, ts.Y);
            ImGui.Dummy(sz);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p0 + sz, Palette.U(Palette.Gold with { W = 0.18f }), sz.Y / 2f);
            dl.AddText(p0 + new Vector2(5f * gs, 0f), Palette.U(Palette.Gold), "NEW");
        }

        ImGui.SameLine(0, 6f * gs);
        var  sp      = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton($"##fav{ev.Id}", new Vector2(star, ImGui.GetTextLineHeight()));
        bool hovered = ImGui.IsItemHovered();
        Widgets.DrawIconCentered(ImGui.GetWindowDrawList(), Dalamud.Interface.FontAwesomeIcon.Star,
            sp + new Vector2(star, ImGui.GetTextLineHeight()) / 2f,
            isFav ? Palette.Gold : hovered ? Palette.TextSoft : Palette.Muted with { W = 0.55f }, 0.8f);
        if (hovered)
            ImGui.SetTooltip(FollowLabel(ev, isFav));
        if (clicked)
            ToggleFollow(ev, config);
    }

    private static string FollowLabel(VenueEvent ev, bool isFav) =>
        ev.Source == EventSource.Partake && !string.IsNullOrEmpty(ev.TeamName)
            ? (isFav ? $"Unfollow {ev.TeamName}" : $"Follow {ev.TeamName} (all their events)")
            : (isFav ? "Unfollow this venue" : "Follow this venue");

    private static void DrawMetaLine(VenueEvent ev, CachedEventStrings cached, Vector4 srcColor, float left, float width)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        var   top = ImGui.GetCursorScreenPos();
        ImGui.PushClipRect(top, new Vector2(left + width, top.Y + ImGui.GetTextLineHeight() + 4f * gs), true);

        using (ImRaii.PushColor(ImGuiCol.Text, srcColor))
            ImGui.TextUnformatted(ev.Source == EventSource.Partake ? "Partake" : "FFXIV Venues");

        if (!string.IsNullOrEmpty(cached.ServerDc))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSoft))
                ImGui.TextUnformatted(cached.ServerDc);
        }

        if (!string.IsNullOrEmpty(cached.Location))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextSoft))
                ImGui.TextUnformatted(cached.Location);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip("Click to copy the address");
            }
            if (ImGui.IsItemClicked())
                ImGui.SetClipboardText(string.IsNullOrEmpty(cached.ServerDc)
                    ? cached.Location
                    : $"{cached.ServerDc} - {cached.Location}");
        }

        if (ev.Source == EventSource.Partake && !string.IsNullOrEmpty(ev.Host))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                ImGui.TextUnformatted($"by {ev.Host}");
        }

        if (ev.AttendeeCount > 0)
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
                ImGui.TextUnformatted($"{ev.AttendeeCount} going");
        }

        ImGui.PopClipRect();
    }

    private static void DrawTagLine(string evId, string[] tags, float width)
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.Dummy(new Vector2(0f, 1f * gs));

        float gap  = 4f * gs;
        float used = 0f;
        int   shown = 0;
        for (int i = 0; i < tags.Length; i++)
        {
            float w = Widgets.ChipWidth(tags[i]);
            int   rest = tags.Length - i - 1;
            float more = rest > 0 ? Widgets.ChipWidth($"+{rest}") + gap : 0f;
            if (used + w + more > width && shown > 0) break;
            used += w + gap;
            shown++;
        }

        for (int i = 0; i < shown; i++)
        {
            if (i > 0) ImGui.SameLine(0, gap);
            bool selected = IsTagSelected?.Invoke(tags[i]) ?? false;
            if (Widgets.Chip($"##tag{evId}{i}", tags[i], selected))
                OnTagClicked?.Invoke(tags[i]);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(selected ? $"Stop filtering by {tags[i]}" : $"Show only {tags[i]}");
        }

        if (shown < tags.Length)
        {
            ImGui.SameLine(0, gap);
            Widgets.Chip($"##tagmore{evId}", $"+{tags.Length - shown}", false);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join("\n", tags[shown..]));
        }
    }

    private static float DrawSideColumn(VenueEvent ev, CachedEventStrings cached, Configuration config, Vector2 topLeft, float width)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        var   dl    = ImGui.GetWindowDrawList();
        float right = topLeft.X + width;
        float y     = topLeft.Y;
        float lineH = ImGui.GetTextLineHeight();

        if (cached.IsLive)
        {
            var ts = ImGui.CalcTextSize("Live");
            var sz = new Vector2(ts.X + 22f * gs, ts.Y + 2f * gs);
            var p0 = new Vector2(right - sz.X, y);
            dl.AddRectFilled(p0, p0 + sz, Palette.U(Palette.Live with { W = 0.14f }), sz.Y / 2f);
            dl.AddCircleFilled(p0 + new Vector2(8f * gs, sz.Y / 2f), 3f * gs, Palette.U(Palette.Live));
            dl.AddText(p0 + new Vector2(15f * gs, 1f * gs), Palette.U(Palette.Live), "Live");
            y += sz.Y + 3f * gs;
        }
        else if (!string.IsNullOrEmpty(cached.StatusLabel))
        {
            var col = cached.HasEnded ? Palette.Muted : cached.IsStartingSoon ? Palette.Soon : Palette.AccentText;
            var ts  = ImGui.CalcTextSize(cached.StatusLabel);
            dl.AddText(new Vector2(right - ts.X, y), Palette.U(col), cached.StatusLabel);
            y += lineH + 2f * gs;
        }

        const float big = 1.2f;
        var timeSz = Widgets.MeasureWithSize(cached.TimeRange, big);
        Widgets.TextWithSize(dl, new Vector2(right - timeSz.X, y), cached.HasEnded ? Palette.Muted : Palette.Text, cached.TimeRange, big);
        if (ImGui.IsMouseHoveringRect(new Vector2(right - timeSz.X, y), new Vector2(right, y + timeSz.Y)))
            ImGui.SetTooltip($"Starts {cached.StartsAtHumanized}\nEnds {cached.EndsAtHumanized}");
        y += timeSz.Y + 1f * gs;

        var daySz = ImGui.CalcTextSize(cached.DayLabel);
        dl.AddText(new Vector2(right - daySz.X, y), Palette.U(Palette.Muted), cached.DayLabel);
        y += lineH + 7f * gs;

        float btn  = ImGui.GetFrameHeight();
        float gap  = 4f * gs;
        bool  hasLinks = !string.IsNullOrEmpty(ev.EventUrl) || !string.IsNullOrEmpty(ev.DiscordUrl)
                      || !string.IsNullOrEmpty(ev.WebsiteUrl) || !string.IsNullOrEmpty(ev.InstagramUrl);
        bool  housing  = IsHousingLocation(ev.LifestreamCode);
        bool  canGo    = !string.IsNullOrEmpty(ev.LifestreamCode);

        float rowW = btn;
        if (hasLinks) rowW += btn + gap;
        if (housing)  rowW += btn + gap;
        if (canGo)    rowW += Widgets.PillWidth(Dalamud.Interface.FontAwesomeIcon.MapMarkerAlt, "Go") + gap;

        ImGui.SetCursorScreenPos(new Vector2(right - rowW, y));

        if (canGo)
        {
            bool lsAvail = Plugin.IsLifestreamAvailable();
            if (Widgets.PillButton($"##go{ev.Id}", Dalamud.Interface.FontAwesomeIcon.MapMarkerAlt, "Go",
                    lsAvail ? Palette.Accent : Palette.Muted,
                    lsAvail ? $"Teleport: {ev.LifestreamCode}" : "Lifestream is not installed, click for details", btn))
                RequestTeleport(ev.Server, ev.LifestreamCode, config);
            ImGui.SameLine(0, gap);
        }

        if (hasLinks)
        {
            if (Widgets.IconButton($"##links{ev.Id}btn", Dalamud.Interface.FontAwesomeIcon.Link, "Links", size: btn))
                ImGui.OpenPopup($"##links{ev.Id}");
            DrawLinksPopup(ev);
            ImGui.SameLine(0, gap);
        }

        if (housing)
        {
            bool known = ev.LinkedSynchell != null;
            if (Widgets.IconButton($"##cwls{ev.Id}btn", Dalamud.Interface.FontAwesomeIcon.Users,
                    known ? "Syncshell" : "Syncshell (none registered yet)",
                    known ? Palette.FFXIVenue : null, size: btn))
                ImGui.OpenPopup($"##cwls{ev.Id}");

            ImGui.SetNextWindowSize(new Vector2(300f * gs, 0f));
            if (ImGui.BeginPopup($"##cwls{ev.Id}"))
            {
                DrawCwlsPopupContent(ev.LinkedSynchell, ev.Id);
                ImGui.EndPopup();
            }
            ImGui.SameLine(0, gap);
        }

        if (Widgets.IconButton($"##more{ev.Id}btn", Dalamud.Interface.FontAwesomeIcon.EllipsisH, "More", size: btn))
            ImGui.OpenPopup($"##more{ev.Id}");
        DrawMorePopup(ev, cached, config);

        return ImGui.GetItemRectMax().Y;
    }

    private static void DrawLinksPopup(VenueEvent ev)
    {
        if (!ImGui.BeginPopup($"##links{ev.Id}")) return;

        if (!string.IsNullOrEmpty(ev.EventUrl) &&
            ImGui.MenuItem(ev.Source == EventSource.Partake ? $"Open on Partake##{ev.Id}lw" : $"Open website##{ev.Id}lw"))
            Util.OpenLink(ev.EventUrl);
        if (!string.IsNullOrEmpty(ev.WebsiteUrl) && ImGui.MenuItem($"Website##{ev.Id}lws"))
            Util.OpenLink(ev.WebsiteUrl);
        if (!string.IsNullOrEmpty(ev.InstagramUrl) && ImGui.MenuItem($"Instagram##{ev.Id}lig"))
            Util.OpenLink(ev.InstagramUrl);
        if (!string.IsNullOrEmpty(ev.DiscordUrl) && ImGui.MenuItem($"Discord server##{ev.Id}ld"))
            Util.OpenLink(ev.DiscordUrl);

        ImGui.EndPopup();
    }

    private static void DrawMorePopup(VenueEvent ev, CachedEventStrings cached, Configuration config)
    {
        if (!ImGui.BeginPopup($"##more{ev.Id}")) return;

        bool isFav = IsFollowed(ev, config);
        if (ImGui.MenuItem($"{FollowLabel(ev, isFav)}##{ev.Id}mfav"))
            ToggleFollow(ev, config);

        if (!string.IsNullOrEmpty(cached.Location) && ImGui.MenuItem($"Copy the address##{ev.Id}mcopy"))
            ImGui.SetClipboardText(string.IsNullOrEmpty(cached.ServerDc) ? cached.Location : $"{cached.ServerDc} - {cached.Location}");

        ImGui.Separator();

        using (ImRaii.PushColor(ImGuiCol.Text, Palette.Danger))
        {
            if (ImGui.MenuItem($"Hide this venue##{ev.Id}mhide"))
                HideVenue(ev, config);
            if (ev.Source == EventSource.FFXIVenue && ImGui.MenuItem($"Report this venue##{ev.Id}mflag"))
                _flagNextId = ev.Id;
        }

        ImGui.EndPopup();
    }

    private static void ToggleFollow(VenueEvent ev, Configuration config)
    {
        bool isFav = IsFollowed(ev, config);
        if (ev.Source == EventSource.Partake && ev.TeamId > 0)
        {
            string key = $"partake:{ev.TeamId}";
            if (isFav)
            {
                config.FavoritePartakeTeamIds.Remove(ev.TeamId);
                config.FavoriteVenueCache.Remove(key);
            }
            else
            {
                config.FavoritePartakeTeamIds.Add(ev.TeamId);
                config.FavoriteVenueCache[key] = new FavoriteVenueInfo
                {
                    TeamId     = ev.TeamId,
                    Name       = ev.TeamName,
                    Server     = ev.Server,
                    DataCenter = ev.DataCenter,
                    IconUrl    = !string.IsNullOrEmpty(ev.TeamIconUrl) ? ev.TeamIconUrl : ev.BannerUrl,
                    Source     = EventSource.Partake,
                };
            }
        }
        else if (ev.Source == EventSource.FFXIVenue)
        {
            string key = $"ffxiv:{ev.Id}";
            if (isFav)
            {
                config.FavoriteEventIds.Remove(ev.Id);
                config.FavoriteVenueCache.Remove(key);
            }
            else
            {
                config.FavoriteEventIds.Add(ev.Id);
                config.FavoriteVenueCache[key] = new FavoriteVenueInfo
                {
                    VenueId    = ev.Id,
                    Name       = ev.Title,
                    Server     = ev.Server,
                    DataCenter = ev.DataCenter,
                    IconUrl    = !string.IsNullOrEmpty(ev.BannerUrl) ? ev.BannerUrl : ev.TeamIconUrl,
                    Source     = EventSource.FFXIVenue,
                };
            }
        }
        config.Save();
    }

    private static void HideVenue(VenueEvent ev, Configuration config)
    {
        if (ev.Source == EventSource.Partake && ev.TeamId > 0)
        {
            string key = $"partake:{ev.TeamId}";
            config.FavoritePartakeTeamIds.Remove(ev.TeamId);
            config.FavoriteVenueCache.Remove(key);
            config.HiddenPartakeTeamIds.Add(ev.TeamId);
            config.HiddenVenueCache[key] = new FavoriteVenueInfo
            {
                TeamId     = ev.TeamId,
                Name       = ev.TeamName,
                Server     = ev.Server,
                DataCenter = ev.DataCenter,
                IconUrl    = !string.IsNullOrEmpty(ev.TeamIconUrl) ? ev.TeamIconUrl : ev.BannerUrl,
                Source     = EventSource.Partake,
            };
        }
        else if (ev.Source == EventSource.FFXIVenue)
        {
            string key = $"ffxiv:{ev.Id}";
            config.FavoriteEventIds.Remove(ev.Id);
            config.FavoriteVenueCache.Remove(key);
            config.HiddenVenueIds.Add(ev.Id);
            config.HiddenVenueCache[key] = new FavoriteVenueInfo
            {
                VenueId    = ev.Id,
                Name       = ev.Title,
                Server     = ev.Server,
                DataCenter = ev.DataCenter,
                IconUrl    = !string.IsNullOrEmpty(ev.BannerUrl) ? ev.BannerUrl : ev.TeamIconUrl,
                Source     = EventSource.FFXIVenue,
            };
        }

        string displayName = ev.Source == EventSource.Partake
            ? (string.IsNullOrEmpty(ev.TeamName) ? ev.Title : ev.TeamName)
            : ev.Title;
        config.Save();
        OnHideVenue?.Invoke(displayName);
    }

    private static readonly System.Text.RegularExpressions.Regex _wardRx = new(@"\bW(?:ard\s+)?\d+\b", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex _plotRx = new(@"\bP(?:lot\s+)?\d+\b", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static bool IsHousingLocation(string code) =>
        !string.IsNullOrEmpty(code) && _wardRx.IsMatch(code) && _plotRx.IsMatch(code);


    private static void DrawCwlsPopupContent(Models.SynchellEntry? synchell, string evId)
    {
        float gs = ImGuiHelpers.GlobalScale;

        if (synchell == null)
        {
            ImGui.Separator();
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextWrapped("No syncshell registered for this venue.");
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.86f, 1f)))
                ImGui.TextWrapped("Are you the owner? Contact me to add yours!");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            using var d1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.22f, 0.26f, 0.60f, 0.80f));
            using var d2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.32f, 0.36f, 0.78f, 0.95f));
            using var d3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.40f, 0.44f, 0.90f, 1.00f));
            using var d4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.80f, 0.84f, 1.00f, 1.00f));
            if (ImGui.Button($"  Contact on Discord  ##cwlsadd{evId}"))
                Util.OpenLink("https://discordid.netlify.app/?id=249633834646241281");
            ImGui.Spacing();
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
            ImGui.TextUnformatted(synchell.VenueName);
        ImGui.Separator();
        ImGui.Spacing();

        int i = 0;
        foreach (var ch in synchell.Channels)
        {
            if (i > 0) ImGui.Separator();
            ImGui.Spacing();

            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.84f, 0.64f, 1.00f, 1f)))
                ImGui.TextUnformatted(ch.Name);

            ImGui.Spacing();

            using var b1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.18f, 0.18f, 0.28f, 0.70f));
            using var b2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.28f, 0.28f, 0.42f, 0.90f));
            using var b3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.36f, 0.36f, 0.54f, 1.00f));

            if (!string.IsNullOrEmpty(ch.Id))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.70f, 0.70f, 0.80f, 1f)))
                    ImGui.TextUnformatted("ID");
                ImGui.SameLine(0, 6);
                using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
                    ImGui.TextUnformatted(ch.Id);
                ImGui.SameLine(0, 8);
                if (ImGui.SmallButton($" Copy ##cwlsn{evId}{i}"))
                    ImGui.SetClipboardText(ch.Id);
            }

            if (!string.IsNullOrEmpty(ch.Password))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.70f, 0.70f, 0.80f, 1f)))
                    ImGui.TextUnformatted("Pass");
                ImGui.SameLine(0, 6);
                using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
                    ImGui.TextUnformatted(ch.Password);
                ImGui.SameLine(0, 8);
                if (ImGui.SmallButton($" Copy ##cwlsp{evId}{i}"))
                    ImGui.SetClipboardText(ch.Password);
            }

            ImGui.Spacing();
            i++;
        }
    }

    public static void OpenFlagPopup(string eventId)
    {
        _flagVenueId  = eventId.StartsWith("ffxivenue-") ? eventId[10..] : eventId;
        _flagCategory = 0;
        _flagDesc     = string.Empty;
        _flagStatus   = string.Empty;
        _flagBusy     = false;
        ImGui.OpenPopup("##venueflagpopup");
    }

    public static void DrawFlagPopup()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(360f * gs, 0f));

        using (ImRaii.PushColor(ImGuiCol.PopupBg, new Vector4(0.11f, 0.11f, 0.18f, 1f)))
        {
            if (!ImGui.BeginPopup("##venueflagpopup")) return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.45f, 1f)))
            ImGui.TextUnformatted("Report Venue");
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.72f, 1f)))
            ImGui.TextUnformatted("Category");
        ImGui.Spacing();
        ImGui.RadioButton("Venue is empty##flagcat",          ref _flagCategory, 0);
        ImGui.RadioButton("Incorrect information##flagcat",   ref _flagCategory, 1);
        ImGui.RadioButton("Inappropriate content##flagcat",   ref _flagCategory, 2);

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.72f, 1f)))
            ImGui.TextUnformatted("Additional details (optional)");
        ImGui.SetNextItemWidth(-1f);
        using (ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.14f, 0.22f, 1f)))
            ImGui.InputTextMultiline("##flagdesc", ref _flagDesc, 512,
                new Vector2(-1f, 56f * gs));

        ImGui.Spacing();

        if (_flagStatus == "ok")
        {
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.20f, 0.86f, 0.42f, 1f)))
                ImGui.TextUnformatted("Report submitted, thank you!");
            ImGui.Spacing();
            if (ImGui.Button("  Close  ##flagclose")) ImGui.CloseCurrentPopup();
        }
        else if (_flagStatus == "err")
        {
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.45f, 1f)))
                ImGui.TextUnformatted("Something went wrong, please try again.");
            ImGui.Spacing();
            if (ImGui.Button("  Retry  ##flagretry"))  { _flagStatus = string.Empty; _flagBusy = false; }
            ImGui.SameLine(0, 8);
            if (ImGui.Button("  Cancel  ##flagcancel")) ImGui.CloseCurrentPopup();
        }
        else if (_flagBusy)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.50f, 0.50f, 0.60f, 1f)))
                ImGui.TextUnformatted("Submitting...");
        }
        else
        {
            if (ImGui.Button("  Submit  ##flagsubmit") && FlagService != null)
            {
                _flagBusy = true;
                var id   = _flagVenueId;
                var cat  = _flagCategory switch
                {
                    0 => "VenueEmpty",
                    1 => "IncorrectInformation",
                    _                    => "InappropriateContent",
                };
                var desc = _flagDesc;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok    = await FlagService.FlagVenueAsync(id, cat, desc);
                    _flagStatus = ok ? "ok" : "err";
                    _flagBusy   = false;
                });
            }
            ImGui.SameLine(0, 8);
            if (ImGui.Button("  Cancel  ##flagcancel")) ImGui.CloseCurrentPopup();
        }

        ImGui.Spacing();
        ImGui.EndPopup();
    }

    public static void RequestTeleport(string server, string lifestreamCode, Configuration config)
    {
        if (string.IsNullOrEmpty(lifestreamCode)) return;

        if (!Plugin.IsLifestreamAvailable())
        {
            Plugin.NotificationManager.AddNotification(new Notification
            {
                Title   = "Lifestream not installed",
                Content = "The Lifestream plugin is required for in-game teleport. Please install it via the Dalamud plugin installer.",
                Type    = NotificationType.Warning,
            });
            return;
        }

        string venueRegion   = Plugin.GetServerRegion(server) ?? string.Empty;
        string currentRegion = Plugin.GetCurrentCharacterRegion() ?? venueRegion;

        bool needsSwitch = venueRegion != currentRegion
                        && !string.IsNullOrEmpty(venueRegion)
                        && venueRegion != "Oceania";

        if (!needsSwitch)
        {
            Plugin.LifestreamIpc.ExecuteCommand(lifestreamCode);
            return;
        }

        if (config.CharacterPerRegion.TryGetValue(venueRegion, out var charEntry) && !string.IsNullOrEmpty(charEntry))
        {
            var parts = charEntry.Split('@', 2);
            if (parts.Length != 2) return;

            string charName  = parts[0].Trim();
            string charWorld = parts[1].Trim();

            config.PendingVenueCode          = lifestreamCode;
            config.PendingExpectedCharacter  = $"{charName}@{charWorld}";
            config.PendingVenueServer        = string.Empty;
            config.PendingTravelCharName     = charName;
            config.PendingTravelHomeWorld    = charWorld;
            config.PendingTravelDestination  = server;
            config.Save();

            Plugin.Log.Information($"Logging out to switch to {charName}@{charWorld} for {venueRegion} venue ({server})");
            int errCode = Plugin.LifestreamIpc.Logout();
            bool ok = errCode == 0;

            if (ok)
                Plugin.BeginPendingTravel();

            Plugin.NotificationManager.AddNotification(new Notification
            {
                Title   = ok ? $"Switching to {charName}" : "Switch failed",
                Content = ok
                    ? $"Logging out. Will travel to {server} on login."
                    : "Lifestream could not log out. Make sure Lifestream is loaded.",
                Type    = ok ? NotificationType.Info : NotificationType.Error,
            });
        }
        else
        {
            Plugin.NotificationManager.AddNotification(new Notification
            {
                Title   = "No character configured",
                Content = $"This venue is in {venueRegion}. Configure a character for that region in Settings then Characters.",
                Type    = NotificationType.Warning,
            });
        }
    }

    private static void Dot()
    {
        ImGui.SameLine(0, 6);
        using (ImRaii.PushColor(ImGuiCol.Text, ColBullet))
            ImGui.TextUnformatted("·");
        ImGui.SameLine(0, 6);
    }
}
