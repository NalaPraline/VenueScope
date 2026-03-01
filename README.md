# VenueScope

Browse FFXIV community events from [Partake.gg](https://www.partake.gg/) and [FFXIV Venues](https://ffxivvenues.com/) directly in-game.

## Features

- Browse upcoming and live events filtered by data center
- Filter by time: All / Live Now / Today
- Filter by source: Partake · FFXIV Venue
- Filter by tags and search by title, location, or tag
- Click event titles to open them in your browser
- One-click in-game teleport to venue locations via [Lifestream](https://github.com/NightmareXIV/Lifestream)
- Toast notifications for new events on your data center
- Auto-refresh on a configurable interval (default: 5 min)

## Commands

| Command | Description |
|---------|-------------|
| `/venuescope` | Open VenueScope |
| `/vs` | Alias for `/venuescope` |

## Installation

### Via puni.sh (recommended)

1. Open `/xlsettings` → **Experimental** tab → **Custom Plugin Repositories**
2. Add the following URL and save:
   ```
   https://puni.sh/api/repository/nala
   ```
3. Open `/xlplugins`, search for **VenueScope** and install

### Via GitHub

1. Open `/xlsettings` → **Experimental** tab → **Custom Plugin Repositories**
2. Add the following URL and save:
   ```
   https://NalaPraline.github.io/VenueScope/pluginmaster.json
   ```
3. Open `/xlplugins`, search for **VenueScope** and install

## Building from Source

Requires .NET 9 SDK and XIVLauncher with Dalamud installed.

```bash
dotnet build --configuration Release VenueScope.sln
```

## License

[AGPL-3.0](LICENSE.md)
