using Humanizer;
using System;
using System.Globalization;
using System.Collections.Generic;
using VenueScope.Models;

namespace VenueScope.Helpers;

public class EventStringCache
{
    private readonly Dictionary<string, CachedEventStrings> _cache = new();
    private DateTime _lastCacheUpdate = DateTime.Now;
    private string   _cachedUpdateStr = string.Empty;

    public CachedEventStrings GetOrCompute(VenueEvent ev)
    {
        bool shouldRefresh = (DateTime.Now - _lastCacheUpdate).TotalSeconds > 30;

        if (shouldRefresh || !_cache.TryGetValue(ev.Id, out var cached))
        {
            if (shouldRefresh)
            {
                _cache.Clear();
                _lastCacheUpdate = DateTime.Now;
                _cachedUpdateStr = string.Empty;
            }

            var utcNow   = DateTime.UtcNow;
            var startUtc = ev.StartTime.ToUniversalTime();
            var endUtc   = ev.EndTime?.ToUniversalTime();

            bool isLive   = startUtc <= utcNow && (endUtc == null || endUtc.Value > utcNow);
            bool isSoon   = !isLive && startUtc > utcNow && (startUtc - utcNow).TotalMinutes < 30;
            bool hasEnded = endUtc.HasValue && endUtc.Value < utcNow;

            var serverDc = !string.IsNullOrEmpty(ev.Server)
                ? (string.IsNullOrEmpty(ev.DataCenter) ? ev.Server : $"{ev.Server} ({ev.DataCenter})")
                : ev.DataCenter;

            var location = !string.IsNullOrEmpty(ev.InGameLocation)
                ? ev.InGameLocation
                : string.Empty;

            var startLocal = ev.StartTime.ToLocalTime();
            var endLocal   = ev.EndTime?.ToLocalTime();

            cached = new CachedEventStrings
            {
                TimeRange         = endLocal.HasValue ? $"{startLocal:HH:mm} → {endLocal.Value:HH:mm}" : startLocal.ToString("HH:mm"),
                DayLabel          = DayLabel(startLocal),
                StatusLabel       = StatusLabel(isLive, hasEnded, startUtc - utcNow),
                StartsAtHumanized = startUtc.Humanize(),
                EndsAtHumanized   = endUtc.HasValue ? endUtc.Value.Humanize() : "N/A",
                StartsAtLocal     = ev.StartTime.ToLocalTime().ToString("dd/MM HH:mm"),
                EndsAtLocal       = ev.EndTime.HasValue ? ev.EndTime.Value.ToLocalTime().ToString("HH:mm") : "?",
                Tags              = ev.Tags.ToArray(),
                Location          = location,
                ServerDc          = serverDc,
                SourceBadge       = Palette.SourceName(ev.Source).ToUpperInvariant(),
                IsLive            = isLive,
                IsStartingSoon    = isSoon,
                HasEnded          = hasEnded,
            };
            _cache[ev.Id] = cached;
        }
        return cached;
    }

    private static string DayLabel(DateTime local)
    {
        var today = DateTime.Now.Date;
        if (local.Date == today)             return "Today";
        if (local.Date == today.AddDays(1))  return "Tomorrow";
        if (local.Date == today.AddDays(-1)) return "Yesterday";
        return local.ToString("ddd d MMM", CultureInfo.InvariantCulture);
    }

    private static string StatusLabel(bool isLive, bool hasEnded, TimeSpan untilStart)
    {
        if (isLive)   return "Live";
        if (hasEnded) return "Ended";
        if (untilStart.TotalMinutes < 60) return $"in {Math.Max(1, (int)untilStart.TotalMinutes)} min";
        if (untilStart.TotalHours < 24)   return $"in {(int)untilStart.TotalHours} h";
        return string.Empty;
    }

    public string GetLastUpdateString(DateTime lastUpdate)
    {
        if ((DateTime.Now - _lastCacheUpdate).TotalSeconds > 30 || string.IsNullOrEmpty(_cachedUpdateStr))
            _cachedUpdateStr = $"Updated {lastUpdate.Humanize()}";
        return _cachedUpdateStr;
    }

    public void Clear()
    {
        _cache.Clear();
        _cachedUpdateStr = string.Empty;
        _lastCacheUpdate = DateTime.Now;
    }
}
