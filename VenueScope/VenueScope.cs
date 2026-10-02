using System;
using System.Linq;
using Dalamud.Game.ClientState.Keys;
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
    [PluginService] internal static IKeyState               KeyState             { get; private set; } = null!;

    internal static LifestreamIPC  LifestreamIpc   { get; private set; } = null!;
    internal static PartakeService PartakeRef      { get; private set; } = null!;

    private static bool     _awaitingTitleScreen    = false;
    private static bool     _pendingTeleportOnLoad  = false;
    private static DateTime _lastTravelAttempt      = DateTime.MinValue;
    private static DateTime _pendingTeleportReadyAt = DateTime.MinValue;
    private static DateTime _travelStartedAt        = DateTime.MinValue;

    private bool     _pendingHousingCheck = false;
    private DateTime _housingCheckAt      = DateTime.MinValue;
    private uint     _pendingTerritoryId  = 0;

    internal static void BeginPendingTravel()
    {
        _travelStartedAt        = DateTime.UtcNow;
        _awaitingTitleScreen    = true;
        _pendingTeleportOnLoad  = false;
        _pendingTeleportReadyAt = DateTime.MinValue;
        _lastTravelAttempt      = DateTime.MinValue;
    }


    private const string CmdMain  = "/venuescope";
    private const string CmdAlias = "/vs";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("VenueScope");
    private MainWindow      MainWindow      { get; init; }
    private ConfigWindow    ConfigWindow    { get; init; }
    private SpotlightWindow SpotlightWindow { get; init; }
    private EventWindow     EventWindow     { get; init; }
    private QuickSearchWindow QuickSearch   { get; init; }
    private ChangelogWindow Changelog       { get; init; }
    private bool            _quickKeyHeld;

    private readonly PartakeService      _partakeService;
    private readonly FFXIVenueService    _ffxivenueService;
    private readonly VenueScopeService   _venueScopeService;
    private readonly PartyFinderService  _partyFinderService;
    private readonly SynchellService     _synchellService;
    private readonly SpotlightService    _spotlightService;
    private readonly EventCacheService   _cacheService;
    private readonly NotificationService _notificationService;
    private readonly TeamIconCache       _teamIconCache;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Palette.Apply(Configuration);
        if (string.IsNullOrEmpty(Configuration.SynchellApiUrl))
        {
            Configuration.SynchellApiUrl = "https://venuescope-synchells.yunookami.workers.dev/synchells";
            Configuration.Save();
        }
        if (string.IsNullOrEmpty(Configuration.SpotlightApiUrl))
        {
            Configuration.SpotlightApiUrl = "https://venuescope-synchells.yunookami.workers.dev/spotlights";
            Configuration.Save();
        }

        _partakeService      = new PartakeService(Log, DataManager);
        _ffxivenueService    = new FFXIVenueService(Log);
        _venueScopeService   = new VenueScopeService(Log);
        _partyFinderService  = new PartyFinderService(Log);
        _synchellService     = new SynchellService(Log, Configuration.SynchellApiUrl);
        _spotlightService    = new SpotlightService(Log, Configuration.SpotlightApiUrl);
        _cacheService        = new EventCacheService(_partakeService, _ffxivenueService, _venueScopeService, _partyFinderService, _synchellService, _spotlightService, Configuration, Log);
        _notificationService = new NotificationService(_cacheService, Configuration, NotificationManager, Log);
        _teamIconCache       = new TeamIconCache(TextureProvider, Log);
        LifestreamIpc        = new LifestreamIPC(PluginInterface);
        PartakeRef           = _partakeService;

        if (!string.IsNullOrEmpty(Configuration.PendingTravelCharName) || !string.IsNullOrEmpty(Configuration.PendingVenueCode))
            ClearPendingTravel(Configuration);
        EventRenderer.IconCache    = _teamIconCache;
        EventRenderer.FlagService  = _ffxivenueService;

        ClientState.TerritoryChanged += OnTerritoryChanged;
        ClientState.Login            += OnLogin;
        Framework.Update             += OnFrameworkUpdate;

        ConfigWindow = new ConfigWindow(Configuration, _partakeService, _cacheService);
        WindowSystem.AddWindow(ConfigWindow);

        SpotlightWindow = new SpotlightWindow(Configuration);
        WindowSystem.AddWindow(SpotlightWindow);

        EventWindow = new EventWindow(Configuration, _cacheService);
        WindowSystem.AddWindow(EventWindow);
        EventRenderer.OnOpenEvent = EventWindow.Open;

        QuickSearch = new QuickSearchWindow(Configuration, _cacheService);
        WindowSystem.AddWindow(QuickSearch);

        Changelog = new ChangelogWindow();
        WindowSystem.AddWindow(Changelog);
        MainWindow.OnOpenChangelog = Changelog.Show;
        if (Configuration.LastSeenChangelog != ChangelogWindow.Latest)
        {
            Changelog.Show();
            Configuration.LastSeenChangelog = ChangelogWindow.Latest;
            Configuration.Save();
        }

        MainWindow = new MainWindow(_cacheService, _partakeService, Configuration, ConfigWindow.Toggle,
                                    _spotlightService, SpotlightWindow.Open);
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
        SynchellNotifOverlay.Config = Configuration;
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
        SpotlightWindow.Dispose();
        EventWindow.Dispose();
        QuickSearch.Dispose();
        Changelog.Dispose();
        MainWindow.OnOpenChangelog = null;
        EventRenderer.OnOpenEvent = null;

        CommandManager.RemoveHandler(CmdMain);
        CommandManager.RemoveHandler(CmdAlias);

        _notificationService.Dispose();
        _cacheService.Dispose();
        _partakeService.Dispose();
        _ffxivenueService.Dispose();
        _venueScopeService.Dispose();
        _partyFinderService.Dispose();
        _synchellService.Dispose();
        _spotlightService.Dispose();
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
        var s1 = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name.Equals(world1, StringComparison.OrdinalIgnoreCase));
        var s2 = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name.Equals(world2, StringComparison.OrdinalIgnoreCase));
        if (s1 == null || s2 == null) return false;
        return s1.DataCenterId == s2.DataCenterId;
    }

    internal static string? GetServerRegion(string serverName)
    {
        var server = PartakeRef.Servers.Values.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
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
        CheckQuickSearchKey();

        if (_pendingHousingCheck && DateTime.UtcNow >= _housingCheckAt)
        {
            _pendingHousingCheck = false;
            CheckHousingForSynchell();
        }

        // a trip that never finishes is dropped after a few minutes
        // login queues can be long, so the trip itself gets more time than the relog
        var limit = _awaitingTitleScreen ? TimeSpan.FromMinutes(4) : TimeSpan.FromMinutes(20);
        if ((_awaitingTitleScreen || !string.IsNullOrEmpty(Configuration.PendingVenueCode))
            && _travelStartedAt != DateTime.MinValue && DateTime.UtcNow - _travelStartedAt > limit)
        {
            var who = Configuration.PendingTravelCharName;
            bool stuckAtLogin = _awaitingTitleScreen;
            ClearPendingTravel(Configuration);
            if (stuckAtLogin)
                NotificationManager.AddNotification(new Notification
                {
                    Title   = "Could not switch character",
                    Content = $"Logging in as {who} did not work. Check the name and world in Settings, Travel.",
                    Type    = NotificationType.Error,
                });
            return;
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
                _travelStartedAt     = DateTime.UtcNow;
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
            // wait until Lifestream is done with its own world or data center trip
            if (player == null || LifestreamIpc.IsBusy())
            {
                _pendingTeleportReadyAt = DateTime.MinValue;
                return;
            }

            if (_pendingTeleportReadyAt == DateTime.MinValue)
                _pendingTeleportReadyAt = DateTime.UtcNow.AddSeconds(2.5);

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

        if (string.IsNullOrEmpty(Configuration.PendingVenueCode) || _awaitingTitleScreen) return;

        _pendingTeleportOnLoad  = true;
        _pendingTeleportReadyAt = DateTime.MinValue;
    }

    internal static void ClearPendingTravel(Configuration config)
    {
        _awaitingTitleScreen           = false;
        _pendingTeleportOnLoad         = false;
        _travelStartedAt               = DateTime.MinValue;
        config.PendingVenueCode         = string.Empty;
        config.PendingVenueServer       = string.Empty;
        config.PendingExpectedCharacter = string.Empty;
        config.PendingTravelCharName    = string.Empty;
        config.PendingTravelHomeWorld   = string.Empty;
        config.PendingTravelDestination = string.Empty;
        config.Save();
    }

    private void CheckQuickSearchKey()
    {
        if (Configuration.QuickSearchKey == 0 || ConfigWindow.CapturingKey) return;
        var key  = (VirtualKey)Configuration.QuickSearchKey;
        bool down = KeyState[key]
            && KeyState[VirtualKey.CONTROL] == Configuration.QuickSearchCtrl
            && KeyState[VirtualKey.SHIFT]   == Configuration.QuickSearchShift
            && KeyState[VirtualKey.MENU]    == Configuration.QuickSearchAlt;
        if (down && !_quickKeyHeld)
        {
            // eat the key so the game does not act on it too
            KeyState[key] = false;
            QuickSearch.Summon();
        }
        _quickKeyHeld = down;
    }
}
