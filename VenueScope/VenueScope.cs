using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
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
    [PluginService] internal static IObjectTable            ObjectTable          { get; private set; } = null!;

    internal static LifestreamIPC  LifestreamIpc   { get; private set; } = null!;
    internal static PartakeService PartakeRef      { get; private set; } = null!;

    private static bool     _awaitingTitleScreen    = false;
    private static bool     _pendingTeleportOnLoad  = false;
    private static DateTime _lastTravelAttempt      = DateTime.MinValue;
    private static DateTime _pendingTeleportReadyAt = DateTime.MinValue;

    private bool     _pendingHousingCheck = false;
    private DateTime _housingCheckAt      = DateTime.MinValue;
    private uint     _pendingTerritoryId  = 0;

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
    private readonly SynchellService     _synchellService;
    private readonly EventCacheService   _cacheService;
    private readonly NotificationService _notificationService;
    private readonly TeamIconCache       _teamIconCache;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (string.IsNullOrEmpty(Configuration.SynchellApiUrl))
        {
            Configuration.SynchellApiUrl = "https://venuescope-synchells.yunookami.workers.dev/synchells";
            Configuration.Save();
        }

        _partakeService      = new PartakeService(Log, DataManager);
        _ffxivenueService    = new FFXIVenueService(Log);
        _synchellService     = new SynchellService(Log, Configuration.SynchellApiUrl);
        _cacheService        = new EventCacheService(_partakeService, _ffxivenueService, _synchellService, Configuration, Log);
        _notificationService = new NotificationService(_cacheService, Configuration, NotificationManager, Log);
        _teamIconCache       = new TeamIconCache(TextureProvider, Log);
        LifestreamIpc        = new LifestreamIPC(PluginInterface);
        PartakeRef           = _partakeService;

        if (!string.IsNullOrEmpty(Configuration.PendingTravelCharName))
        {
            Configuration.PendingTravelCharName    = string.Empty;
            Configuration.PendingTravelHomeWorld   = string.Empty;
            Configuration.PendingTravelDestination = string.Empty;
            Configuration.Save();
        }
        EventRenderer.IconCache    = _teamIconCache;
        EventRenderer.FlagService  = _ffxivenueService;

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
        PluginInterface.UiBuilder.Draw         += SynchellNotifOverlay.Draw;
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
        PluginInterface.UiBuilder.Draw         -= SynchellNotifOverlay.Draw;
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
        _synchellService.Dispose();
        _teamIconCache.Dispose();
        LifestreamIpc.Dispose();

        Log.Information("Plugin unloaded.");
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi()   => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    internal static bool IsLifestreamAvailable() =>
        PluginInterface.InstalledPlugins.Any(p => p.InternalName == "Lifestream" && p.IsLoaded);

    internal static string? GetCurrentCharacterRegion()
    {
        if (ObjectTable.LocalPlayer == null) return null;
        int worldId = (int)ObjectTable.LocalPlayer.HomeWorld.RowId;
        if (!PartakeRef.Servers.TryGetValue(worldId, out var server)) return null;
        if (!PartakeRef.DataCenters.TryGetValue(server.DataCenterId, out var dc)) return null;
        return PartakeService.RegionList.ElementAtOrDefault(dc.Region);
    }

    internal static bool AreSameDC(string world1, string world2)
    {
        var s1 = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name == world1);
        var s2 = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name == world2);
        if (s1 == null || s2 == null) return false;
        return s1.DataCenterId == s2.DataCenterId;
    }

    internal static string? GetServerRegion(string serverName)
    {
        var server = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name == serverName);
        if (server == null) return null;
        if (!PartakeRef.DataCenters.TryGetValue(server.DataCenterId, out var dc)) return null;
        return PartakeService.RegionList.ElementAtOrDefault(dc.Region);
    }

    private unsafe void CheckHousingForSynchell()
    {
        if (!Configuration.EnableSyncshellPopup) return;

        var hm = HousingManager.Instance();
        if (hm == null) return;
        if (hm->IndoorTerritory == null) return;

        int ward = hm->GetCurrentWard() + 1;
        int plot = hm->GetCurrentPlot() + 1;
        if (ward <= 0 || plot <= 0 || plot > 60) return;

        var player = ObjectTable.LocalPlayer;
        if (player == null) return;

        var worldId = (int)player.CurrentWorld.RowId;
        if (!PartakeRef.Servers.TryGetValue(worldId, out var serverInfo)) return;

        var synchell = _synchellService.FindByHousing(serverInfo.Name, ward, plot);
        if (synchell == null) return;

        SynchellNotifOverlay.Show(synchell);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_pendingHousingCheck && DateTime.UtcNow >= _housingCheckAt)
        {
            _pendingHousingCheck = false;
            CheckHousingForSynchell();
        }

        if (_awaitingTitleScreen)
        {
            if (string.IsNullOrEmpty(Configuration.PendingTravelCharName)) { _awaitingTitleScreen = false; return; }

            if ((DateTime.UtcNow - _lastTravelAttempt).TotalSeconds < 1.0) return;
            _lastTravelAttempt = DateTime.UtcNow;

            bool ok;
            if (string.Equals(Configuration.PendingTravelDestination, Configuration.PendingTravelHomeWorld, StringComparison.OrdinalIgnoreCase))
            {
                ok = LifestreamIpc.ConnectAndLogin(
                    Configuration.PendingTravelCharName,
                    Configuration.PendingTravelHomeWorld);
            }
            else if (AreSameDC(Configuration.PendingTravelDestination, Configuration.PendingTravelHomeWorld))
            {
                ok = LifestreamIpc.ConnectAndTravel(
                    Configuration.PendingTravelCharName,
                    Configuration.PendingTravelHomeWorld,
                    Configuration.PendingTravelHomeWorld,
                    false);
            }
            else
            {
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
            }
            else
            {
                Log.Debug("ConnectAndLogin not ready yet, retrying...");
            }
            return;
        }

        if (_pendingTeleportOnLoad && !string.IsNullOrEmpty(Configuration.PendingVenueCode))
        {
            var player = ObjectTable.LocalPlayer;
            if (player == null) return;

            if (_pendingTeleportReadyAt == DateTime.MinValue)
                _pendingTeleportReadyAt = DateTime.UtcNow.AddSeconds(1.5);

            if (DateTime.UtcNow < _pendingTeleportReadyAt) return;

            _pendingTeleportOnLoad = false;

            if (!string.IsNullOrEmpty(Configuration.PendingExpectedCharacter))
            {
                var homeWorldId = (int)player.HomeWorld.RowId;
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

    private void OnLogin()
    {
        if (!string.IsNullOrEmpty(Configuration.PendingVenueCode))
        {
            _pendingTeleportOnLoad  = true;
            _pendingTeleportReadyAt = DateTime.MinValue;
        }
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        _pendingTerritoryId  = territoryId;
        _pendingHousingCheck = true;
        _housingCheckAt      = DateTime.UtcNow.AddSeconds(1.5);

        if (string.IsNullOrEmpty(Configuration.PendingVenueCode)) return;

        if (!string.IsNullOrEmpty(Configuration.PendingExpectedCharacter))
        {
            var player = ObjectTable.LocalPlayer;
            if (player == null) { _pendingTeleportOnLoad = true; return; }

            var homeWorldId = (int)player.HomeWorld.RowId;
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
