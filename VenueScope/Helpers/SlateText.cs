using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace VenueScope.Helpers;

public static class SlateText
{
    public static string ToMarkdown(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        if (!text.StartsWith('[')) return text;

        JArray nodes;
        try { nodes = JArray.Parse(text); }
        catch { return text; }

        var sb = new StringBuilder();
        foreach (var node in nodes.OfType<JObject>())
            Block(node, sb, 0);
        return sb.ToString().Trim();
    }

    private static void Block(JObject node, StringBuilder sb, int depth)
    {
        var type = node["type"]?.ToString() ?? "paragraph";
        var children = node["children"] as JArray ?? [];

        switch (type)
        {
            case "image":
                var src = node["url"]?.ToString() ?? node["src"]?.ToString();
                if (!string.IsNullOrEmpty(src)) sb.Append("![](").Append(src).Append(")\n\n");
                return;
            case "divider":
                sb.Append("---\n\n");
                return;
            case "bulleted-list":
            case "numbered-list":
                int n = 1;
                foreach (var item in children.OfType<JObject>())
                {
                    sb.Append(new string(' ', depth * 2))
                      .Append(type == "numbered-list" ? $"{n++}. " : "- ")
                      .Append(Inline(item["children"] as JArray ?? []))
                      .Append('\n');
                }
                sb.Append('\n');
                return;
        }

        var line = Inline(children);
        if (type.StartsWith("heading"))
        {
            if (line.Trim().Length > 0) sb.Append(HeadingMark(type)).Append(line.Trim()).Append("\n\n");
            return;
        }
        if (type == "block-quote")
        {
            foreach (var l in line.Split('\n')) sb.Append("> ").Append(l).Append('\n');
            sb.Append('\n');
            return;
        }

        sb.Append(line).Append("\n\n");
    }

    private static string HeadingMark(string type) => type switch
    {
        "heading-one" => "# ",
        "heading-two" => "## ",
        _             => "### ",
    };

    private static string Inline(JArray children)
    {
        var sb = new StringBuilder();
        foreach (var child in children.OfType<JObject>())
        {
            if (child["text"] is JToken t)
            {
                var s = t.ToString();
                if (s.Trim().Length == 0) { sb.Append(s); continue; }
                var lead  = s[..(s.Length - s.TrimStart().Length)];
                var trail = s[s.TrimEnd().Length..];
                var core  = s.Trim();
                if (child["strikethrough"]?.Value<bool>() == true) core = $"~~{core}~~";
                if (child["italic"]?.Value<bool>() == true)        core = $"*{core}*";
                if (child["bold"]?.Value<bool>() == true)          core = $"**{core}**";
                sb.Append(lead).Append(core).Append(trail);
            }
            else if (child["type"]?.ToString() == "link")
            {
                var label = Inline(child["children"] as JArray ?? []);
                var url   = child["url"]?.ToString() ?? string.Empty;
                sb.Append(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? $"[{label}]({url})" : label);
            }
            else
            {
                sb.Append(Inline(child["children"] as JArray ?? []));
            }
        }
        return sb.ToString();
    }
}
