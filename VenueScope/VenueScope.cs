using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using VenueScope.Helpers;
using VenueScope.Services;
using VenueScope.UI;

namespace VenueScope;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface      { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager       { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log                  { get; private set; } = null!;
    [PluginService] internal static INotificationManager    NotificationManager  { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager          { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        TextureProvider      { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState          { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework            { get; private set; } = null!;

    internal static LifestreamIPC  LifestreamIpc   { get; private set; } = null!;
    internal static PartakeService PartakeRef      { get; private set; } = null!;

    // Framework.Update travel state — polls ConnectAndTravel until title screen is ready
    private static bool     _awaitingTitleScreen    = false;
    private static bool     _pendingTeleportOnLoad  = false;
    private static DateTime _lastTravelAttempt      = DateTime.MinValue;
    private static DateTime _pendingTeleportReadyAt = DateTime.MinValue;

    internal static void BeginPendingTravel()
    {
        _awaitingTitleScreen    = true;
        _pendingTeleportOnLoad  = false;
        _pendingTeleportReadyAt = DateTime.MinValue;
        _lastTravelAttempt      = DateTime.MinValue;
    }


    private const string CmdMain  = "/venuescope";
    private const string CmdAlias = "/vs";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("VenueScope");
    private MainWindow   MainWindow   { get; init; }
    private ConfigWindow ConfigWindow { get; init; }

    private readonly PartakeService      _partakeService;
    private readonly FFXIVenueService    _ffxivenueService;
    private readonly EventCacheService   _cacheService;
    private readonly NotificationService _notificationService;
    private readonly TeamIconCache       _teamIconCache;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _partakeService      = new PartakeService(Log, DataManager);
        _ffxivenueService    = new FFXIVenueService(Log);
        _cacheService        = new EventCacheService(_partakeService, _ffxivenueService, Configuration, Log);
        _notificationService = new NotificationService(_cacheService, Configuration, NotificationManager, Log);
        _teamIconCache       = new TeamIconCache(TextureProvider, Log);
        LifestreamIpc        = new LifestreamIPC(PluginInterface);
        PartakeRef           = _partakeService;

        // Clear any stale travel pending from a prior interrupted session
        if (!string.IsNullOrEmpty(Configuration.PendingTravelCharName))
        {
            Configuration.PendingTravelCharName    = string.Empty;
            Configuration.PendingTravelHomeWorld   = string.Empty;
            Configuration.PendingTravelDestination = string.Empty;
            Configuration.Save();
        }
        EventRenderer.IconCache    = _teamIconCache;
        EventRenderer.FlagService  = _ffxivenueService;
        // OnHideVenue set by MainWindow after construction

        ClientState.TerritoryChanged += OnTerritoryChanged;
        ClientState.Login            += OnLogin;
        Framework.Update             += OnFrameworkUpdate;

        ConfigWindow = new ConfigWindow(Configuration, _partakeService, _cacheService);
        WindowSystem.AddWindow(ConfigWindow);

        MainWindow = new MainWindow(_cacheService, _partakeService, Configuration, ConfigWindow.Toggle);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CmdMain, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open VenueScope FFXIV community event browser"
        });
        CommandManager.AddHandler(CmdAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /venuescope"
        });

        PluginInterface.UiBuilder.Draw         += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi   += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        _cacheService.Start();

        Log.Information("Plugin loaded. Use /vs to open.");
    }

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        ClientState.Login            -= OnLogin;
        Framework.Update             -= OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw         -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi   -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CmdMain);
        CommandManager.RemoveHandler(CmdAlias);

        _notificationService.Dispose();
        _cacheService.Dispose();
        _partakeService.Dispose();
        _ffxivenueService.Dispose();
        _teamIconCache.Dispose();
        LifestreamIpc.Dispose();

