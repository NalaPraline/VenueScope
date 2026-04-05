using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace VenueScope.Services;

public class LifestreamIPC : IDisposable
{
    private readonly ICallGateSubscriber<string, object?>                    _executeCommand;
    private readonly ICallGateSubscriber<string, string, int>                _changeCharacter;
    private readonly ICallGateSubscriber<string, string, string, bool, bool> _connectAndTravel;
    private readonly ICallGateSubscriber<string, string, bool>               _connectAndLogin;
    private readonly ICallGateSubscriber<bool>                               _isBusy;
    private readonly ICallGateSubscriber<int>                                _logout;

    public LifestreamIPC(IDalamudPluginInterface pi)
    {
        _executeCommand   = pi.GetIpcSubscriber<string, object?>("Lifestream.ExecuteCommand");
        _changeCharacter  = pi.GetIpcSubscriber<string, string, int>("Lifestream.ChangeCharacter");
        _connectAndTravel = pi.GetIpcSubscriber<string, string, string, bool, bool>("Lifestream.ConnectAndTravel");
        _connectAndLogin  = pi.GetIpcSubscriber<string, string, bool>("Lifestream.ConnectAndLogin");
        _isBusy           = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        _logout           = pi.GetIpcSubscriber<int>("Lifestream.Logout");
    }

    public bool IsBusy()
    {
        try   { return _isBusy.InvokeFunc(); }
        catch { return false; }
    }

    // Returns 0 (Success) or a non-zero ErrorCode. Only works from in-game.
    public int Logout()
    {
        try   { return _logout.InvokeFunc(); }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[LifestreamIPC] Logout failed: {e.Message}");
            return -1;
        }
    }

    public void ExecuteCommand(string args)
    {
        try   { _executeCommand.InvokeAction(args); }
        catch (Exception e) { Plugin.Log.Warning($"[LifestreamIPC] ExecuteCommand failed: {e.Message}"); }
    }

    // From in-game: logs out current character, navigates to charHomeWorld's DC chara select,
    // initiates DC travel to destination world, then logs in. Returns ErrorCode as int (0 = Success).
    public bool ChangeCharacter(string name, string world)
    {
        try
        {
            int errorCode = _changeCharacter.InvokeFunc(name, world);
            if (errorCode != 0)
                Plugin.Log.Warning($"[LifestreamIPC] ChangeCharacter returned error code {errorCode}");
            return errorCode == 0;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[LifestreamIPC] ChangeCharacter failed: {e.Message}");
            return false;
        }
    }

    // From title screen: navigate to charHomeWorld's DC chara select, travel to destination world,
    // then login (noLogin=false) or just travel without logging in (noLogin=true).
    public bool ConnectAndTravel(string charName, string charHomeWorld, string destination, bool noLogin)
    {
        try   { return _connectAndTravel.InvokeFunc(charName, charHomeWorld, destination, noLogin); }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[LifestreamIPC] ConnectAndTravel failed: {e.Message}");
            return false;
        }
    }

    // From title screen: navigate to charHomeWorld's DC chara select and log in directly.
    // After login, ExecuteCommand handles world travel + house TP via PendingVenueCode.
    public bool ConnectAndLogin(string charName, string charHomeWorld)
    {
        try   { return _connectAndLogin.InvokeFunc(charName, charHomeWorld); }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[LifestreamIPC] ConnectAndLogin failed: {e.Message}");
            return false;
        }
    }

    public void Dispose() { }
}
