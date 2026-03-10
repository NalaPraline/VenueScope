using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using VenueScope.Models;

namespace VenueScope.Services;

/// <summary>Listens for new events from the cache and sends Dalamud notifications.</summary>
public class NotificationService : IDisposable
{
    private readonly EventCacheService    _cache;
    private readonly Configuration        _config;
    private readonly INotificationManager _notifications;
    private readonly IPluginLog           _log;
    
    public NotificationService(EventCacheService cache, Configuration config,
                                INotificationManager notifications, IPluginLog log)
    {
        _cache         = cache;
        _config        = config;
        _notifications = notifications;
        _log           = log;
        _cache.OnNewEventsDetected += HandleNewEvents;
    }

    private void HandleNewEvents(List<VenueEvent> newEvents)
    {
        if (!_config.EnableNotifications) return;

        var filtered = newEvents.AsEnumerable();
        if (_config.NotifyForDataCenters.Count > 0)
            filtered = filtered.Where(e => _config.NotifyForDataCenters.Contains(e.DataCenter, StringComparer.OrdinalIgnoreCase));

        var list = filtered.ToList();
        if (list.Count == 0) return;

        const int MaxSingle = 3;
        if (list.Count > MaxSingle)
        {
            Notify("VenueScope New Events", $"{list.Count} new events available! Open /vs to browse.");
            return;
        }

        foreach (var ev in list)
        {
            var where = string.IsNullOrEmpty(ev.Server) ? ev.DataCenter : $"{ev.Server} ({ev.DataCenter})";
            var time  = ev.StartTime.ToLocalTime().ToString("HH:mm");
            Notify($"VenueScope {ev.Title}", $"Hosted by {ev.Host} on {where} at {time}");
        }
    }

    private void Notify(string title, string body)
    {
        try
        {
            _notifications.AddNotification(new Notification
            {
                Title           = title,
                Content         = body,
                Type            = NotificationType.Info,
                InitialDuration = TimeSpan.FromSeconds(5),
            });
        }
        catch (Exception ex) { _log.Warning($"[Notif] {ex.Message}"); }
    }

    public void Dispose() => _cache.OnNewEventsDetected -= HandleNewEvents;
}
