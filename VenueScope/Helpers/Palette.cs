using System.Collections.Generic;
using System.Numerics;

namespace VenueScope.Helpers;

public sealed record Theme(string Name, Vector4 Window, Vector4 Sidebar, Vector4 Card, Vector4 CardHover,
    Vector4 Surface, Vector4 SurfaceHover, Vector4 Text, Vector4 TextSoft, Vector4 Muted, Vector4 Accent);

public static class Palette
{
    public static readonly List<Theme> Themes =
    [
        new("Night",
            new(0.078f, 0.071f, 0.102f, 1f), new(0.094f, 0.086f, 0.125f, 1f), new(0.110f, 0.098f, 0.145f, 1f), new(0.129f, 0.114f, 0.169f, 1f),
            new(0.125f, 0.110f, 0.161f, 1f), new(0.169f, 0.149f, 0.216f, 1f),
            new(0.94f, 0.92f, 0.98f, 1f), new(0.74f, 0.70f, 0.80f, 1f), new(0.50f, 0.46f, 0.57f, 1f), new(0.65f, 0.55f, 0.98f, 1f)),
        new("Sunset",
            new(0.098f, 0.070f, 0.078f, 1f), new(0.118f, 0.084f, 0.092f, 1f), new(0.141f, 0.100f, 0.108f, 1f), new(0.165f, 0.118f, 0.125f, 1f),
            new(0.157f, 0.112f, 0.118f, 1f), new(0.212f, 0.149f, 0.153f, 1f),
            new(0.99f, 0.93f, 0.90f, 1f), new(0.82f, 0.71f, 0.67f, 1f), new(0.58f, 0.47f, 0.45f, 1f), new(0.96f, 0.62f, 0.48f, 1f)),
        new("Ocean",
            new(0.059f, 0.082f, 0.094f, 1f), new(0.071f, 0.098f, 0.110f, 1f), new(0.086f, 0.118f, 0.133f, 1f), new(0.102f, 0.141f, 0.157f, 1f),
            new(0.098f, 0.133f, 0.149f, 1f), new(0.129f, 0.176f, 0.196f, 1f),
            new(0.90f, 0.97f, 0.96f, 1f), new(0.68f, 0.80f, 0.80f, 1f), new(0.44f, 0.56f, 0.57f, 1f), new(0.37f, 0.83f, 0.77f, 1f)),
        new("Ember",
            new(0.090f, 0.067f, 0.055f, 1f), new(0.110f, 0.082f, 0.067f, 1f), new(0.133f, 0.098f, 0.078f, 1f), new(0.157f, 0.118f, 0.094f, 1f),
            new(0.149f, 0.110f, 0.086f, 1f), new(0.200f, 0.149f, 0.114f, 1f),
            new(0.98f, 0.94f, 0.86f, 1f), new(0.82f, 0.74f, 0.62f, 1f), new(0.57f, 0.50f, 0.40f, 1f), new(0.91f, 0.71f, 0.30f, 1f)),
        new("Mono",
            new(0.071f, 0.071f, 0.078f, 1f), new(0.086f, 0.086f, 0.094f, 1f), new(0.106f, 0.106f, 0.114f, 1f), new(0.125f, 0.125f, 0.137f, 1f),
            new(0.118f, 0.118f, 0.129f, 1f), new(0.161f, 0.161f, 0.176f, 1f),
            new(0.93f, 0.93f, 0.95f, 1f), new(0.72f, 0.72f, 0.76f, 1f), new(0.49f, 0.49f, 0.53f, 1f), new(0.74f, 0.74f, 0.80f, 1f)),
        new("Sakura",
            new(0.086f, 0.063f, 0.098f, 1f), new(0.106f, 0.078f, 0.118f, 1f), new(0.129f, 0.094f, 0.141f, 1f), new(0.153f, 0.110f, 0.165f, 1f),
            new(0.145f, 0.106f, 0.157f, 1f), new(0.196f, 0.141f, 0.212f, 1f),
            new(0.99f, 0.92f, 0.96f, 1f), new(0.82f, 0.70f, 0.78f, 1f), new(0.57f, 0.45f, 0.54f, 1f), new(0.96f, 0.45f, 0.71f, 1f)),
    ];

    private static Theme  _theme   = Themes[0];
    private static Vector4 _accent = Themes[0].Accent;
    private static float  _opacity = 1f;

    public static Theme Current => _theme;

    public static void Apply(Configuration config)
    {
        _theme   = Themes.Find(t => t.Name == config.ThemeName) ?? Themes[0];
        _accent  = config.UseCustomAccent ? config.CustomAccent with { W = 1f } : _theme.Accent;
        _opacity = System.Math.Clamp(config.WindowOpacity, 0.5f, 1f);
    }

    public static Vector4 Window       => _theme.Window with { W = _opacity };
    public static Vector4 Sidebar      => _theme.Sidebar with { W = _opacity };
    public static Vector4 Card         => _theme.Card;
    public static Vector4 CardHover    => _theme.CardHover;
    public static Vector4 Surface      => _theme.Surface;
    public static Vector4 SurfaceHover => _theme.SurfaceHover;
    public static Vector4 Line         => new(1f, 1f, 1f, 0.06f);

    public static Vector4 Text         => _theme.Text;
    public static Vector4 TextSoft     => _theme.TextSoft;
    public static Vector4 Muted        => _theme.Muted;

    public static Vector4 Accent       => _accent;
    public static Vector4 AccentText   => Vector4.Lerp(_accent, new Vector4(1f), 0.55f);

    public static readonly Vector4 Live        = new(0.53f, 0.94f, 0.67f, 1f);
    public static readonly Vector4 Soon        = new(1.00f, 0.72f, 0.36f, 1f);
    public static readonly Vector4 Gold        = new(1.00f, 0.82f, 0.30f, 1f);
    public static readonly Vector4 Danger      = new(0.92f, 0.48f, 0.52f, 1f);

    public static readonly Vector4 Partake     = new(0.38f, 0.65f, 0.98f, 1f);
    public static readonly Vector4 FFXIVenue   = new(0.75f, 0.52f, 0.99f, 1f);
    public static readonly Vector4 VenueScope  = new(0.98f, 0.60f, 0.72f, 1f);
    public static readonly Vector4 PartyFinder = new(0.96f, 0.78f, 0.38f, 1f);

    public static Vector4 Source(Models.EventSource source) => source switch
    {
        Models.EventSource.Partake     => Partake,
        Models.EventSource.VenueScope  => VenueScope,
        Models.EventSource.PartyFinder => PartyFinder,
        _                              => FFXIVenue,
    };

    public static string SourceName(Models.EventSource source) => source switch
    {
        Models.EventSource.Partake     => "Partake",
        Models.EventSource.VenueScope  => "VenueScope",
        Models.EventSource.PartyFinder => "Party Finder",
        _                              => "FFXIV Venues",
    };

    public static uint U(Vector4 color) => Dalamud.Bindings.ImGui.ImGui.ColorConvertFloat4ToU32(color);
}
