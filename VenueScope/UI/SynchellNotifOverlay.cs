using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using VenueScope.Models;

namespace VenueScope.UI;

public static class SynchellNotifOverlay
{
    private static SynchellEntry? _current;
    private static bool           _showDetail;
    private static DateTime       _dismissAt;

    private static readonly Vector4 ColPurple = new(0.84f, 0.64f, 1.00f, 1f);
    private static readonly Vector4 ColMuted  = new(0.50f, 0.50f, 0.62f, 1f);
    private static readonly Vector4 ColTitle  = new(0.94f, 0.94f, 1.00f, 1f);

    public static void Show(SynchellEntry entry)
    {
        _current     = entry;
        _showDetail  = false;
        _dismissAt   = DateTime.UtcNow.AddSeconds(15);
    }

    public static void Draw()
    {
        if (_current == null) return;
        if (DateTime.UtcNow >= _dismissAt) { _current = null; return; }

        float gs    = ImGuiHelpers.GlobalScale;
        float width = (_showDetail ? 520f : 420f) * gs;

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(
            new Vector2(viewport.Pos.X + (viewport.Size.X - width) * 0.5f,
                        viewport.Pos.Y + 20f * gs),
            ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);

        var flags = ImGuiWindowFlags.NoMove          | ImGuiWindowFlags.NoResize    |
                    ImGuiWindowFlags.NoTitleBar       | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNav;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding,   8f);
        using (ImRaii.PushColor(ImGuiCol.WindowBg, new Vector4(0.09f, 0.07f, 0.16f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.Border,   new Vector4(0.50f, 0.22f, 0.78f, 0.90f)))
        {
            bool visible = ImGui.Begin("##synchellnotif", flags);
            ImGui.PopStyleVar(2);
            if (!visible) { ImGui.End(); return; }
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ColPurple))
            ImGui.TextUnformatted("Syncshell available");

        float closeX = ImGui.GetContentRegionAvail().X - 20f * gs;
        ImGui.SameLine(closeX);
        using (ImRaii.PushColor(ImGuiCol.Text,          ColMuted))
        using (ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0f, 0f, 0f, 0f)))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.20f, 0.30f, 0.60f)))
        {
            if (ImGui.SmallButton("X##snclose")) { _current = null; ImGui.End(); return; }
        }

        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
            ImGui.TextWrapped(_current!.VenueName);

        using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
            ImGui.TextUnformatted($"{_current.Channels.Count} syncshell(s) registered");

        ImGui.Spacing();

        if (!_showDetail)
        {
            using var c1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.34f, 0.12f, 0.54f, 0.75f));
            using var c2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.46f, 0.18f, 0.72f, 1.00f));
            using var c3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.56f, 0.22f, 0.86f, 1.00f));
            using var c4 = ImRaii.PushColor(ImGuiCol.Text,          ColPurple);
            if (ImGui.Button("  View syncshells  ##snview"))
            {
                _showDetail = true;
                _dismissAt  = DateTime.UtcNow.AddSeconds(60);
            }
        }
        else
        {
            int idx = 0;
            foreach (var ch in _current.Channels)
            {
                if (idx > 0) { ImGui.Spacing(); ImGui.Separator(); }
                ImGui.Spacing();

                using (ImRaii.PushColor(ImGuiCol.Text, ColPurple))
                    ImGui.TextUnformatted(ch.Name);
                ImGui.Spacing();

                using var b1 = ImRaii.PushColor(ImGuiCol.Button,        new Vector4(0.18f, 0.18f, 0.28f, 0.70f));
                using var b2 = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.28f, 0.28f, 0.42f, 0.90f));
                using var b3 = ImRaii.PushColor(ImGuiCol.ButtonActive,  new Vector4(0.36f, 0.36f, 0.54f, 1.00f));

                if (!string.IsNullOrEmpty(ch.Id))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                        ImGui.TextUnformatted("ID");
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
                        ImGui.TextUnformatted(ch.Id);
                    ImGui.SameLine(0, 8);
                    if (ImGui.SmallButton($" Copy ##sncid{idx}"))
                        ImGui.SetClipboardText(ch.Id);
                }

                if (!string.IsNullOrEmpty(ch.Password))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ColMuted))
                        ImGui.TextUnformatted("Pass");
                    ImGui.SameLine(0, 6);
                    using (ImRaii.PushColor(ImGuiCol.Text, ColTitle))
                        ImGui.TextUnformatted(ch.Password);
                    ImGui.SameLine(0, 8);
                    if (ImGui.SmallButton($" Copy ##sncpw{idx}"))
                        ImGui.SetClipboardText(ch.Password);
                }

                ImGui.Spacing();
                idx++;
            }
        }

        ImGui.End();
    }
}
