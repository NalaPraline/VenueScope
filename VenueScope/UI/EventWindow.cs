using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using VenueScope.Helpers;
using VenueScope.Models;

namespace VenueScope.UI;

public sealed class EventWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private VenueEvent? _event;
    private int         _tab;
    private string?     _zoom;

    public EventWindow(Configuration config)
        : base("Event##venuescope-event", ImGuiWindowFlags.None)
    {
        _config = config;
        SizeCondition   = ImGuiCond.FirstUseEver;
        Size            = new Vector2(560, 800);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 380),
            MaximumSize = new Vector2(1200, 1600),
        };
    }

    public void Open(VenueEvent ev)
    {
        _event     = ev;
        _tab       = 0;
        _zoom      = null;
        WindowName = $"{(ev.Title.Length > 0 ? ev.Title : "Event")}##venuescope-event";
        IsOpen     = true;
        BringToFront();
    }

    public override void PreDraw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.WindowBg,         Palette.Window);
        ImGui.PushStyleColor(ImGuiCol.Border,           Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,      Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,    Palette.SurfaceHover);
        ImGui.PushStyleColor(ImGuiCol.TitleBg,          Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive,    Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Palette.Sidebar);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,     new Vector2(14f, 12f) * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize,     10f * gs);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(7);
    }

    public override void Draw()
    {
        var ev = _event;
        if (ev == null) return;
        var src = SourceColor(ev);

        DrawBanner(ev, src);
        Card(src, w => DrawHeader(ev, w));
        Space(10);

        if (ev.Source == EventSource.FFXIVenue)
        {
            Card(null, w =>
            {
                bool any = false;
                if (ev.Openings.Count > 0)
                {
                    Title("Opening hours", "in your time");
                    DrawOpenings(ev, w);
                    any = true;
                }
                if (RichText.HasContent(ev.Description))
                {
                    if (any) Divider(w);
                    Title("About");
                    RichText.Draw(ev.Description, w);
                    any = true;
                }
                if (!any) Muted("This venue has no description yet.");
            });
            return;
        }

        if (ev.Images.Count > 0)
        {
            int tab = _tab;
            if (Widgets.Segment("##evtabs", ["Details", $"Gallery ({ev.Images.Count})"], ref tab, 260f * ImGuiHelpers.GlobalScale))
            {
                _tab  = tab;
                _zoom = null;
            }
            Space(10);
        }

        if (_tab == 1 && ev.Images.Count > 0)
        {
            Card(null, w => DrawGallery(ev, w));
            return;
        }

        Card(null, w =>
        {
            if (RichText.HasContent(ev.Description)) RichText.Draw(ev.Description, w);
            else                                     Muted("No description for this event.");
        });

        if (!string.IsNullOrEmpty(ev.TeamDescription))
        {
            Space(10);
            Card(null, w =>
            {
                Title(ev.TeamName.Length > 0 ? $"About {ev.TeamName}" : "About the team");
                RichText.Draw(ev.TeamDescription, w);
            });
        }
    }

    private void DrawBanner(VenueEvent ev, Vector4 src)
    {
        string url = ev.Source == EventSource.Partake && ev.Images.Count > 0 ? ev.Images[0] : ev.BannerUrl;
        if (string.IsNullOrEmpty(url) || EventRenderer.IconCache?.HasFailed(url) == true) return;

        float gs   = ImGuiHelpers.GlobalScale;
        float w    = ImGui.GetContentRegionAvail().X;
        var   size = new Vector2(w, w * 9f / 16f);
        var   p0   = ImGui.GetCursorScreenPos();
        var   dl   = ImGui.GetWindowDrawList();
        var   tex  = EventRenderer.IconCache?.GetOrQueue(url);

        if (tex == null) dl.AddRectFilled(p0, p0 + size, Palette.U(Palette.Card), 10f * gs);
        else             DrawCover(dl, tex, p0, size, 10f * gs);

        var badge = ev.Source == EventSource.Partake ? "Partake" : "FFXIV Venues";
        Badge(dl, p0 + new Vector2(10f, 10f) * gs, badge, src);

        var (status, col) = Status(ev);
        var ssz = ImGui.CalcTextSize(status);
        Badge(dl, new Vector2(p0.X + w - ssz.X - 26f * gs, p0.Y + 10f * gs), status, col);

        ImGui.Dummy(size);
        Space(10);
    }

    private void DrawHeader(VenueEvent ev, float w)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();

        string iconUrl = !string.IsNullOrEmpty(ev.TeamIconUrl) ? ev.TeamIconUrl : ev.BannerUrl;
        var icon = !string.IsNullOrEmpty(iconUrl) ? EventRenderer.IconCache?.GetOrQueue(iconUrl) : null;
        float iconSz = 46f * gs;
        float textX  = 0f;
        var   top    = ImGui.GetCursorScreenPos();
        if (icon != null)
        {
            DrawCover(dl, icon, top, new Vector2(iconSz), 9f * gs);
            textX = iconSz + 12f * gs;
        }

        ImGui.SetCursorScreenPos(top + new Vector2(textX, 0));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w - textX);
        ImGui.SetWindowFontScale(1.3f);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.Text);
        ImGui.TextUnformatted(ev.Title.Length > 0 ? ev.Title : "Untitled event");
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
        ImGui.PopTextWrapPos();

        var meta = new[] { ev.TeamName.Length > 0 ? $"by {ev.TeamName}" : "", ev.AttendeeCount > 0 ? $"{ev.AttendeeCount} going" : "" }
            .Where(s => s.Length > 0).ToArray();
        if (meta.Length > 0) Muted(string.Join("  ·  ", meta));
        ImGui.EndGroup();
        float bottom = Math.Max(ImGui.GetItemRectMax().Y, icon != null ? top.Y + iconSz : 0f);
        ImGui.SetCursorScreenPos(new Vector2(top.X, bottom));

        Divider(w);

        var start = Local(ev.StartTime);
        var end   = ev.EndTime is { } e ? Local(e) : (DateTime?)null;
        var (status, statusCol) = Status(ev);
        Line(FontAwesomeIcon.Clock, $"{start:dddd d MMMM, HH:mm}" + (end != null ? $" to {end:HH:mm}" : ""), Palette.Text, ("   " + status, statusCol));

        string where = string.Join(", ", new[] { ev.Server, ev.InGameLocation }.Where(s => !string.IsNullOrEmpty(s)).Distinct());
        if (where.Length > 0) Line(FontAwesomeIcon.MapMarkerAlt, where, Palette.TextSoft, null);
        if (ev.Hiring)        Line(FontAwesomeIcon.Briefcase, "Hiring staff", Palette.Live, null);

        Space(8);
        DrawButtons(ev, w);

        if (ev.Tags.Count > 0)
        {
            Space(10);
            DrawTags(ev, w);
        }
    }

    private void DrawButtons(VenueEvent ev, float w)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        float gap  = 6f * gs;
        float maxX = ImGui.GetCursorScreenPos().X + w;
        bool  first = true;

        void Pill(string id, FontAwesomeIcon icon, string label, Vector4 color, string tip, Action click)
        {
            float pw = Widgets.PillWidth(icon, label);
            if (!first)
            {
                ImGui.SameLine(0, gap);
                if (ImGui.GetCursorScreenPos().X + pw > maxX) ImGui.NewLine();
            }
            first = false;
            if (Widgets.PillButton(id, icon, label, color, tip)) click();
        }

        if (!string.IsNullOrEmpty(ev.LifestreamCode))
        {
            bool ls = Plugin.IsLifestreamAvailable();
            Pill("##evgo", FontAwesomeIcon.MapMarkerAlt, "Go", ls ? Palette.Accent : Palette.Muted,
                ls ? $"Teleport: {ev.LifestreamCode}" : "Lifestream is not installed",
                () => EventRenderer.RequestTeleport(ev.Server, ev.LifestreamCode, _config));
        }
        if (!string.IsNullOrEmpty(ev.DiscordUrl))
            Pill("##evdc", FontAwesomeIcon.Comments, "Discord", Palette.TextSoft, ev.DiscordUrl, () => Util.OpenLink(ev.DiscordUrl));
        if (!string.IsNullOrEmpty(ev.WebsiteUrl))
            Pill("##evweb", FontAwesomeIcon.Globe, "Website", Palette.TextSoft, ev.WebsiteUrl, () => Util.OpenLink(ev.WebsiteUrl));
        if (!string.IsNullOrEmpty(ev.InstagramUrl))
            Pill("##evig", FontAwesomeIcon.Camera, "Instagram", Palette.TextSoft, ev.InstagramUrl, () => Util.OpenLink(ev.InstagramUrl));
        if (!string.IsNullOrEmpty(ev.EventUrl))
        {
            var label = ev.Source == EventSource.Partake ? "Partake" : "FFXIV Venues";
            Pill("##evsrc", FontAwesomeIcon.ExternalLinkAlt, label, SourceColor(ev), ev.EventUrl, () => Util.OpenLink(ev.EventUrl));
        }
    }

    private static void DrawTags(VenueEvent ev, float w)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        var   dl   = ImGui.GetWindowDrawList();
        var   pos  = ImGui.GetCursorScreenPos();
        float maxX = pos.X + w;
        float h    = ImGui.GetTextLineHeight() + 5f * gs;
        float x = pos.X, y = pos.Y;

        foreach (var tag in ev.Tags)
        {
            var   col = EventRenderer.GetTagColor(tag);
            float tw  = ImGui.CalcTextSize(tag).X + 14f * gs;
            if (x + tw > maxX && x > pos.X) { x = pos.X; y += h + 5f * gs; }
            dl.AddRectFilled(new Vector2(x, y), new Vector2(x + tw, y + h), Palette.U(col with { W = 0.16f }), h / 2f);
            dl.AddText(new Vector2(x + 7f * gs, y + 2.5f * gs), Palette.U(Vector4.Lerp(col, Palette.Text, 0.25f)), tag);
            x += tw + 5f * gs;
        }
        ImGui.Dummy(new Vector2(w, y - pos.Y + h));
    }

    private void DrawGallery(VenueEvent ev, float w)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();

        if (_zoom != null)
        {
            if (Widgets.PillButton("##evback", FontAwesomeIcon.ArrowLeft, "All images", Palette.TextSoft)) { _zoom = null; return; }
            Space(8);
            var tex = EventRenderer.IconCache?.GetOrQueue(_zoom);
            if (tex == null || tex.Width == 0) { Muted("Loading image"); return; }
            float h  = w * tex.Height / tex.Width;
            var   p0 = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##evzoom", new Vector2(w, h));
            dl.AddImageRounded(tex.Handle, p0, p0 + new Vector2(w, h), Vector2.Zero, Vector2.One, 0xFFFFFFFF, 8f * gs);
            if (ImGui.IsItemHovered()) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip("Open in your browser"); }
            if (ImGui.IsItemClicked()) Util.OpenLink(_zoom);
            return;
        }

        int   cols = w > 600f * gs ? 4 : 3;
        float gap  = 6f * gs;
        float cell = (w - gap * (cols - 1)) / cols;
        int   n    = 0;
        foreach (var url in ev.Images)
        {
            if (EventRenderer.IconCache?.HasFailed(url) == true) continue;
            if (n % cols != 0) ImGui.SameLine(0, gap);
            var  p0      = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton($"##evimg{n}", new Vector2(cell));
            bool hovered = ImGui.IsItemHovered();
            var  tex     = EventRenderer.IconCache?.GetOrQueue(url);
            if (tex == null) dl.AddRectFilled(p0, p0 + new Vector2(cell), Palette.U(Palette.Surface), 8f * gs);
            else             DrawCover(dl, tex, p0, new Vector2(cell), 8f * gs);
            if (hovered)
            {
                dl.AddRect(p0, p0 + new Vector2(cell), Palette.U(Palette.Accent), 8f * gs, 0, 2f * gs);
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            if (clicked) _zoom = url;
            n++;
        }
    }

    private static void DrawOpenings(VenueEvent ev, float w)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        var   dl   = ImGui.GetWindowDrawList();
        var   now  = DateTime.Now;
        bool  next = false;
        float rowH = ImGui.GetTextLineHeight() + 8f * gs;
        float dayW = 110f * gs;

        foreach (var o in ev.Openings.Take(12))
        {
            var s = Local(o.Start);
            var e = o.End is { } end ? Local(end) : (DateTime?)null;
            if ((e ?? s) < now) continue;

            string day  = s.Date == now.Date ? "Today" : s.Date == now.Date.AddDays(1) ? "Tomorrow" : s.ToString("ddd d MMM");
            string time = e != null ? $"{s:HH:mm} to {e:HH:mm}" : s.ToString("HH:mm");
            string note = "";
            var    col  = Palette.TextSoft;
            bool   mark = false;

            if (o.Closed)      { col = Palette.Danger; time = "Closed"; }
            else if (s <= now) { col = Palette.Live; note = "open now"; next = true; mark = true; }
            else if (!next)    { col = Palette.Live; note = "next"; next = true; mark = true; }

            var p = ImGui.GetCursorScreenPos();
            if (mark) dl.AddRectFilled(p, p + new Vector2(w, rowH), Palette.U(Palette.Live with { W = 0.08f }), 7f * gs);
            float ty = p.Y + 4f * gs;
            dl.AddText(new Vector2(p.X + 8f * gs, ty), Palette.U(o.Closed ? Palette.Danger : Palette.Muted), day);
            dl.AddText(new Vector2(p.X + 8f * gs + dayW, ty), Palette.U(col), time);
            if (note.Length > 0)
            {
                var nsz = ImGui.CalcTextSize(note);
                dl.AddText(new Vector2(p.X + w - nsz.X - 8f * gs, ty), Palette.U(col), note);
            }
            ImGui.Dummy(new Vector2(w, rowH));
        }
    }

    // A rounded card like the ones in the list, with an optional colored edge.
    private static void Card(Vector4? stripe, Action<float> body)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        var   dl  = ImGui.GetWindowDrawList();
        float w   = ImGui.GetContentRegionAvail().X;
        var   pad = new Vector2(14f, 12f) * gs;
        var   p0  = ImGui.GetCursorScreenPos();
        float inner = w - pad.X * 2;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(p0 + pad);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + inner);
        body(inner);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        float bottom = ImGui.GetItemRectMax().Y + pad.Y;

        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(p0, new Vector2(p0.X + w, bottom), Palette.U(Palette.Card), 10f * gs);
        if (stripe is { } c)
            dl.AddRectFilled(p0 + new Vector2(0, 10f * gs), new Vector2(p0.X + 3f * gs, bottom - 10f * gs), Palette.U(c with { W = 0.85f }), 2f * gs);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(p0.X, bottom));
        ImGui.Dummy(new Vector2(w, 0));
    }

    private static void Line(FontAwesomeIcon icon, string text, Vector4 color, (string Text, Vector4 Color)? extra)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        var   p   = ImGui.GetCursorScreenPos();
        float lh  = ImGui.GetTextLineHeight();
        var   isz = Widgets.IconSize(icon, 0.85f);
        Widgets.DrawIcon(ImGui.GetWindowDrawList(), icon, new Vector2(p.X + (16f * gs - isz.X) / 2, p.Y + (lh - isz.Y) / 2), Palette.Accent, 0.85f);
        ImGui.SetCursorScreenPos(new Vector2(p.X + 24f * gs, p.Y));
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        if (extra is { } x)
        {
            ImGui.SameLine(0, 0);
            ImGui.PushStyleColor(ImGuiCol.Text, x.Color);
            ImGui.TextUnformatted(x.Text);
            ImGui.PopStyleColor();
        }
        ImGui.SetCursorScreenPos(new Vector2(p.X, ImGui.GetCursorScreenPos().Y + 2f * gs));
    }

    private static void Badge(ImDrawListPtr dl, Vector2 p, string text, Vector4 color)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   sz = ImGui.CalcTextSize(text) + new Vector2(16f, 6f) * gs;
        dl.AddRectFilled(p, p + sz, Palette.U(Palette.Window with { W = 0.82f }), sz.Y / 2f);
        dl.AddRectFilled(p, p + sz, Palette.U(color with { W = 0.20f }), sz.Y / 2f);
        dl.AddText(p + new Vector2(8f, 3f) * gs, Palette.U(Vector4.Lerp(color, Palette.Text, 0.3f)), text);
    }

    private static void Title(string text, string hint = "")
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.AccentText);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        if (hint.Length > 0)
        {
            ImGui.SameLine(0, 8f * ImGuiHelpers.GlobalScale);
            Muted(hint);
        }
        Space(4);
    }

    private static void Divider(float w)
    {
        float gs = ImGuiHelpers.GlobalScale;
        Space(8);
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(p, p + new Vector2(w, 0), Palette.U(Palette.Line), Math.Max(1f, gs));
        Space(8);
    }

    private static void Muted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.Muted);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void Space(float px) => ImGui.Dummy(new Vector2(0, px * ImGuiHelpers.GlobalScale));

    private static Vector4 SourceColor(VenueEvent ev) =>
        ev.Source == EventSource.Partake ? Palette.Partake : Palette.FFXIVenue;

    private static (string, Vector4) Status(VenueEvent ev)
    {
        var start = Local(ev.StartTime);
        var end   = ev.EndTime is { } e ? Local(e) : (DateTime?)null;
        var now   = DateTime.Now;
        if (start <= now && (end == null || end > now)) return ("Live now", Palette.Live);
        if (start > now) return ($"Starts {Until(start - now)}", Palette.Soon);
        return ("Ended", Palette.Muted);
    }

    private static void DrawCover(ImDrawListPtr dl, IDalamudTextureWrap tex, Vector2 p0, Vector2 size, float rounding)
    {
        var uv0 = Vector2.Zero;
        var uv1 = Vector2.One;
        if (tex.Width > 0 && tex.Height > 0)
        {
            float img = (float)tex.Width / tex.Height;
            float box = size.X / size.Y;
            if (img > box)      { float o = (1f - box / img) / 2f; uv0 = new(o, 0); uv1 = new(1 - o, 1); }
            else if (img < box) { float o = (1f - img / box) / 2f; uv0 = new(0, o); uv1 = new(1, 1 - o); }
        }
        dl.AddImageRounded(tex.Handle, p0, p0 + size, uv0, uv1, 0xFFFFFFFF, rounding);
    }

    private static DateTime Local(DateTime d) =>
        d.Kind == DateTimeKind.Local ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime();

    private static string Until(TimeSpan span) =>
        span.TotalDays >= 1    ? $"in {(int)span.TotalDays}d {span.Hours}h"
        : span.TotalHours >= 1 ? $"in {(int)span.TotalHours}h {span.Minutes}m"
        : $"in {Math.Max(1, span.Minutes)}m";

    public void Dispose() { }
}
