using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;
using VenueScope.Helpers;

namespace VenueScope.Services;

/// <summary>
/// Provides housing zone map textures (from game files) and approximate
/// ward positions for overlay rendering.
/// </summary>
public static class HousingMapService
{
    public static readonly string[] ZoneNames =
        ["The Mist", "The Goblet", "The Lavender Beds", "Shirogane", "Empyreum"];

    // Main ward territory IDs for each housing zone
    private static readonly Dictionary<HousingZone, uint> TerritoryIds = new()
    {
        { HousingZone.Mist,         339 },
        { HousingZone.Goblet,       340 },
        { HousingZone.LavenderBeds, 345 },
        { HousingZone.Shirogane,    649 },
        { HousingZone.Empyreum,     979 },
    };

    // Texture cache — ISharedImmediateTexture is already cached by Dalamud
    // but we cache the lookup to avoid repeated Lumina queries per frame.
    private static readonly Dictionary<HousingZone, ISharedImmediateTexture?> TexCache = new();

    public static ISharedImmediateTexture? GetMapTexture(HousingZone zone)
    {
        if (TexCache.TryGetValue(zone, out var hit)) return hit;

        ISharedImmediateTexture? tex = null;
        try
        {
            if (!TerritoryIds.TryGetValue(zone, out var territoryId)) return null;

            var territory = Plugin.DataManager
                .GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
            var mapRef = territory?.Map.ValueNullable;
            if (mapRef == null) return null;

            var mapId  = mapRef.Value.Id.ExtractText();
            var path   = $"ui/map/{mapId}/{mapId}_m.tex";

            if (Plugin.DataManager.FileExists(path))
                tex = Plugin.TextureProvider.GetFromGame(path);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[HousingMap] Could not load map texture for {zone}: {ex.Message}");
        }

        TexCache[zone] = tex;
        return tex;
    }

    /// <summary>
    /// Returns approximate FFXIV map coordinates (1–42 scale) for the given ward.
    /// Wards 1–30 are placed on an outer ring, 31–60 on an inner ring.
    /// This is an approximation — precise positions would need HousingLandSet data.
    /// </summary>
    public static Vector2 WardMapCoord(int ward)
    {
        const float cx = 21f, cy = 21f;
        int wi  = ward - 1;        // 0-based
        bool sub = wi >= 30;
        int  idx = wi % 30;

        float radius = sub ? 7.5f : 12f;
        // Start at top (-π/2) and go clockwise
        float angle = idx * (float)(2 * Math.PI / 30) - (float)(Math.PI / 2);

        return new Vector2(
            cx + radius * MathF.Cos(angle),
            cy + radius * MathF.Sin(angle));
    }

    /// <summary>Clears texture cache (call on plugin unload).</summary>
    public static void Clear() => TexCache.Clear();
}
