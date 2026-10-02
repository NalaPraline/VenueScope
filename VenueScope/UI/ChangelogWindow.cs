using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using VenueScope.Helpers;

namespace VenueScope.UI;

public sealed class ChangelogWindow : Window, IDisposable
{
    private sealed record Release(string Version, string Date, string Summary, string[] New, string[] Improved, string[] Fixed);

    private static readonly Release[] Releases =
    [
        new("1.1.0.0", "October 2026", "A new look, a real event page and two new sources",
            [
                "A full page for every event: description, pictures, gallery, DJ lineup and activities",
                "Venue ads from the Party Finder, with the address read from the ad",
                "Events posted by venue owners on VenueScope (coming soon)",
                "Quick search: bind a key, type a venue name, press Enter to travel",
                "Themes, your own accent color and window opacity",
                "Animated banners and logos (GIF, APNG, WebP)",
            ],
            [
                "New look for the list, the filters and the settings",
                "Data centers follow the region of the character you play",
                "The syncshell card shows ID and password right away, and you can move it",
                "Partake descriptions read much better: titles, links and pictures",
            ],
            [
                "Ctrl+F now jumps to the search box",
                "Some Partake team descriptions showed raw code",
            ]),
        new("1.0.11.0", "August 26th, 2026", "Spotlight DJ logos and activity details",
            ["Spotlight lineups show DJ logos", "Activities can list their start time and price"],
            ["The spotlight banner text can be changed or hidden"],
            []),
        new("1.0.10.0", "June 19th, 2026", "Spotlight banner and live status",
            ["Spotlight banner at the top of the list, with its own window", "Live status and accent colors for spotlights"],
            ["New tags"],
            []),
        new("1.0.9", "May 2026", "Syncshells when you enter a venue",
            ["The venue syncshell pops up when you walk into its house", "Syncshell button on venue cards"],
            ["Syncshells refresh on their own every minute"],
            ["Goblet addresses were not always matched", "A crash when closing the syncshell popup"]),
        new("1.0.7", "March 2026", "Lifestream travel and venue links",
            ["Travel with Lifestream, switching character when the venue is in another region", "Website, Instagram and Discord links on venues", "Hide a venue from the list"],
            ["Cleaner venue addresses"],
            ["Some district names did not work with Lifestream"]),
    ];

    public static string Latest => Releases[0].Version;

    private string _open = Releases[0].Version;

