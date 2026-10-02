using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using VenueScope.Helpers;
using VenueScope.Models;

namespace VenueScope.UI;

public static class SynchellNotifOverlay
{
    private const float ShownFor = 20f;

    public static Configuration? Config;

    private static SynchellEntry? _current;
    private static float          _left;
    private static bool           _placed;
    private static Vector2        _lastPos;
    private static readonly Dictionary<string, DateTime> _copied = new();

    public static void Show(SynchellEntry entry)
    {
        _current = entry;
        _left    = ShownFor;
        _placed  = false;
        _copied.Clear();
    }

    public static void ShowPreview() => Show(new SynchellEntry
    {
        VenueName = "Moonlit Lounge",
        Channels  =
        [
            new SynchellChannel { Name = "Lightless", Id = "moonlit", Password = "example" },
            new SynchellChannel { Name = "PlayerSync", Id = "moonlit", Password = "example" },
        ],
    });

    public static void ResetPosition()
    {
        if (Config == null) return;
        Config.VenueCardX = -1f;
        Config.VenueCardY = -1f;
        Config.Save();
        _placed = false;
    }

    public static void Draw()
    {
        if (_current == null) return;

        float gs    = ImGuiHelpers.GlobalScale;
        float width = 290f * gs;
        var   vp    = ImGui.GetMainViewport();

        if (!_placed)
        {
            ImGui.SetNextWindowPos(StartPosition(vp, width), ImGuiCond.Always);
            _placed = true;
        }
        ImGui.SetNextWindowSize(new Vector2(width, 0f), ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Palette.Window with { W = 0.97f });
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f) * gs);
        bool open = ImGui.Begin("##venuescope-venuecard", flags);
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);

        if (open) DrawBody(width);
        KeepPosition(vp);
        ImGui.End();

        if (!ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId) && !_hovered)
            _left -= ImGui.GetIO().DeltaTime;
        if (_left <= 0f) _current = null;
    }

    private static bool _hovered;

    private static void DrawBody(float width)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        var   entry = _current!;
        _hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        float mark = 34f * gs;
        var   p0   = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(p0, p0 + new Vector2(mark), Palette.U(Palette.Accent with { W = 0.22f }), 8f * gs);
        var initial = entry.VenueName.Length > 0 ? entry.VenueName[..1].ToUpperInvariant() : "?";
        var isz = ImGui.CalcTextSize(initial);
        dl.AddText(p0 + (new Vector2(mark) - isz) / 2f, Palette.U(Palette.AccentText), initial);

        float textX = p0.X + mark + 10f * gs;
        float lineH = ImGui.GetTextLineHeight();
        dl.AddText(new Vector2(textX, p0.Y + mark / 2f - lineH), Palette.U(Palette.Muted), "You are at");
        string name = Widgets.Ellipsize(entry.VenueName, width - mark - 70f * gs);
        dl.AddText(new Vector2(textX, p0.Y + mark / 2f), Palette.U(Palette.Text), name);

        float close = ImGui.GetFrameHeight();
        ImGui.SetCursorScreenPos(new Vector2(p0.X + width - 24f * gs - close, p0.Y + (mark - close) / 2f));
        if (Widgets.GhostIcon("##vcclose", FontAwesomeIcon.Times, "Close", close))
            _left = 0f;
        ImGui.SetCursorScreenPos(new Vector2(p0.X, p0.Y + mark + 8f * gs));

        if (entry.Channels.Count > 0) DrawChannels(entry, width - 24f * gs);

        ImGui.Dummy(new Vector2(0, 6f * gs));
        var wp = ImGui.GetWindowPos();
        var ws = ImGui.GetWindowSize();
        float frac = Math.Clamp(_left / ShownFor, 0f, 1f);
        var bar0 = new Vector2(wp.X + 12f * gs, wp.Y + ws.Y - 5f * gs);
        float barW = ws.X - 24f * gs;
        ImGui.PushClipRect(wp, wp + ws, false);
        dl.AddRectFilled(bar0, bar0 + new Vector2(barW, 2f * gs), Palette.U(Palette.Line), 1f * gs);
        dl.AddRectFilled(bar0, bar0 + new Vector2(barW * frac, 2f * gs), Palette.U(Palette.Accent with { W = _hovered ? 0.5f : 0.9f }), 1f * gs);
        ImGui.PopClipRect();
    }

    private static void DrawChannels(SynchellEntry entry, float w)
    {
        float gs  = ImGuiHelpers.GlobalScale;
        var   dl  = ImGui.GetWindowDrawList();
        var   pad = new Vector2(10f, 8f) * gs;
        var   p0  = ImGui.GetCursorScreenPos();
        float inner = w - pad.X * 2;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(p0 + pad);
        ImGui.BeginGroup();

        int n = 0;
        foreach (var ch in entry.Channels)
        {
            if (n > 0)
            {
                ImGui.Dummy(new Vector2(0, 4f * gs));
                var lp = ImGui.GetCursorScreenPos();
                dl.AddLine(lp, lp + new Vector2(inner, 0), Palette.U(Palette.Line));
                ImGui.Dummy(new Vector2(0, 4f * gs));
            }

            var col = EventRenderer.GetTagColor(ch.Name);
            var pp  = ImGui.GetCursorScreenPos();
            var tsz = ImGui.CalcTextSize(ch.Name);
            var psz = tsz + new Vector2(14f, 4f) * gs;
            dl.AddRectFilled(pp, pp + psz, Palette.U(col with { W = 0.18f }), psz.Y / 2f);
            dl.AddText(pp + new Vector2(7f, 2f) * gs, Palette.U(Vector4.Lerp(col, Palette.Text, 0.3f)), ch.Name);
            ImGui.Dummy(psz);

            Row("ID", ch.Id, $"{n}id", inner);
            Row("Pass", ch.Password, $"{n}pw", inner);
            n++;
        }

        ImGui.EndGroup();
        float bottom = ImGui.GetItemRectMax().Y + pad.Y;
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(p0, new Vector2(p0.X + w, bottom), Palette.U(Palette.Card), 9f * gs);
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(p0.X, bottom));
        ImGui.Dummy(new Vector2(w, 0));
    }

    private static void Row(string label, string value, string id, float w)
    {
        if (string.IsNullOrEmpty(value)) return;
        float gs  = ImGuiHelpers.GlobalScale;
        var   dl  = ImGui.GetWindowDrawList();
        float btn = ImGui.GetFrameHeight();
        var   p   = ImGui.GetCursorScreenPos();
        float ty  = p.Y + (btn - ImGui.GetTextLineHeight()) / 2f;

        dl.AddText(new Vector2(p.X, ty), Palette.U(Palette.Muted), label);
        dl.AddText(new Vector2(p.X + 40f * gs, ty), Palette.U(Palette.Text), Widgets.Ellipsize(value, w - 40f * gs - btn - 6f * gs));

        bool done = _copied.TryGetValue(id, out var at) && (DateTime.UtcNow - at).TotalSeconds < 1.5;
        ImGui.SetCursorScreenPos(new Vector2(p.X + w - btn, p.Y));
        if (Widgets.IconButton($"##vccopy{id}", done ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy,
                done ? "Copied" : $"Copy {label.ToLowerInvariant()}", done ? Palette.Live : Palette.Accent, size: btn))
        {
            ImGui.SetClipboardText(value);
            _copied[id] = DateTime.UtcNow;
        }
    }

    private static Vector2 StartPosition(ImGuiViewportPtr vp, float width)
    {
        if (Config is { VenueCardX: >= 0f, VenueCardY: >= 0f } c)
        {
            var pos = vp.Pos + new Vector2(c.VenueCardX * vp.Size.X, c.VenueCardY * vp.Size.Y);
            pos.X = Math.Clamp(pos.X, vp.Pos.X, vp.Pos.X + vp.Size.X - width);
            pos.Y = Math.Clamp(pos.Y, vp.Pos.Y, vp.Pos.Y + vp.Size.Y - 120f * ImGuiHelpers.GlobalScale);
            _lastPos = pos;
            return pos;
        }
        _lastPos = vp.Pos + new Vector2(vp.Size.X - width - 260f * ImGuiHelpers.GlobalScale, vp.Size.Y * 0.30f);
        return _lastPos;
    }

    private static void KeepPosition(ImGuiViewportPtr vp)
    {
        var pos = ImGui.GetWindowPos();
        if (Config == null || ImGui.IsMouseDown(ImGuiMouseButton.Left) || Vector2.DistanceSquared(pos, _lastPos) < 1f) return;
        _lastPos = pos;
        // kept as a fraction so it stays put after a resolution change
        Config.VenueCardX = (pos.X - vp.Pos.X) / vp.Size.X;
        Config.VenueCardY = (pos.Y - vp.Pos.Y) / vp.Size.Y;
        Config.Save();
    }
}
