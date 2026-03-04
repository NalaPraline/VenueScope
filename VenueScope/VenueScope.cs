using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using VenueScope.Helpers;
using VenueScope.Services;
using VenueScope.UI;

namespace VenueScope;

/// <summary>VenueScope plugin entry point.</summary>
public sealed class Plugin : IDalamudPlugin
{
    // ── Dalamud services (injected automatically) ─────────────────────────────
    [PluginService] internal static IDalamudPluginInterface PluginInterface      { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager       { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log                  { get; private set; } = null!;
    [PluginService] internal static INotificationManager    NotificationManager  { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager          { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        TextureProvider      { get; private set; } = null!;

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

        // Services — PartakeService needs IDataManager to load DC/server data from Lumina
        _partakeService      = new PartakeService(Log, DataManager);
        _ffxivenueService    = new FFXIVenueService(Log);
        _cacheService        = new EventCacheService(_partakeService, _ffxivenueService, Configuration, Log);
        _notificationService = new NotificationService(_cacheService, Configuration, NotificationManager, Log);
        _teamIconCache       = new TeamIconCache(TextureProvider, Log);
        EventRenderer.IconCache    = _teamIconCache;
        EventRenderer.FlagService  = _ffxivenueService;
        // OnHideVenue is set by MainWindow after construction (needs access to banner state)

        // UI
        ConfigWindow = new ConfigWindow(Configuration, _partakeService, _cacheService);
        WindowSystem.AddWindow(ConfigWindow);

        MainWindow = new MainWindow(_cacheService, _partakeService, Configuration, ConfigWindow.Toggle);
        WindowSystem.AddWindow(MainWindow);

        // Commands
        CommandManager.AddHandler(CmdMain, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open VenueScope — FFXIV community event browser"
        });
        CommandManager.AddHandler(CmdAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /venuescope"
        });

        PluginInterface.UiBuilder.Draw         += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi   += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Start background auto-refresh
        _cacheService.Start();

        Log.Information("[VenueScope] Plugin loaded. Use /vs to open.");
    }

    public void Dispose()
    {
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

        Log.Information("[VenueScope] Plugin unloaded.");
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi()   => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    /// <summary>Returns true if the Lifestream plugin is installed and active.</summary>
    internal static bool IsLifestreamAvailable() =>
        PluginInterface.InstalledPlugins.Any(p => p.InternalName == "Lifestream" && p.IsLoaded);
}