    public ChangelogWindow() : base("What's new##venuescope-changelog", ImGuiWindowFlags.NoCollapse)
    {
        SizeCondition   = ImGuiCond.FirstUseEver;
        Size            = new Vector2(620, 720);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 400), MaximumSize = new Vector2(1000, 1400) };
    }

    public void Show()
    {
        _open  = Releases[0].Version;
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Palette.Window);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Palette.Sidebar);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Palette.SurfaceHover);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f) * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10f * gs);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);
    }

    public override void Draw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        DrawBanner();
        ImGui.Dummy(new Vector2(0f, 10f * gs));

        float footer = ImGui.GetFrameHeight() + 16f * gs;
        using (var body = Dalamud.Interface.Utility.Raii.ImRaii.Child("##changes", new Vector2(0f, -footer), false))
        {
            if (body.Success)
            {
                Widgets.TextWithSize(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), Palette.Text, Releases[0].Summary, 1.2f);
                ImGui.Dummy(Widgets.MeasureWithSize(Releases[0].Summary, 1.2f));
                ImGui.Dummy(new Vector2(0f, 6f * gs));
                foreach (var r in Releases) DrawRelease(r);
            }
        }

        ImGui.Dummy(new Vector2(0f, 4f * gs));
        float bw = Widgets.PillWidth(FontAwesomeIcon.Times, "Close");
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - bw) / 2f);
        if (Widgets.PillButton("##clclose", FontAwesomeIcon.Times, "Close", Palette.TextSoft))
            IsOpen = false;
    }

    private static void DrawBanner()
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        float w  = ImGui.GetContentRegionAvail().X;
        float h  = 118f * gs;
        var   p0 = ImGui.GetCursorScreenPos();
        var   p1 = p0 + new Vector2(w, h);

        var accent = Palette.Accent;
        var deep   = Vector4.Lerp(Palette.Card, accent, 0.35f);
        var bright = Vector4.Lerp(accent, new Vector4(0.98f, 0.55f, 0.75f, 1f), 0.5f);
        dl.AddRectFilledMultiColor(p0, p1, Palette.U(deep), Palette.U(bright), Palette.U(Vector4.Lerp(bright, deep, 0.4f)), Palette.U(deep));
        dl.AddRect(p0, p1, Palette.U(accent with { W = 0.35f }), 10f * gs, 0, 1f);

        const string title = "WHAT'S NEW";
        var tsz = Widgets.MeasureWithSize(title, 2.2f);
        Widgets.TextWithSize(dl, new Vector2(p0.X + (w - tsz.X) / 2f, p0.Y + h / 2f - tsz.Y * 0.62f), new Vector4(1f, 1f, 1f, 0.95f), title, 2.2f);
        var sub = $"VenueScope {Releases[0].Version[..3]}";
        var ssz = ImGui.CalcTextSize(sub);
        dl.AddText(new Vector2(p0.X + (w - ssz.X) / 2f, p0.Y + h / 2f + tsz.Y * 0.45f), Palette.U(new Vector4(1f, 1f, 1f, 0.85f)), sub);
        ImGui.Dummy(new Vector2(w, h));
    }

    private void DrawRelease(Release r)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        var   dl  = ImGui.GetWindowDrawList();
        float w   = ImGui.GetContentRegionAvail().X;
        float h   = ImGui.GetFrameHeight() + 4f * gs;
        bool  on  = _open == r.Version;
        var   p0  = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton($"##rel{r.Version}", new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        dl.AddRectFilled(p0, p0 + new Vector2(w, h), Palette.U(on ? Palette.Accent with { W = 0.18f } : hovered ? Palette.CardHover : Palette.Card), 8f * gs);

        float ty = p0.Y + (h - ImGui.GetTextLineHeight()) / 2f;
        Widgets.DrawIcon(dl, on ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, new Vector2(p0.X + 10f * gs, ty + 1f * gs), on ? Palette.Live : Palette.Muted, 0.75f);
        float x = p0.X + 30f * gs;
        var version = $"v{r.Version}";
        dl.AddText(new Vector2(x, ty), Palette.U(on ? Palette.Live : Palette.Text), version);
        x += ImGui.CalcTextSize(version).X + 10f * gs;
        dl.AddText(new Vector2(x, ty), Palette.U(Palette.Muted), r.Date);
        x += ImGui.CalcTextSize(r.Date).X + 12f * gs;
        dl.AddText(new Vector2(x, ty), Palette.U(Palette.TextSoft), Widgets.Ellipsize(r.Summary, p0.X + w - x - 10f * gs));
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked) _open = on ? string.Empty : r.Version;

        if (on)
        {
            ImGui.Dummy(new Vector2(0f, 4f * gs));
            Group(FontAwesomeIcon.Star, "New", Palette.AccentText, r.New);
            Group(FontAwesomeIcon.Magic, "Improved", Palette.Partake, r.Improved);
            Group(FontAwesomeIcon.Bug, "Fixed", Palette.Soon, r.Fixed);
        }
        ImGui.Dummy(new Vector2(0f, 5f * gs));
    }

    private static void Group(FontAwesomeIcon icon, string label, Vector4 color, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(0f, 3f * gs));
        var p = ImGui.GetCursorScreenPos() + new Vector2(8f * gs, 0f);
        var isz = Widgets.IconSize(icon, 0.85f);
        Widgets.DrawIcon(dl, icon, p + new Vector2(0f, (ImGui.GetTextLineHeight() - isz.Y) / 2f), color, 0.85f);
        dl.AddText(p + new Vector2(isz.X + 7f * gs, 0f), Palette.U(color), label);
        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() + 2f * gs));

        float indent = 26f * gs;
        foreach (var line in lines)
        {
            var bp = ImGui.GetCursorScreenPos();
            dl.AddCircleFilled(bp + new Vector2(indent - 9f * gs, ImGui.GetTextLineHeight() / 2f + 1f * gs), 2.5f * gs, Palette.U(Palette.TextSoft));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 8f * gs);
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.Text);
            ImGui.TextWrapped(line);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, 1f * gs));
        }
    }

    public void Dispose() { }
}
