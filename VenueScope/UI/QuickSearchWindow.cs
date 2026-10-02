using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using VenueScope.Helpers;
using VenueScope.Models;
using VenueScope.Services;

namespace VenueScope.UI;

public sealed class QuickSearchWindow : Window, IDisposable
{
    private const int MaxResults = 8;

    private readonly Configuration     _config;
    private readonly EventCacheService _cache;

    private string            _query = string.Empty;
    private string            _lastQuery = "\0";
    private List<VenueEvent>  _results = new();
    private int               _selected;
    private bool              _focus;
    private int               _frames;

    public QuickSearchWindow(Configuration config, EventCacheService cache)
        : base("##venuescope-quicksearch",
               ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings |
               ImGuiWindowFlags.AlwaysAutoResize)
    {
        _config = config;
        _cache  = cache;
        RespectCloseHotkey = false;
    }

    public void Summon()
    {
        if (IsOpen) { IsOpen = false; return; }
        _query     = string.Empty;
        _lastQuery = "\0";
        _selected  = 0;
        _focus     = true;
        _frames    = 0;
        IsOpen     = true;
        BringToFront();
    }

    public override void PreDraw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   vp = ImGui.GetMainViewport();
        float w  = 560f * gs;
        Position          = new Vector2(vp.Pos.X + (vp.Size.X - w) / 2f, vp.Pos.Y + vp.Size.Y * 0.18f);
        PositionCondition = ImGuiCond.Always;
        Size              = new Vector2(w, 0f);
        SizeCondition     = ImGuiCond.Always;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, Palette.Window with { W = 0.98f });
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f * gs);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 10f) * gs);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(3);
    }

    public override void Draw()
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        _frames++;

        if (_frames > 2 && !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            IsOpen = false;
            return;
        }

        var p0 = ImGui.GetCursorScreenPos();
        float full = ImGui.GetContentRegionAvail().X;
        float h = ImGui.GetFrameHeight() + 8f * gs;
        Widgets.DrawIconCentered(dl, FontAwesomeIcon.Search, p0 + new Vector2(14f * gs, h / 2f), Palette.Accent, 0.95f);
        ImGui.SetCursorScreenPos(p0 + new Vector2(30f * gs, 4f * gs));
        ImGui.SetNextItemWidth(full - 80f * gs);
        if (_focus) { ImGui.SetKeyboardFocusHere(); _focus = false; }
        ImGui.InputTextWithHint("##qs", "Search a venue or an event", ref _query, 100);
        var esc = ImGui.CalcTextSize("Esc");
        dl.AddText(new Vector2(p0.X + full - esc.X - 10f * gs, p0.Y + (h - esc.Y) / 2f), Palette.U(Palette.Muted), "Esc");
        ImGui.SetCursorScreenPos(new Vector2(p0.X, p0.Y + h));
        var lp = ImGui.GetCursorScreenPos();
        dl.AddLine(lp, lp + new Vector2(full, 0f), Palette.U(Palette.Line));
        ImGui.Dummy(new Vector2(0f, 6f * gs));

        if (_query != _lastQuery)
        {
            _lastQuery = _query;
            _results   = Search(_query);
            _selected  = 0;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { IsOpen = false; return; }
        if (_results.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) _selected = (_selected + 1) % _results.Count;
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))   _selected = (_selected - 1 + _results.Count) % _results.Count;
            if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
            {
                Choose(_results[_selected], ImGui.GetIO().KeyCtrl);
                return;
            }
        }

        if (_results.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.Muted);
            ImGui.TextUnformatted(_query.Trim().Length < 2 ? "Type at least two letters of a venue name." : "Nothing found for that name.");
            ImGui.PopStyleColor();
        }

        for (int i = 0; i < _results.Count; i++)
            if (DrawRow(_results[i], i)) { Choose(_results[i], ImGui.GetIO().KeyCtrl); return; }

        ImGui.Dummy(new Vector2(0f, 4f * gs));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * gs);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.Muted);
        ImGui.TextUnformatted("Up and down to choose  ·  Enter to travel  ·  Ctrl+Enter to open the page");
        ImGui.PopStyleColor();
    }

    private bool DrawRow(VenueEvent ev, int index)
    {
        float gs   = ImGuiHelpers.GlobalScale;
        var   dl   = ImGui.GetWindowDrawList();
        float w    = ImGui.GetContentRegionAvail().X;
        float lh   = ImGui.GetTextLineHeight();
        float rowH = lh * 2f + 12f * gs;
        var   p0   = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton($"##qsrow{index}", new Vector2(w, rowH));
        bool hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.GetIO().MouseDelta != Vector2.Zero) _selected = index;
        bool on = index == _selected;

        if (on) dl.AddRectFilled(p0, p0 + new Vector2(w, rowH), Palette.U(Palette.Accent with { W = 0.16f }), 8f * gs);
        dl.AddCircleFilled(p0 + new Vector2(14f * gs, rowH / 2f), 4f * gs, Palette.U(Palette.Source(ev.Source)));

        float tx = p0.X + 28f * gs;
        string hint = on ? (ev.LifestreamCode.Length > 0 ? "Enter to go" : "Enter to open") : string.Empty;
        float hintW = hint.Length > 0 ? ImGui.CalcTextSize(hint).X + 12f * gs : 0f;
        string name = ev.Source == EventSource.FFXIVenue || ev.PlaceName.Length == 0 ? ev.Title : $"{ev.Title}  ·  {ev.PlaceName}";
        dl.AddText(new Vector2(tx, p0.Y + 6f * gs), Palette.U(Palette.Text), Widgets.Ellipsize(name, w - 28f * gs - hintW));
        dl.AddText(new Vector2(tx, p0.Y + 6f * gs + lh), Palette.U(Palette.Muted), Widgets.Ellipsize(Detail(ev), w - 28f * gs - hintW));
        if (hint.Length > 0)
            dl.AddText(new Vector2(p0.X + w - hintW, p0.Y + (rowH - lh) / 2f), Palette.U(Palette.AccentText), hint);
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    private static string Detail(VenueEvent ev)
    {
        var parts = new List<string>();
        if (ev.Source == EventSource.PartyFinder) parts.Add("Party Finder");
        var where = string.Join(", ", new[] { ev.Server, ev.InGameLocation }.Where(s => !string.IsNullOrEmpty(s)));
        if (where.Length > 0) parts.Add(where);
        parts.Add(When(ev));
        return string.Join("  ·  ", parts.Where(s => s.Length > 0));
    }

    private static string When(VenueEvent ev)
    {
        var now   = DateTime.UtcNow;
        var start = ev.StartTime.ToUniversalTime();
        var end   = ev.EndTime?.ToUniversalTime();
        if (start <= now && (end == null || end > now)) return "open now";
        if (end != null && end <= now) return "ended";
        var local = start.ToLocalTime();
        return local.Date == DateTime.Today ? $"today {local:HH:mm}" : local.ToString("ddd HH:mm");
    }

    private List<VenueEvent> Search(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length < 2) return new List<VenueEvent>();

        var now = DateTime.UtcNow;
        return _cache.CachedEvents
            .Where(e => e.EndTime == null || e.EndTime.Value.ToUniversalTime() > now)
            .Select(e => (Event: e, Score: Score(e, q)))
            .Where(x => x.Score > 0)
            .GroupBy(x => x.Event.VenueKey)
            .Select(g => g.OrderByDescending(x => x.Score).ThenBy(x => x.Event.StartTime).First())
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Event.StartTime)
            .Take(MaxResults)
            .Select(x => x.Event)
            .ToList();
    }

    // names first, a little bonus when it is open right now or we can travel there
    private static double Score(VenueEvent e, string q)
    {
        double best = 0;
        foreach (var raw in new[] { e.Title, e.VenueName, e.PlaceName, e.TeamName })
        {
            if (string.IsNullOrEmpty(raw)) continue;
            var s = raw.ToLowerInvariant();
            if (s == q) best = Math.Max(best, 5);
            else if (s.StartsWith(q)) best = Math.Max(best, 4);
            else if (s.Split(' ', '-', '\'', '.').Any(w => w.StartsWith(q))) best = Math.Max(best, 3);
            else if (s.Contains(q)) best = Math.Max(best, 2);
        }
        if (best == 0) return 0;
        var now = DateTime.UtcNow;
        if (e.StartTime.ToUniversalTime() <= now && (e.EndTime == null || e.EndTime.Value.ToUniversalTime() > now)) best += 0.6;
        if (e.LifestreamCode.Length > 0) best += 0.3;
        if (e.Source == EventSource.PartyFinder) best -= 0.4;
        return best;
    }

    private void Choose(VenueEvent ev, bool openPage)
    {
        IsOpen = false;
        if (openPage || ev.LifestreamCode.Length == 0) EventRenderer.OnOpenEvent?.Invoke(ev);
        else EventRenderer.RequestTeleport(ev.Server, ev.LifestreamCode, _config);
    }

    public void Dispose() { }
}