        Log.Information("Plugin unloaded.");
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi()   => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    internal static bool IsLifestreamAvailable() =>
        PluginInterface.InstalledPlugins.Any(p => p.InternalName == "Lifestream" && p.IsLoaded);

    // Returns "Japan" | "North America" | "Europe" | "Oceania" for the current logged-in character, or null.
    internal static string? GetCurrentCharacterRegion()
    {
#pragma warning disable CS0618
        if (ClientState.LocalPlayer == null) return null;
        int worldId = (int)ClientState.LocalPlayer.HomeWorld.RowId;
#pragma warning restore CS0618
        if (!PartakeRef.Servers.TryGetValue(worldId, out var server)) return null;
        if (!PartakeRef.DataCenters.TryGetValue(server.DataCenterId, out var dc)) return null;
        return PartakeService.RegionList.ElementAtOrDefault(dc.Region);
    }

    // Returns true if both world names belong to the same data center.
    internal static bool AreSameDC(string world1, string world2)
    {
        var s1 = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name == world1);
        var s2 = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name == world2);
        if (s1 == null || s2 == null) return false;
        return s1.DataCenterId == s2.DataCenterId;
    }

    // Returns the region name for a given server name, or null.
    internal static string? GetServerRegion(string serverName)
    {
        var server = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name == serverName);
        if (server == null) return null;
        if (!PartakeRef.DataCenters.TryGetValue(server.DataCenterId, out var dc)) return null;
        return PartakeService.RegionList.ElementAtOrDefault(dc.Region);
    }

    // Polls ConnectAndTravel every second after Logout() until the title screen is ready.
    // Also handles deferred teleport execution when LocalPlayer wasn't available at TerritoryChanged time.
    private void OnFrameworkUpdate(IFramework framework)
    {
        // Phase 1 — wait for title screen, then trigger ConnectAndTravel
        if (_awaitingTitleScreen)
        {
            if (string.IsNullOrEmpty(Configuration.PendingTravelCharName)) { _awaitingTitleScreen = false; return; }

            if ((DateTime.UtcNow - _lastTravelAttempt).TotalSeconds < 1.0) return;
            _lastTravelAttempt = DateTime.UtcNow;

            bool ok;
            if (string.Equals(Configuration.PendingTravelDestination, Configuration.PendingTravelHomeWorld, StringComparison.OrdinalIgnoreCase))
            {
                // Venue is on home world — just log in directly
                ok = LifestreamIpc.ConnectAndLogin(
                    Configuration.PendingTravelCharName,
                    Configuration.PendingTravelHomeWorld);
            }
            else if (AreSameDC(Configuration.PendingTravelDestination, Configuration.PendingTravelHomeWorld))
            {
                // Same DC, different world — log in at home world, ExecuteCommand handles same-DC world visit
                ok = LifestreamIpc.ConnectAndTravel(
                    Configuration.PendingTravelCharName,
                    Configuration.PendingTravelHomeWorld,
                    Configuration.PendingTravelHomeWorld,
                    false);
            }
            else
            {
                // Different DC — travel directly to venue's world from title screen
                ok = LifestreamIpc.ConnectAndTravel(
                    Configuration.PendingTravelCharName,
                    Configuration.PendingTravelHomeWorld,
                    Configuration.PendingTravelDestination,
                    false);
            }

            if (ok)
            {
                Log.Information($"ConnectAndLogin accepted: {Configuration.PendingTravelCharName}@{Configuration.PendingTravelHomeWorld}");
                _awaitingTitleScreen = false;
                Configuration.PendingTravelCharName    = string.Empty;
                Configuration.PendingTravelHomeWorld   = string.Empty;
                Configuration.PendingTravelDestination = string.Empty;
                Configuration.Save();
                // PendingVenueCode + PendingExpectedCharacter remain set — handled after login
            }
            else
            {
                Log.Debug("ConnectAndLogin not ready yet, retrying...");
            }
            return;
        }

        // Phase 2 — TerritoryChanged fired but LocalPlayer was null; wait until it's available then execute
        if (_pendingTeleportOnLoad && !string.IsNullOrEmpty(Configuration.PendingVenueCode))
        {
#pragma warning disable CS0618
            var player = ClientState.LocalPlayer;
#pragma warning restore CS0618
            if (player == null) return;

            // Start the 1.5s countdown the first time LocalPlayer is available
            if (_pendingTeleportReadyAt == DateTime.MinValue)
                _pendingTeleportReadyAt = DateTime.UtcNow.AddSeconds(1.5);

            if (DateTime.UtcNow < _pendingTeleportReadyAt) return;

            _pendingTeleportOnLoad = false;

            if (!string.IsNullOrEmpty(Configuration.PendingExpectedCharacter))
            {
#pragma warning disable CS0618
                var homeWorldId = (int)player.HomeWorld.RowId;
#pragma warning restore CS0618
                var world   = PartakeRef.Servers.TryGetValue(homeWorldId, out var srv) ? srv.Name : string.Empty;
                var current = $"{player.Name.TextValue}@{world}";
                if (!Configuration.PendingExpectedCharacter.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning($"Character mismatch: expected {Configuration.PendingExpectedCharacter}, got {current}. Aborting pending teleport.");
                    Configuration.PendingVenueCode         = string.Empty;
                    Configuration.PendingVenueServer       = string.Empty;
                    Configuration.PendingExpectedCharacter = string.Empty;
                    Configuration.Save();
                    return;
                }
            }

            string args = Configuration.PendingVenueCode;
            Configuration.PendingVenueCode         = string.Empty;
            Configuration.PendingVenueServer       = string.Empty;
            Configuration.PendingExpectedCharacter = string.Empty;
            Configuration.Save();

            LifestreamIpc.ExecuteCommand(args);
            Log.Information($"Pending teleport executed (deferred): {args}");
        }
    }

    // Fires on character login — used as primary trigger when TerritoryChanged doesn't fire (same zone)
    private void OnLogin()
    {
        if (!string.IsNullOrEmpty(Configuration.PendingVenueCode))
        {
            _pendingTeleportOnLoad  = true;
            _pendingTeleportReadyAt = DateTime.MinValue;
        }
    }

    // Fires when territory finishes loading — more reliable than Login for travel commands
    private void OnTerritoryChanged(ushort territoryId)
    {
        if (string.IsNullOrEmpty(Configuration.PendingVenueCode)) return;

        // Verify we're on the expected character if set
        if (!string.IsNullOrEmpty(Configuration.PendingExpectedCharacter))
        {
#pragma warning disable CS0618
            var player = ClientState.LocalPlayer;
#pragma warning restore CS0618
            // LocalPlayer may be null on the first TerritoryChanged at login — defer to Framework.Update
            if (player == null) { _pendingTeleportOnLoad = true; return; }

#pragma warning disable CS0618
            var homeWorldId = (int)player.HomeWorld.RowId;
#pragma warning restore CS0618
            var world   = PartakeRef.Servers.TryGetValue(homeWorldId, out var srv) ? srv.Name : string.Empty;
            var current = $"{player.Name.TextValue}@{world}";
            if (!Configuration.PendingExpectedCharacter.Equals(current, System.StringComparison.OrdinalIgnoreCase))
            {
                Configuration.PendingVenueCode         = string.Empty;
                Configuration.PendingVenueServer       = string.Empty;
                Configuration.PendingExpectedCharacter = string.Empty;
                Configuration.Save();
                return;
            }
        }

        string args = Configuration.PendingVenueCode;

        Configuration.PendingVenueCode         = string.Empty;
        Configuration.PendingVenueServer       = string.Empty;
        Configuration.PendingExpectedCharacter = string.Empty;
        Configuration.Save();

        LifestreamIpc.ExecuteCommand(args);
        Log.Information($"Pending teleport executed: {args}");
    }
}
