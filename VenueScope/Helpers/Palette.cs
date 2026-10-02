using System.Numerics;

namespace VenueScope.Helpers;

public static class Palette
{
    public static readonly Vector4 Window        = new(0.078f, 0.071f, 0.102f, 1.00f);
    public static readonly Vector4 Sidebar       = new(0.094f, 0.086f, 0.125f, 1.00f);
    public static readonly Vector4 Card          = new(0.110f, 0.098f, 0.145f, 1.00f);
    public static readonly Vector4 CardHover     = new(0.129f, 0.114f, 0.169f, 1.00f);
    public static readonly Vector4 Surface       = new(0.125f, 0.110f, 0.161f, 1.00f);
    public static readonly Vector4 SurfaceHover  = new(0.169f, 0.149f, 0.216f, 1.00f);
    public static readonly Vector4 Line          = new(1.000f, 1.000f, 1.000f, 0.06f);

    public static readonly Vector4 Text          = new(0.94f, 0.92f, 0.98f, 1f);
    public static readonly Vector4 TextSoft      = new(0.74f, 0.70f, 0.80f, 1f);
    public static readonly Vector4 Muted         = new(0.50f, 0.46f, 0.57f, 1f);

    public static readonly Vector4 Accent        = new(0.65f, 0.55f, 0.98f, 1f);
    public static readonly Vector4 AccentText    = new(0.85f, 0.80f, 1.00f, 1f);
    public static readonly Vector4 Live          = new(0.53f, 0.94f, 0.67f, 1f);
    public static readonly Vector4 Soon          = new(1.00f, 0.72f, 0.36f, 1f);
    public static readonly Vector4 Gold          = new(1.00f, 0.82f, 0.30f, 1f);
    public static readonly Vector4 Danger        = new(0.92f, 0.48f, 0.52f, 1f);

    public static readonly Vector4 Partake       = new(0.38f, 0.65f, 0.98f, 1f);
    public static readonly Vector4 FFXIVenue     = new(0.75f, 0.52f, 0.99f, 1f);

    public static uint U(Vector4 color) => Dalamud.Bindings.ImGui.ImGui.ColorConvertFloat4ToU32(color);
}
