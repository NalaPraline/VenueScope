using System.Collections.Generic;
using System.Linq;
using VenueScope.Models;

namespace VenueScope.Helpers;

/// <summary>
/// Caches tag-filtered event lists per data center.
/// Invalidates when the selected-tag set changes.
/// </summary>
public class EventFilterCache
{
    private readonly Dictionary<string, (List<VenueEvent> filtered, int tagHash)> _cache = new();

    public List<VenueEvent> GetFiltered(string dcKey, List<VenueEvent> allEvents, List<string> selectedTags)
    {
        int hash = ComputeHash(selectedTags);

        if (_cache.TryGetValue(dcKey, out var entry) && entry.tagHash == hash)
            return entry.filtered;

        var filtered = selectedTags.Count == 0
            ? allEvents
            : allEvents.Where(ev => selectedTags.All(t => ev.Tags.Contains(t))).ToList();

        _cache[dcKey] = (filtered.ToList(), hash);
        return filtered;
    }

    private static int ComputeHash(List<string> tags)
    {
        unchecked
        {
            int h = 17;
            foreach (var t in tags.OrderBy(x => x))
                h = h * 31 + t.GetHashCode();
            return h;
        }
    }

    public void Clear() => _cache.Clear();
}
