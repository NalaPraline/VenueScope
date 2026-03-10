using System;
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

    public static Services.TeamIconCache? IconCache;
    public static Services.FFXIVenueService? FlagService;
    public static Action<string>? OnHideVenue;

    // one popup at a time
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
        // GetHashCode is randomized per-session in .NET 5+
        unchecked
        {
            int h = 17;
            foreach (char c in tag) h = h * 31 + c;
            return TagPalette[Math.Abs(h) % TagPalette.Length];
        }
    }

    private const float PadX    = 14f;
    private const float PadY    = 7f;
    private const float ThumbW  = 50f;
    private const float ThumbH  = 42f;
    private const float ThumbGap = 8f;
    

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

        // ch1 = foreground (ImGui widgets), ch0 = background (DrawList)
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.Dummy(new Vector2(0f, padY));

        var dotCenter = cardTL + new Vector2(8f * gs, padY + ImGui.GetTextLineHeight() * 0.52f);
        dl.AddCircleFilled(dotCenter, 3.5f * gs, ImGui.ColorConvertFloat4ToU32(statusColor));

        ImGui.Indent(indent);

        DrawTitleRow(ev, cached, srcColor, config);
        DrawInfoRow(ev, cached, srcColor, statusColor);
        if (cached.Tags.Length > 0)
            DrawTags(ev.Id, cached.Tags, srcColor);
        if (!string.IsNullOrEmpty(ev.Description))
            DrawDescription(ev);

        ImGui.Dummy(new Vector2(0f, padY));
        ImGui.Unindent(indent);

        var cardBR = new Vector2(cardTL.X + cardW, ImGui.GetCursorScreenPos().Y);

        dl.ChannelsSetCurrent(0);

        dl.AddRectFilled(cardTL, cardBR,
            ImGui.ColorConvertFloat4ToU32(ColCardBg), 6f * gs);

        dl.AddRectFilled(
            cardTL + new Vector2(0f, 4f * gs),
            new Vector2(cardTL.X + 3f * gs, cardBR.Y - 4f * gs),
            ImGui.ColorConvertFloat4ToU32(srcColor with { W = 0.90f }), 2f);

        float thumbTop  = cardTL.Y + padY;
        float thumbLeft = cardTL.X + padX;
        var   tTL       = new Vector2(thumbLeft, thumbTop);
        var   tBR       = tTL + new Vector2(thumbW, thumbH);

        var icon = GetIcon(ev);
        if (icon != null)
        {
            // center-crop if the image is wider than the box
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

        DrawFlagPopup();
    }

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
        using (ImRaii.PushColor(ImGuiCol.Text, srcColor with { W = 0.50f }))
            ImGui.TextUnformatted(ev.Source == EventSource.Partake ? "Partake" : "FFXIV Venues");

        if (!string.IsNullOrEmpty(cached.ServerDc))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, ColLocation))
                ImGui.TextUnformatted(cached.ServerDc);
        }

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

        // FFXIVenue managers are Discord snowflake IDs, skip
        if (ev.Source == EventSource.Partake && !string.IsNullOrEmpty(ev.Host))
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextUnformatted($"by {ev.Host}");
        }

        if (ev.AttendeeCount > 0)
        {
            Dot();
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextUnformatted($"{ev.AttendeeCount} attending");
        }

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

        bool open;
        using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
            open = ImGui.CollapsingHeader($"Description##{ev.Id}");

        if (open)
            DrawDescriptionLines(StripMarkdown(ev.Description));
    }

    private static readonly Vector4 ColDescSection = new(0.90f, 0.75f, 0.40f, 1f);
    private static readonly Vector4 ColDescBody    = new(0.78f, 0.78f, 0.82f, 1f);

    private static void DrawDescriptionLines(string text)
    {
        ImGui.Spacing();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                ImGui.Spacing();
                continue;
            }

            if (line.StartsWith('='))
            {
                if (i > 0) ImGui.Spacing();
                using var c = ImRaii.PushColor(ImGuiCol.Text, ColDescSection);
                ImGui.TextUnformatted(line[1..].Trim());
                continue;
            }

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool isSection = line.Length <= 25
                          && words.Length <= 3
                          && !line.Contains("<<")
                          && !line.EndsWith('.') && !line.EndsWith('!')
                          && !line.EndsWith('?') && !line.EndsWith(',')
                          && !line.EndsWith(';');

            if (isSection)
            {
                if (i > 0) ImGui.Spacing();
                using var c = ImRaii.PushColor(ImGuiCol.Text, ColDescSection);
                ImGui.TextUnformatted(line);
            }
            else
            {
                using var c = ImRaii.PushColor(ImGuiCol.Text, ColDescBody);
                ImGui.TextWrapped(line);
            }
        }
        ImGui.Spacing();
    }

    private static string StripMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");              // [text](url) → text
        text = Regex.Replace(text, @"\*{1,2}([^*\n]+)\*{1,2}", "$1");           // **bold** / *italic*
        text = Regex.Replace(text, @"_{1,2}([^_\n]+)_{1,2}", "$1");             // __bold__ / _italic_
        text = Regex.Replace(text, @"~~([^~\n]+)~~", "$1");                      // ~~strike~~
        text = Regex.Replace(text, @"`([^`\n]+)`", "$1");                        // `code`
        text = Regex.Replace(text, @"^#{1,6}\s*", "", RegexOptions.Multiline);   // # headings
        text = Regex.Replace(text, @"^>\s*", "", RegexOptions.Multiline);        // > blockquotes
        return text.Trim();
    }

    private static readonly Vector4 ColFavOn  = new(1.00f, 0.82f, 0.14f, 1f);
    private static readonly Vector4 ColFavOff = new(0.44f, 0.44f, 0.52f, 1f);

    private static float CalcActionsWidth(VenueEvent ev)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        float spc = ImGui.GetStyle().ItemSpacing.X;
        float w   = 32f * gs + spc;
        w += 52f * gs + spc;
        if (!string.IsNullOrEmpty(ev.EventUrl))       w += 52f * gs + spc;
        if (!string.IsNullOrEmpty(ev.LifestreamCode)) w += 90f * gs + spc;
        if (ev.Source == EventSource.FFXIVenue)       w += 32f * gs + spc;
        return w;
    }

    private static void DrawActions(VenueEvent ev, float reservedW, Configuration config)
    {
        if (reservedW <= 0f) return;

        float rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(rightEdge - reservedW);

        bool isFav = ev.Source == EventSource.Partake
            ? ev.TeamId > 0 && config.FavoritePartakeTeamIds.Contains(ev.TeamId)
            : config.FavoriteEventIds.Contains(ev.Id);
        using (ImRaii.PushColor(ImGuiCol.Button,        isFav ? new Vector4(0.28f, 0.22f, 0.04f, 0.70f) : new Vector4(0.14f, 0.14f, 0.20f, 0.60f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, isFav ? new Vector4(0.40f, 0.32f, 0.06f, 0.90f) : new Vector4(0.22f, 0.22f, 0.30f, 0.90f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.50f, 0.40f, 0.08f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.Text,          isFav ? ColFavOn : ColFavOff))
        {
            if (ImGui.SmallButton($" {(isFav ? "\u2605" : "\u2606")} ##{ev.Id}fav"))
            {
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
        }
        string favTooltip = ev.Source == EventSource.Partake && !string.IsNullOrEmpty(ev.TeamName)
            ? (isFav ? $"Unfollow {ev.TeamName}" : $"Follow {ev.TeamName} (all their events)")
            : (isFav ? "Unfollow this venue" : "Follow this venue");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(favTooltip);
        ImGui.SameLine(0, 4);

        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.25f, 0.12f, 0.12f, 0.60f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.18f, 0.18f, 0.85f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.60f, 0.22f, 0.22f, 1.00f)))
        using (ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.90f, 0.42f, 0.42f, 1.00f)))
        {
            if (ImGui.SmallButton($" Hide ##{ev.Id}hide"))
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
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hide this venue");
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
            bool lsAvail = Plugin.IsLifestreamAvailable();
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        lsAvail ? new Vector4(0.18f, 0.36f, 0.22f, 0.65f) : new Vector4(0.28f, 0.20f, 0.20f, 0.65f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, lsAvail ? new Vector4(0.24f, 0.52f, 0.30f, 0.90f) : new Vector4(0.40f, 0.26f, 0.26f, 0.90f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  lsAvail ? new Vector4(0.30f, 0.64f, 0.38f, 1.00f) : new Vector4(0.50f, 0.32f, 0.32f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          lsAvail ? new Vector4(0.62f, 1.00f, 0.70f, 1.00f) : new Vector4(0.80f, 0.50f, 0.50f, 1.00f));
            if (ImGui.SmallButton($" Teleport ##{ev.Id}"))
            {
                if (lsAvail)
                {
                    Plugin.CommandManager.ProcessCommand($"/li {ev.LifestreamCode}");
                }
                else
                {
                    Plugin.NotificationManager.AddNotification(new Notification
                    {
                        Title   = "Lifestream not installed",
                        Content = "The Lifestream plugin is required for in-game teleport. Please install it via the Dalamud plugin installer.",
                        Type    = NotificationType.Warning,
                    });
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(lsAvail ? $"/li {ev.LifestreamCode}" : "Lifestream is not installed, click for details");
        }

        if (ev.Source == EventSource.FFXIVenue)
        {
            ImGui.SameLine(0, 4);
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.35f, 0.12f, 0.12f, 0.65f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.55f, 0.18f, 0.18f, 0.90f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.70f, 0.22f, 0.22f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(1.00f, 0.40f, 0.40f, 1.00f));
            if (ImGui.SmallButton($" ! ##{ev.Id}flag"))
            {
                _flagVenueId  = ev.Id.StartsWith("ffxivenue-") ? ev.Id[10..] : ev.Id;
                _flagCategory = 0;
                _flagDesc     = string.Empty;
                _flagStatus   = string.Empty;
                _flagBusy     = false;
                ImGui.OpenPopup("##venueflagpopup");
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Report this venue");
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
                    _ => "InappropriateContent",
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
