using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using VenueScope.Helpers;
using VenueScope.Models;

namespace VenueScope.UI;

public sealed class SpotlightWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private SpotlightVenue?        _venue;
    private Vector4                _accent = ColAccent;

    private static readonly Vector4 ColBackdrop = new(0.055f, 0.045f, 0.085f, 1.00f);
    private static readonly Vector4 ColCard     = new(0.108f, 0.094f, 0.158f, 0.96f);
    private static readonly Vector4 ColBorder   = new(0.46f, 0.36f, 0.72f, 0.22f);
    private static readonly Vector4 ColTitle    = new(0.97f, 0.96f, 1.00f, 1.00f);
    private static readonly Vector4 ColTagline  = new(0.83f, 0.77f, 0.96f, 0.95f);
    private static readonly Vector4 ColMuted     = new(0.55f, 0.54f, 0.66f, 1.00f);
    private static readonly Vector4 ColSection  = new(0.78f, 0.62f, 1.00f, 1.00f);
    private static readonly Vector4 ColBody     = new(0.82f, 0.82f, 0.90f, 1.00f);
    private static readonly Vector4 ColLocation = new(0.46f, 0.86f, 0.62f, 1.00f);
    private static readonly Vector4 ColLink     = new(0.62f, 0.80f, 1.00f, 1.00f);
    private static readonly Vector4 ColAccent   = new(0.78f, 0.55f, 1.00f, 1.00f);

    public SpotlightWindow(Configuration config)
        : base("Spotlight##spotlightdetail", ImGuiWindowFlags.None)
    {
        _config = config;

        SizeCondition   = ImGuiCond.FirstUseEver;
        Size            = new Vector2(470, 720);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 380),
            MaximumSize = new Vector2(840, 1200),
        };
    }

    public void Open(SpotlightVenue venue)
    {
        _venue   = venue;
        WindowName = $"{(string.IsNullOrEmpty(venue.Name) ? "Spotlight" : venue.Name)}##spotlightdetail";
        IsOpen   = true;
        BringToFront();
    }

    public override void PreDraw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ColBackdrop);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 16f) * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 11f * gs);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(1);
    }

    public override void Draw()
    {
        if (_venue == null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextUnformatted("No venue selected.");
            return;
        }

        var v = _venue;
        _accent = v.GetAccent() ?? ColAccent;

        DrawHero(v);
        DrawSchedule(v);

        if (v.Activities.Count > 0)
        {
            Gap(10f);
            DrawActivityRow(v);
        }

        if (v.ActivityDetails.Exists(d => d.HasDetails))
        {
            Gap(10f);
            DrawGamesCard(v);
        }

        Gap(12f);
        DrawLocationCard(v);

        if (!string.IsNullOrEmpty(v.Description))
        {
            Gap(10f);
            DrawDescriptionCard(v);
        }

        if (v.Lineup.Count > 0)
        {
            Gap(10f);
            DrawLineupCard(v);
        }

        if (v.Synchell != null && v.Synchell.Channels.Count > 0)
        {
            Gap(10f);
            DrawSynchellCard(v);
        }

        if (!string.IsNullOrEmpty(v.DiscordUrl) || !string.IsNullOrEmpty(v.WebsiteUrl))
        {
            Gap(12f);
            DrawLinks(v);
        }
    }

    private void DrawHero(SpotlightVenue v)
    {
        float gs = ImGuiHelpers.GlobalScale;
        float w  = ImGui.GetContentRegionAvail().X;
        var   dl = ImGui.GetWindowDrawList();
        var   p0 = ImGui.GetCursorScreenPos();
        float rounding = 10f * gs;

        var icon = !string.IsNullOrEmpty(v.ImageUrl) ? EventRenderer.IconCache?.GetOrQueue(v.ImageUrl) : null;

        float h = 200f * gs;
        if (icon != null && icon.Width > 0 && icon.Height > 0)
            h = Math.Clamp(w / ((float)icon.Width / icon.Height), 120f * gs, 360f * gs);

        var p1 = p0 + new Vector2(w, h);

        if (icon != null && icon.Width > 0 && icon.Height > 0)
        {
            float imgAspect = (float)icon.Width / icon.Height;
            float boxAspect = w / h;
            var   uv0 = Vector2.Zero;
            var   uv1 = Vector2.One;
            if (imgAspect > boxAspect)
            {
                float crop = boxAspect / imgAspect;
                float off  = (1f - crop) * 0.5f;
                uv0 = new Vector2(off, 0f);
                uv1 = new Vector2(1f - off, 1f);
            }
            else if (imgAspect < boxAspect)
            {
                float crop = imgAspect / boxAspect;
                float off  = (1f - crop) * 0.5f;
                uv0 = new Vector2(0f, off);
                uv1 = new Vector2(1f, 1f - off);
            }
            dl.AddImageRounded(icon.Handle, p0, p1, uv0, uv1, 0xFFFFFFFF, rounding);
        }
        else
        {
            uint cTop = ImGui.ColorConvertFloat4ToU32(new Vector4(0.22f, 0.13f, 0.34f, 1f));
            uint cBot = ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.07f, 0.15f, 1f));
            dl.AddRectFilledMultiColor(p0, p1, cTop, cTop, cBot, cBot);
        }

        float  pad    = 14f * gs;
        string name   = !string.IsNullOrEmpty(v.BannerTitle) ? v.BannerTitle : v.Name;
        string tag    = !string.IsNullOrEmpty(v.BannerSubtitle) ? v.BannerSubtitle : v.Tagline;
        if (v.HideBannerText) { name = string.Empty; tag = string.Empty; }
        bool   hasTag = !string.IsNullOrEmpty(tag);
        bool   hasText = !string.IsNullOrEmpty(name) || hasTag;

        if (hasText)
        {
            float scrimH = h * 0.55f;
            uint  clear  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.035f, 0.028f, 0.06f, 0.00f));
            uint  shade  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.035f, 0.028f, 0.06f, 0.88f));
            dl.AddRectFilledMultiColor(new Vector2(p0.X, p1.Y - scrimH), p1, clear, clear, shade, shade);
        }

        dl.AddRect(p0, p1, ImGui.ColorConvertFloat4ToU32(_accent with { W = 0.28f }), rounding, 0, 1.5f * gs);

        if (!hasText)
        {
            ImGui.SetCursorScreenPos(p0);
            ImGui.Dummy(new Vector2(w, h));
            return;
        }

        ImGui.PushClipRect(p0, p1, true);

        const float titleScale = 1.6f;
        const float tagScale   = 1.2f;

        ImGui.SetWindowFontScale(titleScale);
        var nameSz = ImGui.CalcTextSize(name);
        ImGui.SetWindowFontScale(tagScale);
        float tagH = hasTag ? ImGui.GetTextLineHeight() : 0f;
        ImGui.SetWindowFontScale(1f);
        float gap2   = hasTag ? 5f * gs : 0f;
        float blockH = nameSz.Y + gap2 + tagH;
        float topY   = p1.Y - pad - blockH;
        float leftX  = p0.X + pad;

        if (!string.IsNullOrEmpty(name))
        {
            ImGui.SetCursorScreenPos(new Vector2(leftX, topY));
            ImGui.SetWindowFontScale(titleScale);
            using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
                ImGui.TextUnformatted(name);
            ImGui.SetWindowFontScale(1f);
        }

        if (hasTag)
        {
            ImGui.SetCursorScreenPos(new Vector2(leftX, topY + nameSz.Y + gap2));
            ImGui.SetWindowFontScale(tagScale);
            using (ImRaii.PushColor(ImGuiCol.Text, ColTagline))
                ImGui.TextUnformatted(tag);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.PopClipRect();

        ImGui.SetCursorScreenPos(p0);
        ImGui.Dummy(new Vector2(w, h));
    }

    private void DrawActivityRow(SpotlightVenue v)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        var   dl    = ImGui.GetWindowDrawList();
        var   pos   = ImGui.GetCursorScreenPos();
        float avail = ImGui.GetContentRegionAvail().X;
        float maxX  = pos.X + avail;
        float lineH = ImGui.GetTextLineHeight();
        float pillH = lineH + 7f * gs;
        float rowGap = 6f * gs;

        float x = pos.X;
        float y = pos.Y;
        foreach (var act in v.Activities)
        {
            var   col = SpotlightActivities.GetColor(act);
            var   ts  = ImGui.CalcTextSize(act);
            float pw  = ts.X + 16f * gs;
            if (x + pw > maxX && x > pos.X)
            {
                x = pos.X;
                y += pillH + rowGap;
            }
            var q0 = new Vector2(x, y);
            var q1 = new Vector2(x + pw, y + pillH);
            dl.AddRectFilled(q0, q1, ImGui.ColorConvertFloat4ToU32(col with { W = 0.18f }), pillH * 0.5f);
            dl.AddRect(q0, q1, ImGui.ColorConvertFloat4ToU32(col with { W = 0.52f }), pillH * 0.5f, 0, 1f);
            dl.AddText(new Vector2(x + 8f * gs, y + 3.5f * gs), ImGui.ColorConvertFloat4ToU32(col), act);
            x += pw + 6f * gs;
        }

        ImGui.Dummy(new Vector2(avail, (y - pos.Y) + pillH));
    }

    private void DrawSchedule(SpotlightVenue v)
    {
        var now    = DateTimeOffset.UtcNow;
        var status = v.GetStatus(now);
        if (status == SpotlightStatus.None) return;

        string  badge;
        Vector4 badgeCol;
        string  detail = string.Empty;

        switch (status)
        {
            case SpotlightStatus.Live:
                badge    = "EVENT IN PROGRESS";
                badgeCol = new Vector4(0.30f, 0.92f, 0.48f, 1f);
                if (v.EndTime != null)
                    detail = $"ends {Relative(v.EndTime.Value, now)}";
                break;
            case SpotlightStatus.Upcoming:
                badge    = "UPCOMING";
                badgeCol = _accent;
                detail   = $"{v.StartTime!.Value.ToLocalTime():ddd d MMM, HH:mm}  ·  starts {Relative(v.StartTime.Value, now)}";
                break;
            default:
                badge    = "ENDED";
                badgeCol = new Vector4(0.55f, 0.55f, 0.62f, 1f);
                if (v.EndTime != null)
                    detail = v.EndTime.Value.ToLocalTime().ToString("ddd d MMM, HH:mm");
                break;
        }

        Gap(10f);

        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        var   p  = ImGui.GetCursorScreenPos();
        float px = 8f * gs;
        float py = 3f * gs;

        bool live = status == SpotlightStatus.Live;
        string label = live ? $"● {badge}" : badge;
        var   ts = ImGui.CalcTextSize(label);
        var   q0 = p;
        var   q1 = p + new Vector2(ts.X + px * 2f, ts.Y + py * 2f);
        float rad = (ts.Y + py * 2f) * 0.5f;
        dl.AddRectFilled(q0, q1, ImGui.ColorConvertFloat4ToU32(badgeCol with { W = live ? 0.22f : 0.16f }), rad);
        dl.AddRect(q0, q1, ImGui.ColorConvertFloat4ToU32(badgeCol with { W = 0.55f }), rad, 0, 1f);
        dl.AddText(q0 + new Vector2(px, py), ImGui.ColorConvertFloat4ToU32(badgeCol), label);
        ImGui.Dummy(new Vector2(q1.X - q0.X, q1.Y - q0.Y));

        if (!string.IsNullOrEmpty(detail))
        {
            ImGui.SameLine(0, 9);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + py);
            using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                ImGui.TextUnformatted(detail);
        }
    }

    private static string Relative(DateTimeOffset target, DateTimeOffset now)
    {
        var span = target - now;
        bool past = span < TimeSpan.Zero;
        span = span.Duration();

        string s;
        if (span.TotalDays >= 1)       s = $"{(int)span.TotalDays}d {span.Hours}h";
        else if (span.TotalHours >= 1) s = $"{(int)span.TotalHours}h {span.Minutes}m";
        else                           s = $"{Math.Max(1, span.Minutes)}m";

        return past ? $"{s} ago" : $"in {s}";
    }

    private void DrawLocationCard(SpotlightVenue v)
    {
        var card = BeginCard(_accent);

        string server = v.Server;
        string loc    = v.BuildLocationLabel();

        if (!string.IsNullOrEmpty(server))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ColLocation))
                ImGui.TextUnformatted(server);
            if (!string.IsNullOrEmpty(loc))
            {
                ImGui.SameLine(0, 7);
                using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                    ImGui.TextUnformatted("·");
                ImGui.SameLine(0, 7);
                using (ImRaii.PushColor(ImGuiCol.Text, ColLocation with { W = 0.80f }))
                    ImGui.TextUnformatted(loc);
            }
        }

        string code = v.BuildLifestreamCode();
        if (!string.IsNullOrEmpty(code))
        {
            ImGui.Dummy(new Vector2(0f, 4f * ImGuiHelpers.GlobalScale));
            bool ls = Plugin.IsLifestreamAvailable();
            float innerW = card.W - card.PadX * 2f;
            bool clicked = BigButton("Teleport##spotteleport", innerW,
                ls ? new Vector4(0.16f, 0.34f, 0.21f, 0.85f) : new Vector4(0.28f, 0.20f, 0.20f, 0.80f),
                ls ? new Vector4(0.22f, 0.50f, 0.30f, 0.95f) : new Vector4(0.40f, 0.26f, 0.26f, 0.90f),
                ls ? new Vector4(0.28f, 0.62f, 0.38f, 1.00f) : new Vector4(0.50f, 0.32f, 0.32f, 1.00f),
                ls ? new Vector4(0.66f, 1.00f, 0.74f, 1.00f) : new Vector4(0.84f, 0.54f, 0.54f, 1.00f));
            if (clicked)
                EventRenderer.RequestTeleport(v.Server, code, _config);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(ls ? $"Teleport: {code}" : "Lifestream is not installed");
        }

        EndCard(card);
    }

    private void DrawDescriptionCard(SpotlightVenue v)
    {
        var card = BeginCard(default);
        float wrapX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - card.PadX;
        RichText.Draw(v.Description, wrapX - ImGui.GetCursorPosX());
        EndCard(card);
    }

    private void DrawLineupCard(SpotlightVenue v)
    {
        var card = BeginCard(_accent);
        SectionHeader("Lineup");
        ImGui.Dummy(new Vector2(0f, 4f * ImGuiHelpers.GlobalScale));

        float gs    = ImGuiHelpers.GlobalScale;
        float rowH  = 36f * gs;
        float lineH = ImGui.GetTextLineHeight();

        for (int i = 0; i < v.Lineup.Count; i++)
        {
            var entry = v.Lineup[i];
            if (i > 0) ImGui.Dummy(new Vector2(0f, 4f * gs));

            float startY = ImGui.GetCursorPosY();

            if (!string.IsNullOrEmpty(entry.Time))
            {
                ImGui.SetCursorPosY(startY + (rowH - (lineH + 4f * gs)) * 0.5f);
                DrawTimeChip(entry.Time);
                ImGui.SameLine(0, 9);
            }

            ImGui.SetCursorPosY(startY);
            DrawLineupLogo(entry, rowH);
            ImGui.SameLine(0, 9);

            ImGui.SetCursorPosY(startY + (rowH - lineH) * 0.5f);

            if (!string.IsNullOrEmpty(entry.Link))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ColLink))
                    ImGui.TextUnformatted(string.IsNullOrEmpty(entry.Name) ? entry.Link : entry.Name);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(entry.Link);
                }
                if (ImGui.IsItemClicked())
                    Util.OpenLink(entry.Link);
            }
            else
            {
                using (ImRaii.PushColor(ImGuiCol.Text, ColBody))
                    ImGui.TextUnformatted(entry.Name);
            }

            ImGui.SetCursorPosY(startY + rowH);
        }

        EndCard(card);
    }

    private void DrawLineupLogo(SpotlightLineupEntry entry, float size)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        var   p0 = ImGui.GetCursorScreenPos();

        var logo = !string.IsNullOrEmpty(entry.LogoUrl)
            ? EventRenderer.IconCache?.GetOrQueue(entry.LogoUrl)
            : null;

        if (logo != null && logo.Width > 0 && logo.Height > 0)
        {
            float imgAspect = (float)logo.Width / logo.Height;
            var   uv0 = Vector2.Zero;
            var   uv1 = Vector2.One;
            if (imgAspect > 1f)
            {
                float off = (1f - 1f / imgAspect) * 0.5f;
                uv0 = new Vector2(off, 0f);
                uv1 = new Vector2(1f - off, 1f);
            }
            else if (imgAspect < 1f)
            {
                float off = (1f - imgAspect) * 0.5f;
                uv0 = new Vector2(0f, off);
                uv1 = new Vector2(1f, 1f - off);
            }

            var q1 = p0 + new Vector2(size, size);
            dl.AddRectFilled(p0, q1, ImGui.ColorConvertFloat4ToU32(_accent with { W = 0.10f }), 7f * gs);
            dl.AddImageRounded(logo.Handle, p0, q1, uv0, uv1, 0xFFFFFFFF, 7f * gs);
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        dl.AddCircleFilled(
            p0 + new Vector2(size * 0.5f, size * 0.5f), 3.5f * gs,
            ImGui.ColorConvertFloat4ToU32(_accent with { W = 0.55f }));
        ImGui.Dummy(new Vector2(size, size));
    }

    private void DrawGamesCard(SpotlightVenue v)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var card = BeginCard(_accent);
        SectionHeader("Games");
        ImGui.Dummy(new Vector2(0f, 4f * gs));

        bool first = true;
        foreach (var d in v.ActivityDetails)
        {
            if (!d.HasDetails) continue;

            if (!first)
            {
                ImGui.Dummy(new Vector2(0f, 5f * gs));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0f, 5f * gs));
            }
            first = false;

            using (ImRaii.PushColor(ImGuiCol.Text, SpotlightActivities.GetColor(d.Activity)))
                ImGui.TextUnformatted(d.Activity);

            if (!string.IsNullOrEmpty(d.Start))
            {
                ImGui.SameLine();
                float chipW = ImGui.CalcTextSize(d.Start).X + 12f * gs + 13f * gs;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - chipW);
                DrawTimeChip(d.Start);
            }

            ImGui.Dummy(new Vector2(0f, 2f * gs));

            if (!string.IsNullOrEmpty(d.Price))
                using (ImRaii.PushColor(ImGuiCol.Text, ColBody))
                    ImGui.TextUnformatted(d.Price);

            if (!string.IsNullOrEmpty(d.Note))
                using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                    ImGui.TextWrapped(d.Note);

            if (!string.IsNullOrEmpty(d.Payout))
                using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                    ImGui.TextWrapped(d.Payout);
        }

        EndCard(card);
    }

    private void DrawSynchellCard(SpotlightVenue v)
    {
        var card = BeginCard(_accent);
        SectionHeader("Syncshell");
        ImGui.Dummy(new Vector2(0f, 4f * ImGuiHelpers.GlobalScale));

        int i = 0;
        foreach (var ch in v.Synchell!.Channels)
        {
            if (i > 0)
            {
                ImGui.Dummy(new Vector2(0f, 5f * ImGuiHelpers.GlobalScale));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0f, 5f * ImGuiHelpers.GlobalScale));
            }

            using (ImRaii.PushColor(ImGuiCol.Text, _accent))
                ImGui.TextUnformatted(ch.Name);
            ImGui.Dummy(new Vector2(0f, 3f * ImGuiHelpers.GlobalScale));

            if (!string.IsNullOrEmpty(ch.Id))
                DrawCredentialRow("ID", ch.Id, $"spotsyncid{i}");
            if (!string.IsNullOrEmpty(ch.Password))
                DrawCredentialRow("Pass", ch.Password, $"spotsyncpw{i}");

            i++;
        }

        EndCard(card);
    }

    private static void DrawCredentialRow(string label, string value, string id)
    {
        float gs = ImGuiHelpers.GlobalScale;
        using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
            ImGui.TextUnformatted(label);
        ImGui.SameLine(0, 8);
        using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
            ImGui.TextUnformatted(value);

        float btnW = 56f * gs;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - btnW);
        if (CopyButton($"Copy##{id}", btnW))
            ImGui.SetClipboardText(value);
    }

    private void DrawLinks(SpotlightVenue v)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        bool  hasD = !string.IsNullOrEmpty(v.DiscordUrl);
        bool  hasW = !string.IsNullOrEmpty(v.WebsiteUrl);
        float full = ImGui.GetContentRegionAvail().X;
        float spc  = 8f * gs;
        float each = (hasD && hasW) ? (full - spc) * 0.5f : full;

        if (hasD)
        {
            if (BigButton("Discord##spotdiscord", each,
                    new Vector4(0.22f, 0.26f, 0.60f, 0.88f),
                    new Vector4(0.32f, 0.36f, 0.78f, 0.96f),
                    new Vector4(0.40f, 0.44f, 0.90f, 1.00f),
                    new Vector4(0.84f, 0.88f, 1.00f, 1.00f)))
                Util.OpenLink(v.DiscordUrl);
            if (hasW) ImGui.SameLine(0, spc);
        }

        if (hasW)
        {
            if (BigButton("Website##spotwebsite", each,
                    new Vector4(0.16f, 0.30f, 0.54f, 0.82f),
                    new Vector4(0.22f, 0.42f, 0.72f, 0.96f),
                    new Vector4(0.28f, 0.52f, 0.88f, 1.00f),
                    new Vector4(0.76f, 0.88f, 1.00f, 1.00f)))
                Util.OpenLink(v.WebsiteUrl);
        }
    }

    private readonly struct CardScope
    {
        public readonly Vector2 Tl;
        public readonly float   W;
        public readonly float   PadX;
        public readonly float   PadY;
        public readonly Vector4 Accent;

        public CardScope(Vector2 tl, float w, float padX, float padY, Vector4 accent)
        {
            Tl     = tl;
            W      = w;
            PadX   = padX;
            PadY   = padY;
            Accent = accent;
        }
    }

    private static CardScope BeginCard(Vector4 accent)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        float padX = 13f * gs;
        float padY = 11f * gs;
        var   tl   = ImGui.GetCursorScreenPos();
        float w    = ImGui.GetContentRegionAvail().X;
        var   dl   = ImGui.GetWindowDrawList();

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.Dummy(new Vector2(0f, padY));
        ImGui.Indent(padX);

        return new CardScope(tl, w, padX, padY, accent);
    }

    private static void EndCard(CardScope c)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();

        ImGui.Unindent(c.PadX);
        ImGui.Dummy(new Vector2(0f, c.PadY));

        var br = new Vector2(c.Tl.X + c.W, ImGui.GetCursorScreenPos().Y);

        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(c.Tl, br, ImGui.ColorConvertFloat4ToU32(ColCard), 8f * gs);
        dl.AddRect(c.Tl, br, ImGui.ColorConvertFloat4ToU32(ColBorder), 8f * gs, 0, 1f * gs);
        if (c.Accent.W > 0f)
            dl.AddRectFilled(
                c.Tl + new Vector2(0f, 7f * gs),
                new Vector2(c.Tl.X + 3f * gs, br.Y - 7f * gs),
                ImGui.ColorConvertFloat4ToU32(c.Accent with { W = 0.90f }), 2f);
        dl.ChannelsMerge();
    }

    private void SectionHeader(string label)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        var   p  = ImGui.GetCursorScreenPos();
        float h  = ImGui.GetTextLineHeight();
        dl.AddRectFilled(
            p + new Vector2(-7f * gs, 2f * gs),
            p + new Vector2(-4f * gs, h),
            ImGui.ColorConvertFloat4ToU32(_accent), 1f);
        using (ImRaii.PushColor(ImGuiCol.Text, ColSection))
            ImGui.TextUnformatted(label.ToUpperInvariant());
    }

    private void DrawTimeChip(string time)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        var   p0 = ImGui.GetCursorScreenPos();
        var   ts = ImGui.CalcTextSize(time);
        float padx = 6f * gs;
        float pady = 2f * gs;
        var   p1 = p0 + new Vector2(ts.X + padx * 2f, ts.Y + pady * 2f);
        dl.AddRectFilled(p0, p1, ImGui.ColorConvertFloat4ToU32(_accent with { W = 0.16f }), 4f * gs);
        dl.AddText(p0 + new Vector2(padx, pady), ImGui.ColorConvertFloat4ToU32(_accent with { W = 0.95f }), time);
        ImGui.Dummy(new Vector2(ts.X + padx * 2f, ts.Y + pady * 2f));
    }

    private static bool BigButton(string label, float width, Vector4 bg, Vector4 hover, Vector4 active, Vector4 text)
    {
        float gs = ImGuiHelpers.GlobalScale;
        using var s1 = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 7f * gs);
        using var s2 = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(12f * gs, 9f * gs));
        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        bg);
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, hover);
        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  active);
        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          text);
        return ImGui.Button(label, new Vector2(width, 0f));
    }

    private static bool CopyButton(string label, float width)
    {
        float gs = ImGuiHelpers.GlobalScale;
        using var s1 = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 5f * gs);
        using var s2 = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f * gs, 4f * gs));
        using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.20f, 0.18f, 0.30f, 0.90f));
        using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.26f, 0.46f, 0.95f));
        using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.38f, 0.32f, 0.58f, 1.00f));
        using var c4 = ImRaii.PushColor(ImGuiCol.Text,          new Vector4(0.86f, 0.82f, 0.98f, 1.00f));
        return ImGui.Button(label, new Vector2(width, 0f));
    }

    private static void Gap(float h) =>
        ImGui.Dummy(new Vector2(0f, h * ImGuiHelpers.GlobalScale));

    public void Dispose() { }
}
