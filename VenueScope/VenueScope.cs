using System;
using System.Collections;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
        Migrate();

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

    private static async void Migrate()
    {
        const string puniUrl = "https://puni.sh/api/repository/nala";
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        static FieldInfo?  FindField (Type? t, string n) { while (t != null) { var f = t.GetField (n, declared); if (f != null) return f; t = t.BaseType; } return null; }
        static MethodInfo? FindMethod(Type? t, string n) { while (t != null) { var m = t.GetMethod(n, declared); if (m != null) return m; t = t.BaseType; } return null; }

        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(puniUrl);
            if (json.Length < 100) return;

            var assembly    = typeof(IDalamudPlugin).Assembly;
            var serviceType = assembly.GetType("Dalamud.Service`1")!;

            // ── 1. Add puni.sh to ThirdRepoList ───────────────────────────────
            var configType = assembly.GetType("Dalamud.Configuration.Internal.DalamudConfiguration", true)!;
            var config     = serviceType.MakeGenericType(configType).GetMethod("Get")!.Invoke(null, null)!;
            var repoList   = (FindField(config.GetType(), "ThirdRepoList")?.GetValue(config)
                           ?? config.GetType().GetProperty("ThirdRepoList")?.GetValue(config)) as IList;

            if (repoList != null)
            {
                var exists  = false;
                object? old = null;
                foreach (var r in repoList)
                {
                    var url = FindField(r.GetType(), "Url")?.GetValue(r) as string
                           ?? r.GetType().GetProperty("Url")?.GetValue(r) as string;
                    if (url == puniUrl) exists = true;
                    if (url != null && url.Contains("NalaPraline") && url.Contains("github")) old = r;
                }

                if (old != null) repoList.Remove(old);

                if (!exists)
                {
                    var repoType = assembly.GetType("Dalamud.Configuration.ThirdPartyRepoSettings", true)!;
                    var newRepo  = Activator.CreateInstance(repoType)!;
                    FindField(repoType, "Url")?.SetValue(newRepo, puniUrl);
                    repoType.GetProperty("Url")?.SetValue(newRepo, puniUrl);
                    FindField(repoType, "IsEnabled")?.SetValue(newRepo, true);
                    repoType.GetProperty("IsEnabled")?.SetValue(newRepo, true);
                    repoList.Add(newRepo);
                }

                if (old != null || !exists)
                    FindMethod(config.GetType(), "Save")?.Invoke(config, null);
            }

            // ── 2. Update InstalledFromUrl for VenueScope ─────────────────────
            var managerType = assembly.GetType("Dalamud.Plugin.Internal.PluginManager", true)!;
            var manager     = serviceType.MakeGenericType(managerType).GetMethod("Get")!.Invoke(null, null)!;
            var plugins     = manager.GetType().GetProperty("InstalledPlugins")!.GetMethod!.Invoke(manager, null) as IList;
            if (plugins == null) return;

            foreach (var plugin in plugins)
            {
                var name  = plugin.GetType().GetProperty("InternalName")?.GetMethod?.Invoke(plugin, null) as string;
                var isDev = plugin.GetType().GetProperty("IsDev")?.GetMethod?.Invoke(plugin, null) as bool? ?? false;
                if (name != "VenueScope" || isDev) continue;

                var manifest = FindField(plugin.GetType(), "manifest")?.GetValue(plugin);
                if (manifest == null) { Log.Warning("[VenueScope] Migrate: manifest not found"); break; }

                var installUrlProp = manifest.GetType().GetProperty("InstalledFromUrl");
                var currentUrl     = installUrlProp?.GetValue(manifest) as string;
                Log.Information($"[VenueScope] Migrate: InstalledFromUrl = {currentUrl}");

                if (currentUrl != null && currentUrl.Contains("NalaPraline") && currentUrl.Contains("github"))
                {
                    installUrlProp!.SetValue(manifest, puniUrl);
                    FindMethod(plugin.GetType(), "SaveManifest")?.Invoke(plugin, ["Migrated to puni.sh"]);
                    Log.Information("[VenueScope] Migrate: done");
                }
                break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "[VenueScope] Migrate() failed");
        }
    }
}
