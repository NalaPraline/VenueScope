using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Interface;

namespace VenueScope.Helpers;

public static class ActivityStyle
{
    private static readonly Dictionary<string, (string Hex, FontAwesomeIcon Icon)> Styles = new()
    {
        ["dj"]          = ("f6b3d6", FontAwesomeIcon.Headphones),
        ["live"]        = ("9fd2ff", FontAwesomeIcon.Guitar),
        ["bards"]       = ("8fe0d0", FontAwesomeIcon.Music),
        ["openmic"]     = ("85d6f0", FontAwesomeIcon.Microphone),
        ["bingo"]       = ("a3db70", FontAwesomeIcon.Th),
        ["gamba"]       = ("f0c747", FontAwesomeIcon.Dice),
        ["deathroll"]   = ("e58a6a", FontAwesomeIcon.Skull),
        ["blackjack"]   = ("e06a74", FontAwesomeIcon.Clone),
        ["poker"]       = ("e6bd57", FontAwesomeIcon.Coins),
        ["wheel"]       = ("f58f38", FontAwesomeIcon.SyncAlt),
        ["scratch"]     = ("99d6db", FontAwesomeIcon.Receipt),
        ["triad"]       = ("7fb6f0", FontAwesomeIcon.ThLarge),
        ["trivia"]      = ("b8d977", FontAwesomeIcon.QuestionCircle),
        ["truthordare"] = ("f275b8", FontAwesomeIcon.Comments),
        ["giveaway"]    = ("ffd27a", FontAwesomeIcon.Gift),
        ["raffle"]      = ("c4a0ff", FontAwesomeIcon.TicketAlt),
        ["glam"]        = ("f0a3d8", FontAwesomeIcon.Crown),
        ["contest"]     = ("eb85c7", FontAwesomeIcon.Medal),
        ["tournament"]  = ("7fd1a8", FontAwesomeIcon.Trophy),
        ["auction"]     = ("d6bd75", FontAwesomeIcon.Gavel),
        ["dance"]       = ("f59cb8", FontAwesomeIcon.Star),
        ["fashion"]     = ("d9a6f5", FontAwesomeIcon.Tshirt),
        ["showcase"]    = ("a7c7ff", FontAwesomeIcon.Palette),
        ["speeddating"] = ("ff9fb0", FontAwesomeIcon.Heart),
        ["openstage"]   = ("9fe6b4", FontAwesomeIcon.MicrophoneAlt),
        ["other"]       = ("bcb4c9", FontAwesomeIcon.Star),
    };

    public static Vector4 Color(string kind)
    {
        var hex = Styles.TryGetValue(kind, out var s) ? s.Hex : Styles["other"].Hex;
        int v = int.Parse(hex, NumberStyles.HexNumber);
        return new Vector4(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
    }

    public static FontAwesomeIcon Icon(string kind) =>
        Styles.TryGetValue(kind, out var s) ? s.Icon : FontAwesomeIcon.Star;
}
