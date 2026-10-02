using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace VenueScope.Helpers;

public static class Widgets
{
    public static string Glyph(FontAwesomeIcon icon) => icon.ToIconString();

    public static Vector2 IconSize(FontAwesomeIcon icon, float scale = 1f)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            return ImGui.CalcTextSize(Glyph(icon)) * scale;
    }

    public static void DrawIcon(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 pos, Vector4 color, float scale = 1f)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Palette.U(color), Glyph(icon));
    }

    public static void DrawIconCentered(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 center, Vector4 color, float scale = 1f)
    {
        var size = IconSize(icon, scale);
        DrawIcon(dl, icon, center - size / 2f, color, scale);
    }

    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, Vector4? color = null, bool active = false, float size = 0f)
    {
        float gs = ImGuiHelpers.GlobalScale;
        if (size <= 0f) size = ImGui.GetFrameHeight() + 2f * gs;

        var  p0      = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, new Vector2(size));
        bool hovered = ImGui.IsItemHovered();
        var  dl      = ImGui.GetWindowDrawList();

        var bg = active ? Palette.Accent with { W = 0.20f } : hovered ? Palette.SurfaceHover : Palette.Surface;
        dl.AddRectFilled(p0, p0 + new Vector2(size), Palette.U(bg), 7f * gs);
        var fg = color ?? (active ? Palette.AccentText : hovered ? Palette.Text : Palette.TextSoft);
        DrawIconCentered(dl, icon, p0 + new Vector2(size / 2f), fg);

        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static bool GhostIcon(string id, FontAwesomeIcon? icon, string tooltip, float size)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   p0 = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton(id, new Vector2(size));
        bool hovered = ImGui.IsItemHovered();
        var  dl      = ImGui.GetWindowDrawList();

        if (hovered)
            dl.AddRectFilled(p0, p0 + new Vector2(size), Palette.U(Palette.SurfaceHover), size / 2f);
        if (icon is { } i)
            DrawIconCentered(dl, i, p0 + new Vector2(size / 2f), hovered ? Palette.Text : Palette.TextSoft);

        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static bool PillButton(string id, FontAwesomeIcon icon, string label, Vector4 color, string tooltip = "", float height = 0f)
    {
        float gs = ImGuiHelpers.GlobalScale;
        if (height <= 0f) height = ImGui.GetFrameHeight() + 2f * gs;

        var   iconSz = IconSize(icon, 0.9f);
        var   textSz = ImGui.CalcTextSize(label);
        float padX   = 10f * gs;
        float gap    = label.Length > 0 ? 6f * gs : 0f;
        var   size   = new Vector2(padX * 2f + iconSz.X + gap + textSz.X, height);

        var  p0      = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        var  dl      = ImGui.GetWindowDrawList();

        dl.AddRectFilled(p0, p0 + size, Palette.U(color with { W = hovered ? 0.34f : 0.22f }), 7f * gs);
        var text = Vector4.Lerp(color, Palette.Text, 0.45f);
        DrawIcon(dl, icon, new Vector2(p0.X + padX, p0.Y + (height - iconSz.Y) / 2f), text, 0.9f);
        if (label.Length > 0)
            dl.AddText(new Vector2(p0.X + padX + iconSz.X + gap, p0.Y + (height - textSz.Y) / 2f), Palette.U(text), label);

        if (hovered && !string.IsNullOrEmpty(tooltip))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    public static float PillWidth(FontAwesomeIcon icon, string label)
    {
        float gs = ImGuiHelpers.GlobalScale;
        return 20f * gs + IconSize(icon, 0.9f).X + (label.Length > 0 ? 6f * gs : 0f) + ImGui.CalcTextSize(label).X;
    }

    public static bool Chip(string id, string label, bool selected, Vector4? accent = null, int? count = null)
    {
        float gs    = ImGuiHelpers.GlobalScale;
        var   tsz   = ImGui.CalcTextSize(label);
        string num  = count?.ToString() ?? string.Empty;
        float numW  = num.Length > 0 ? ImGui.CalcTextSize(num).X + 6f * gs : 0f;
        var   size  = new Vector2(tsz.X + numW + 14f * gs, tsz.Y + 5f * gs);
        var   p0   = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        var  dl      = ImGui.GetWindowDrawList();
        var  col     = accent ?? Palette.Accent;

        var bg = selected ? col with { W = 0.24f } : hovered ? Palette.SurfaceHover : Palette.Surface;
        dl.AddRectFilled(p0, p0 + size, Palette.U(bg), size.Y / 2f);
        var fg = selected ? Vector4.Lerp(col, Palette.Text, 0.35f) : hovered ? Palette.Text : Palette.TextSoft;
        dl.AddText(p0 + new Vector2(7f * gs, 2.5f * gs), Palette.U(fg), label);
        if (num.Length > 0)
            dl.AddText(p0 + new Vector2(13f * gs + tsz.X, 2.5f * gs), Palette.U(fg with { W = 0.45f }), num);
        return clicked;
    }

    public static float ChipWidth(string label, int? count = null) =>
        ImGui.CalcTextSize(label).X + 14f * ImGuiHelpers.GlobalScale
        + (count is { } c ? ImGui.CalcTextSize(c.ToString()).X + 6f * ImGuiHelpers.GlobalScale : 0f);

    public static bool NavItem(string id, FontAwesomeIcon? icon, Vector4? dot, string label, int? count, bool active, Vector4? tint = null)
    {
        float gs = ImGuiHelpers.GlobalScale;
        float w  = ImGui.GetContentRegionAvail().X;
        float h  = ImGui.GetTextLineHeight() + 12f * gs;
        var   p0 = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        bool hovered = ImGui.IsItemHovered();
        var  dl      = ImGui.GetWindowDrawList();
        var  col     = tint ?? Palette.Accent;

        if (active)       dl.AddRectFilled(p0, p0 + new Vector2(w, h), Palette.U(col with { W = 0.16f }), 7f * gs);
        else if (hovered) dl.AddRectFilled(p0, p0 + new Vector2(w, h), Palette.U(Palette.Line), 7f * gs);

        float y   = p0.Y + (h - ImGui.GetTextLineHeight()) / 2f;
        float x   = p0.X + 10f * gs;
        var   ink = active ? Vector4.Lerp(col, Palette.Text, 0.40f) : hovered ? Palette.Text : Palette.TextSoft;

        if (dot is { } d)
            dl.AddCircleFilled(new Vector2(x + 7f * gs, p0.Y + h / 2f), 3.5f * gs, Palette.U(d));
        else if (icon is { } i)
            DrawIconCentered(dl, i, new Vector2(x + 7f * gs, p0.Y + h / 2f), active ? ink : Palette.Muted, 0.9f);

        dl.AddText(new Vector2(x + 22f * gs, y), Palette.U(ink), label);

        if (count is { } c)
        {
            string n  = c.ToString();
            float  nw = ImGui.CalcTextSize(n).X;
            dl.AddText(new Vector2(p0.X + w - nw - 10f * gs, y), Palette.U(active ? ink with { W = 0.75f } : Palette.Muted), n);
        }
        return clicked;
    }

    public static void SectionLabel(string text)
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.Dummy(new Vector2(0f, 6f * gs));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10f * gs);
        using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Text, Palette.Muted with { W = 0.85f }))
            ImGui.TextUnformatted(text);
    }

    public static bool Toggle(string id, string label, ref bool value, string hint = "")
    {
        float gs     = ImGuiHelpers.GlobalScale;
        float lineH  = ImGui.GetTextLineHeight();
        var   track  = new Vector2(30f * gs, 17f * gs);
        float hintH  = hint.Length > 0 ? lineH + 2f * gs : 0f;
        float width  = ImGui.GetContentRegionAvail().X;
        var   p0     = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.InvisibleButton(id, new Vector2(width, Math.Max(track.Y, lineH) + hintH + 4f * gs));
        bool hovered = ImGui.IsItemHovered();
        if (clicked) value = !value;

        var dl     = ImGui.GetWindowDrawList();
        var trackP = p0 + new Vector2(0f, (lineH - track.Y) / 2f + 2f * gs);
        var on     = value ? Palette.Accent with { W = 0.85f } : hovered ? Palette.SurfaceHover : Palette.Surface;
        dl.AddRectFilled(trackP, trackP + track, Palette.U(on), track.Y / 2f);
        float knobX = value ? trackP.X + track.X - track.Y / 2f : trackP.X + track.Y / 2f;
        dl.AddCircleFilled(new Vector2(knobX, trackP.Y + track.Y / 2f), track.Y / 2f - 2.5f * gs,
            Palette.U(value ? Palette.Text : Palette.TextSoft), 16);

        float textX = p0.X + track.X + 12f * gs;
        dl.AddText(new Vector2(textX, p0.Y + 2f * gs), Palette.U(hovered ? Palette.Text : Palette.Text with { W = 0.92f }), label);
        if (hint.Length > 0)
            dl.AddText(new Vector2(textX, p0.Y + lineH + 4f * gs), Palette.U(Palette.Muted), hint);
        return clicked;
    }

    public static bool Segment(string id, string[] options, ref int current, float width = 0f)
    {
        float gs      = ImGuiHelpers.GlobalScale;
        float gap     = 3f * gs;
        float total   = width > 0f ? width : ImGui.GetContentRegionAvail().X;
        float each    = (total - gap * (options.Length - 1)) / options.Length;
        float height  = ImGui.GetFrameHeight() + 2f * gs;
        var   start   = ImGui.GetCursorScreenPos();
        var   dl      = ImGui.GetWindowDrawList();
        bool  changed = false;

        dl.AddRectFilled(start - new Vector2(3f * gs), start + new Vector2(total + 3f * gs, height + 3f * gs), Palette.U(Palette.Surface), 9f * gs);
        for (int i = 0; i < options.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, gap);
            var p0 = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"{id}{i}", new Vector2(each, height)) && current != i)
            {
                current = i;
                changed = true;
            }
            bool hovered = ImGui.IsItemHovered();
            if (current == i)  dl.AddRectFilled(p0, p0 + new Vector2(each, height), Palette.U(Palette.Accent with { W = 0.24f }), 7f * gs);
            else if (hovered)  dl.AddRectFilled(p0, p0 + new Vector2(each, height), Palette.U(Palette.Line), 7f * gs);
            var ts = ImGui.CalcTextSize(options[i]);
            dl.AddText(p0 + new Vector2((each - ts.X) / 2f, (height - ts.Y) / 2f),
                Palette.U(current == i ? Palette.AccentText : hovered ? Palette.Text : Palette.TextSoft), options[i]);
        }
        ImGui.Dummy(new Vector2(0f, 2f * gs));
        return changed;
    }

    public static void PageTitle(string title, string line)
    {
        float gs = ImGuiHelpers.GlobalScale;
        var   dl = ImGui.GetWindowDrawList();
        var   p0 = ImGui.GetCursorScreenPos();
        var   sz = MeasureWithSize(title, 1.3f);
        TextWithSize(dl, p0, Palette.Text, title, 1.3f);
        ImGui.Dummy(new Vector2(sz.X, sz.Y + 2f * gs));
        using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(line);
            ImGui.PopTextWrapPos();
        }
        ImGui.Dummy(new Vector2(0f, 6f * gs));
    }

    public static void Group(string title, string hint = "")
    {
        float gs = ImGuiHelpers.GlobalScale;
        ImGui.Dummy(new Vector2(0f, 8f * gs));
        var p0 = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(p0, p0 + new Vector2(ImGui.GetContentRegionAvail().X, 0f), Palette.U(Palette.Line));
        ImGui.Dummy(new Vector2(0f, 8f * gs));
        using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Text, Palette.AccentText))
            ImGui.TextUnformatted(title);
        if (hint.Length > 0)
        {
            using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Text, Palette.Muted))
            {
                ImGui.PushTextWrapPos(0f);
                ImGui.TextUnformatted(hint);
                ImGui.PopTextWrapPos();
            }
        }
        ImGui.Dummy(new Vector2(0f, 2f * gs));
    }

    public static string Ellipsize(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid] + "...").X <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? "..." : text[..lo].TrimEnd() + "...";
    }

    public static void TextWithSize(ImDrawListPtr dl, Vector2 pos, Vector4 color, string text, float scale)
    {
        dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos, Palette.U(color), text);
    }

    public static Vector2 MeasureWithSize(string text, float scale) =>
        ImGui.CalcTextSize(text) * scale;
}
