using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Utility;

namespace VenueScope.Helpers;

public static class RichText
{
    private enum Kind { Paragraph, Heading, Bullet, Numbered, Quote, Small, Rule, Gap, Image }

    [Flags]
    private enum Style { None = 0, Bold = 1, Italic = 2, Strike = 4, Code = 8 }

    private sealed record Run(string Text, Style Style, string? Url);

    private sealed record Block(Kind Kind, List<Run> Runs, int Level = 0, string Marker = "");

    private static readonly Vector4 ColBody    = new(0.80f, 0.78f, 0.85f, 1f);
    private static readonly Vector4 ColStrong  = Palette.Text;
    private static readonly Vector4 ColItalic  = new(0.84f, 0.80f, 0.94f, 1f);
    private static readonly Vector4 ColHeading = Palette.AccentText;
    private static readonly Vector4 ColLink    = new(0.55f, 0.75f, 1.00f, 1f);
    private static readonly Vector4 ColCode    = new(0.70f, 0.86f, 0.78f, 1f);
    private static readonly Vector4 ColQuote   = new(0.66f, 0.63f, 0.72f, 1f);

    private static readonly Regex DiscordTime  = new(@"<t:(-?\d{1,12})(?::([tTdDfFR]))?>", RegexOptions.Compiled);
    private static readonly Regex DiscordEmoji = new(@"<a?:\w+:\d+>", RegexOptions.Compiled);
    private static readonly Regex DiscordMention = new(@"<(?:@[!&]?|#)\d+>", RegexOptions.Compiled);
    private static readonly Regex AngleLink    = new(@"<(https?://[^\s>]+)>", RegexOptions.Compiled);
    private static readonly Regex ColonEmoji   = new(@":[A-Za-z0-9_+\-]{2,}:", RegexOptions.Compiled);
    private static readonly Regex Image        = new(@"!\[[^\]]*\]\((\S+?)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
    private static readonly Regex ImageUrl     = new(@"^https?://\S+\.(png|jpe?g|webp|gif)(\?\S*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string ImageMark = "@@img ";
    private static readonly Regex Spaces       = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex Inline       = new(
        @"(\*\*|__)(?<b>.+?)\1|~~(?<s>.+?)~~|`(?<c>[^`]+)`|\[(?<lt>[^\]]+)\]\((?<lu>https?://[^\s)]+)\)|(?<u>https?://[^\s<>)]+)|(?<![\w*])[*_](?<i>[^*_\s][^*_]*?)[*_](?![\w*])",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, List<Block>> Cache = new();
    private static float right;

    public static bool HasContent(string text)
    {
        foreach (var b in Parse(text))
            if (b.Kind is not (Kind.Rule or Kind.Gap)) return true;
        return false;
    }

    public static void Draw(string text, float width = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        float gs = ImGuiHelpers.GlobalScale;
        right = ImGui.GetCursorScreenPos().X + (width > 0 ? width : ImGui.GetContentRegionAvail().X);
        float lineH = ImGui.GetTextLineHeight();

        foreach (var b in Parse(text))
        {
            switch (b.Kind)
            {
                case Kind.Gap:
                    ImGui.Dummy(new Vector2(0, lineH * 0.35f));
                    break;
                case Kind.Rule:
                    ImGui.Dummy(new Vector2(0, 2 * gs));
                    ImGui.Separator();
                    ImGui.Dummy(new Vector2(0, 2 * gs));
                    break;
                case Kind.Heading:
                    ImGui.Dummy(new Vector2(0, (b.Level == 1 ? 4 : 2) * gs));
                    DrawRuns(b.Runs, ColHeading, Style.Bold);
                    break;
                case Kind.Bullet:
                case Kind.Numbered:
                {
                    float indent = (14 + b.Level * 12) * gs;
                    ImGui.Indent(indent);
                    var pos = ImGui.GetCursorScreenPos();
                    var marker = b.Kind == Kind.Bullet ? "\u2022" : b.Marker;
                    float mw = ImGui.CalcTextSize(marker).X;
                    ImGui.GetWindowDrawList().AddText(new Vector2(pos.X - mw - 5 * gs, pos.Y), Palette.U(Palette.Accent), marker);
                    DrawRuns(b.Runs, ColBody, Style.None);
                    ImGui.Unindent(indent);
                    break;
                }
                case Kind.Quote:
                {
                    float indent = 12 * gs;
                    var top = ImGui.GetCursorScreenPos();
                    ImGui.Indent(indent);
                    DrawRuns(b.Runs, ColQuote, Style.None);
                    ImGui.Unindent(indent);
                    float bottom = ImGui.GetItemRectMax().Y;
                    ImGui.GetWindowDrawList().AddRectFilled(
                        new Vector2(top.X + 2 * gs, top.Y), new Vector2(top.X + 5 * gs, bottom),
                        Palette.U(Palette.Accent with { W = 0.55f }), 2 * gs);
                    break;
                }
                case Kind.Image:
                    DrawImage(b.Marker);
                    break;
                case Kind.Small:
                    DrawRuns(b.Runs, Palette.Muted, Style.None);
                    break;
                default:
                    DrawRuns(b.Runs, ColBody, Style.None);
                    break;
            }
        }
    }

    private static void DrawImage(string url)
    {
        var cache = EventRenderer.IconCache;
        if (cache == null || cache.HasFailed(url)) return;

        float gs    = ImGuiHelpers.GlobalScale;
        float avail = Math.Max(40 * gs, right - ImGui.GetCursorScreenPos().X);
        var   tex   = cache.GetOrQueue(url);
        var   dl    = ImGui.GetWindowDrawList();
        var   p0    = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new Vector2(0, 3 * gs));
        p0 = ImGui.GetCursorScreenPos();
        if (tex == null || tex.Width == 0)
        {
            var size = new Vector2(avail, avail * 0.3f);
            dl.AddRectFilled(p0, p0 + size, Palette.U(Palette.Surface), 8 * gs);
            var label = "Loading image";
            var ts = ImGui.CalcTextSize(label);
            dl.AddText(p0 + (size - ts) / 2, Palette.U(Palette.Muted), label);
            ImGui.Dummy(size);
        }
        else
        {
            float w = Math.Min(avail, tex.Width * gs);
            float h = w * tex.Height / tex.Width;
            float maxH = 640 * gs;
            if (h > maxH) { w *= maxH / h; h = maxH; }
            var size = new Vector2(w, h);
            ImGui.InvisibleButton($"##img{url}", size);
            dl.AddImageRounded(tex.Handle, p0, p0 + size, Vector2.Zero, Vector2.One, 0xFFFFFFFF, 8 * gs);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip("Open the full image");
            }
            if (ImGui.IsItemClicked()) Util.OpenLink(url);
        }
        ImGui.Dummy(new Vector2(0, 3 * gs));
    }

    private static void DrawRuns(List<Run> runs, Vector4 baseColor, Style baseStyle)
    {
        float gs = ImGuiHelpers.GlobalScale;
        float avail = Math.Max(40 * gs, right - ImGui.GetCursorScreenPos().X);
        float x = 0;
        bool any = false;
        var dl = ImGui.GetWindowDrawList();

        foreach (var run in runs)
        {
            var style = run.Style | baseStyle;
            var color = run.Url != null ? ColLink
                : style.HasFlag(Style.Code) ? ColCode
                : style.HasFlag(Style.Bold) && baseColor == ColBody ? ColStrong
                : style.HasFlag(Style.Italic) && baseColor == ColBody ? ColItalic
                : baseColor;

            foreach (var word in Words(run.Text))
            {
                bool space = word == " ";
                float w = ImGui.CalcTextSize(word).X;
                if (any && x + w > avail)
                {
                    if (space) { x = 0; any = false; continue; }
                    x = 0;
                }
                else if (any)
                {
                    ImGui.SameLine(0, 0);
                }
                else if (space)
                {
                    continue;
                }

                var pos = ImGui.GetCursorScreenPos();
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.TextUnformatted(word);
                ImGui.PopStyleColor();
                if (style.HasFlag(Style.Bold) && !space)
                    dl.AddText(pos + new Vector2(0.8f * gs, 0), Palette.U(color), word);
                if (style.HasFlag(Style.Strike) && !space)
                {
                    float mid = pos.Y + ImGui.GetTextLineHeight() * 0.55f;
                    dl.AddLine(new Vector2(pos.X, mid), new Vector2(pos.X + w, mid), Palette.U(color), Math.Max(1, gs));
                }
                if (run.Url != null && !space && ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip(run.Url);
                    float y = pos.Y + ImGui.GetTextLineHeight();
                    dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + w, y), Palette.U(color), Math.Max(1, gs));
                    if (ImGui.IsItemClicked()) Util.OpenLink(run.Url);
                }
                x += w;
                any = true;
            }
        }
        if (!any) ImGui.NewLine();
    }

    private static IEnumerable<string> Words(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ') continue;
            if (i > start) yield return text[start..i];
            yield return " ";
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    private static List<Block> Parse(string text)
    {
        if (Cache.TryGetValue(text, out var hit)) return hit;
        if (Cache.Count > 200) Cache.Clear();

        var blocks = new List<Block>();
        var lines = Clean(text).Split('\n');
        bool lastBlank = true;

        for (int n = 0; n < lines.Length; n++)
        {
            var raw = lines[n];
            var line = raw.Trim();

            if (line.Length == 0)
            {
                if (!lastBlank) blocks.Add(new Block(Kind.Gap, []));
                lastBlank = true;
                continue;
            }

            if (n + 1 < lines.Length && line.Length <= 60 && IsUnderline(lines[n + 1].Trim()) && HasWords(line))
            {
                blocks.Add(new Block(Kind.Heading, Runs(line), lines[n + 1].Trim()[0] == '=' ? 1 : 2));
                n++;
                lastBlank = false;
                continue;
            }

            if (line.StartsWith(ImageMark) || ImageUrl.IsMatch(line))
            {
                var url = line.StartsWith(ImageMark) ? line[ImageMark.Length..].Trim() : line;
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    blocks.Add(new Block(Kind.Image, [], 0, url));
                lastBlank = false;
                continue;
            }

            if (!HasWords(line) && line.StartsWith('#'))
            {
                if (!lastBlank) blocks.Add(new Block(Kind.Gap, []));
                lastBlank = true;
                continue;
            }

            if (!HasWords(line))
            {
                if (blocks.Count > 0 && blocks[^1].Kind != Kind.Rule) blocks.Add(new Block(Kind.Rule, []));
                lastBlank = false;
                continue;
            }
            lastBlank = false;

            Match m;
            if ((m = Regex.Match(line, @"^(#{1,6})\s*(.*)$")).Success)
            {
                blocks.Add(new Block(Kind.Heading, Runs(TrimDecor(m.Groups[2].Value)), m.Groups[1].Length));
            }
            else if (line.StartsWith("-# "))
            {
                blocks.Add(new Block(Kind.Small, Runs(line[3..])));
            }
            else if ((m = Regex.Match(line, @"^(?:>>>|>)\s?(.*)$")).Success)
            {
                blocks.Add(new Block(Kind.Quote, Runs(m.Groups[1].Value)));
            }
            else if ((m = Regex.Match(raw, @"^(\s*)[-*+\u2022]\s+(.*)$")).Success)
            {
                blocks.Add(new Block(Kind.Bullet, Runs(m.Groups[2].Value), Math.Min(2, m.Groups[1].Length / 2)));
            }
            else if ((m = Regex.Match(raw, @"^(\s*)(\d{1,3})[.)]\s+(.*)$")).Success)
            {
                blocks.Add(new Block(Kind.Numbered, Runs(m.Groups[3].Value), Math.Min(2, m.Groups[1].Length / 2), m.Groups[2].Value + "."));
            }
            else
            {
                var decorated = TrimDecor(line);
                bool framed = decorated.Length < line.Length - 2;
                bool label = line.Length < 60 && line.EndsWith(':') && !line.Contains("http");
                blocks.Add(new Block(framed || label ? Kind.Heading : Kind.Paragraph, Runs(decorated), 2));
            }
        }

        while (blocks.Count > 0 && blocks[^1].Kind is Kind.Gap or Kind.Rule) blocks.RemoveAt(blocks.Count - 1);
        while (blocks.Count > 0 && blocks[0].Kind is Kind.Gap or Kind.Rule) blocks.RemoveAt(0);
        Cache[text] = blocks;
        return blocks;
    }

    private static List<Run> Runs(string text)
    {
        var runs = new List<Run>();
        int at = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > at) runs.Add(new Run(text[at..m.Index], Style.None, null));
            if (m.Groups["b"].Success)       runs.AddRange(Nested(m.Groups["b"].Value, Style.Bold));
            else if (m.Groups["s"].Success)  runs.Add(new Run(m.Groups["s"].Value, Style.Strike, null));
            else if (m.Groups["c"].Success)  runs.Add(new Run(m.Groups["c"].Value, Style.Code, null));
            else if (m.Groups["lt"].Success) runs.Add(new Run(m.Groups["lt"].Value, Style.None, m.Groups["lu"].Value));
            else if (m.Groups["u"].Success)  runs.Add(new Run(m.Groups["u"].Value, Style.None, m.Groups["u"].Value));
            else if (m.Groups["i"].Success)  runs.Add(new Run(m.Groups["i"].Value, Style.Italic, null));
            at = m.Index + m.Length;
        }
        if (at < text.Length) runs.Add(new Run(text[at..], Style.None, null));
        for (int i = 0; i < runs.Count; i++)
            if (runs[i].Url == null && runs[i].Text.Contains('*'))
                runs[i] = runs[i] with { Text = runs[i].Text.Replace("*", "") };
        return runs;
    }

    private static IEnumerable<Run> Nested(string text, Style outer)
    {
        foreach (var r in Runs(text)) yield return r with { Style = r.Style | outer };
    }

    private static string Clean(string text)
    {
        text = text.Replace("\r", "");
        text = Image.Replace(text, m => $"\n{ImageMark}{m.Groups[1].Value}\n");
        text = DiscordTime.Replace(text, m => Stamp(m.Groups[1].Value, m.Groups[2].Value));
        text = DiscordEmoji.Replace(text, "");
        text = DiscordMention.Replace(text, "");
        text = AngleLink.Replace(text, "$1");
        text = ColonEmoji.Replace(text, "");
        text = text.Replace("||", "");

        text = text.Normalize(NormalizationForm.FormKC);

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var sb = new StringBuilder(lines[i].Length);
            foreach (var c in lines[i])
                sb.Append(Drawable(c) ? c : ' ');
            var l = sb.ToString();
            if (l.Trim().Length == 0 && lines[i].Trim().Length > 0)
            {
                lines[i] = "---";
                continue;
            }
            var lead = l.Length - l.TrimStart().Length;
            lines[i] = (lead >= 2 && Regex.IsMatch(l, @"^\s*([-*+\u2022]|\d{1,3}[.)])\s") ? l[..lead] : "") + Spaces.Replace(l.Trim(), " ");
        }
        return string.Join('\n', lines);
    }

    private static bool Drawable(char c)
    {
        if (c == '\n' || c == '\t') return true;
        if (char.IsSurrogate(c)) return false;
        if (c < 0x0250) return !char.IsControl(c);
        if (c is >= ' ' and <= '⁯') return c is not ('​' or '‌' or '‍' or '⁠');
        if (c is >= '₠' and <= '⃏') return true;
        if (c is >= '　' and <= 'ヿ') return true;
        if (c is >= '一' and <= '鿿') return true;
        if (c is >= '＀' and <= '￯') return true;
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.DecimalDigitNumber;
    }

    private static string Stamp(string seconds, string style)
    {
        if (!long.TryParse(seconds, out var s)) return "";
        DateTime t;
        try { t = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime; }
        catch { return ""; }
        return style switch
        {
            "t" => t.ToString("HH:mm"),
            "T" => t.ToString("HH:mm:ss"),
            "d" => t.ToString("dd/MM/yyyy"),
            "D" => t.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
            "F" => t.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture),
            "R" => Relative(t),
            _   => t.ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture),
        };
    }

    private static string Relative(DateTime t)
    {
        var d = t - DateTime.Now;
        var abs = d.Duration();
        string span = abs.TotalMinutes < 60 ? $"{Math.Max(1, (int)abs.TotalMinutes)} min"
            : abs.TotalHours < 48 ? $"{(int)abs.TotalHours} h"
            : $"{(int)abs.TotalDays} days";
        return d.Ticks >= 0 ? $"in {span}" : $"{span} ago";
    }

    private static bool HasWords(string line)
    {
        foreach (var c in line)
            if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    private static bool IsUnderline(string line) =>
        line.Length >= 3 && (line.Trim('=').Length == 0 || line.Trim('-').Length == 0);

    private static string TrimDecor(string line)
    {
        int a = 0, b = line.Length;
        while (a < b && IsDecor(line[a])) a++;
        while (b > a && IsDecor(line[b - 1])) b--;
        return line[a..b].Trim();
    }

    private static bool IsDecor(char c) => c is '=' or '-' or '~' or '+' or '|' or ' ' or '\u2022' or '\u2014' or '\u2013';
}
