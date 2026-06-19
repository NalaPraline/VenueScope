using System.Collections.Generic;
using System.Numerics;

namespace VenueScope.Helpers;

public static class SpotlightActivities
{
    public static readonly string[] Preset =
    [
        "DJ",
        "Live Music",
        "Gamba",
        "NSFW",
        "Festival",
        "Bar",
        "Host",
        "Auction",
        "Fashion",
        "Open Mic",
        "Game Night",
        "Tarot",
        "Bingo",
        "Blackjack",
        "Scratch",
        "Wheel",
        "Poker",
        "Truth or Dare",
        "DRT",
        "Raffle",
    ];

    private static readonly Dictionary<string, Vector4> Colors = new()
    {
        ["DJ"]         = new(0.66f, 0.50f, 1.00f, 1f),
        ["Live Music"] = new(0.42f, 0.72f, 1.00f, 1f),
        ["Gamba"]      = new(0.94f, 0.78f, 0.28f, 1f),
        ["NSFW"]       = new(0.96f, 0.40f, 0.52f, 1f),
        ["Festival"]   = new(0.96f, 0.58f, 0.28f, 1f),
        ["Bar"]        = new(0.42f, 0.88f, 0.66f, 1f),
        ["Host"]       = new(0.96f, 0.68f, 0.84f, 1f),
        ["Auction"]    = new(0.84f, 0.74f, 0.46f, 1f),
        ["Fashion"]    = new(0.92f, 0.52f, 0.78f, 1f),
        ["Open Mic"]   = new(0.52f, 0.84f, 0.94f, 1f),
        ["Game Night"] = new(0.58f, 0.82f, 0.46f, 1f),
        ["Tarot"]      = new(0.74f, 0.58f, 0.94f, 1f),
        ["Bingo"]      = new(0.64f, 0.86f, 0.44f, 1f),
        ["Blackjack"]  = new(0.88f, 0.30f, 0.34f, 1f),
        ["Scratch"]    = new(0.60f, 0.84f, 0.86f, 1f),
        ["Wheel"]      = new(0.96f, 0.56f, 0.22f, 1f),
        ["Poker"]      = new(0.90f, 0.74f, 0.34f, 1f),
        ["Truth or Dare"] = new(0.95f, 0.46f, 0.72f, 1f),
        ["DRT"]        = new(0.80f, 0.26f, 0.30f, 1f),
        ["Raffle"]     = new(0.70f, 0.56f, 0.96f, 1f),
    };

    public static Vector4 GetColor(string activity) =>
        Colors.TryGetValue(activity, out var c) ? c : EventRenderer.GetTagColor(activity);
}
