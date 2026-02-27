using System;
using System.Text.RegularExpressions;

namespace VenueScope.Helpers;

public enum HousingZone
{
    Mist,
    Goblet,
    LavenderBeds,
    Shirogane,
    Empyreum,
}

public static partial class LocationParser
{
    // Matches: "Ward 8, Plot 12" / "W3 P42" / "ward 1 plot 5" etc.
    [GeneratedRegex(@"w(?:ard)?\s*(\d+)[,\s]+p(?:lot)?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WardPlotRx();

    public static bool TryParseHousing(
        string location, out HousingZone zone, out int ward, out int plot)
    {
        zone = default; ward = 0; plot = 0;
        if (string.IsNullOrWhiteSpace(location)) return false;

        var lo = location.ToLowerInvariant();

        if      (lo.Contains("mist"))     zone = HousingZone.Mist;
        else if (lo.Contains("goblet"))   zone = HousingZone.Goblet;
        else if (lo.Contains("lavender")) zone = HousingZone.LavenderBeds;
        else if (lo.Contains("shiro"))    zone = HousingZone.Shirogane;
        else if (lo.Contains("empyreum")) zone = HousingZone.Empyreum;
        else return false;

        var m = WardPlotRx().Match(location);
        if (!m.Success) return false;

        ward = int.Parse(m.Groups[1].Value);
        plot = int.Parse(m.Groups[2].Value);
        return ward >= 1 && plot >= 1;
    }
}
