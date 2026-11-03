# ToolCupboard

ToolCupboard is a Windows manager for Rust dedicated servers. It installs, imports, starts, stops, restarts, updates, wipes, and backs up servers, and can be controlled remotely through Discord.

**Status: early release.** Server management, backups, and scheduled restarts, wipes, and update checks have been tested on a real Rust server, including with a player on it.

## Editions
Both editions do the same job and are built from the same code. Pick one.

| Edition | Download | What you get |
| ------- | -------- | ------------ |
| **Desktop** | `ToolCupboard.exe` | The Windows app, plus the web dashboard. |
| **Web** | `ToolCupboardWeb.exe` | A small tray program and the web dashboard, with no app window. |

## Install
Requires 64-bit Windows. You do not need to install .NET.

1. Download the edition you want from [Releases](https://github.com/Potrosho/ToolCupboard/releases) and put it in its own folder that you will keep. Your servers, backups, and settings are stored in that folder.
2. Run it and accept the administrator prompt. The exe is not code signed, so Windows may warn you first. Click **More info**, then **Run anyway**.

The Web edition opens the dashboard in your browser. Click its tray icon to open it again, or right-click the icon to exit. In the Desktop edition, click **Web UI**.

Closing either edition leaves your servers running. They are picked up again the next time you open it.

Only run one edition at a time, because both use port 27080 for the dashboard. **Start on login** only keeps the edition you turned it on from last.

## Features
  * **Install or import** - Install a server through SteamCMD, or import an existing one.
  * **Multiple servers** - Each server gets its own files and its own game, RCON, and query ports.
  * **Router ports** - Turn on **Open the game and query ports on my router** (**Router Ports** in the Desktop app) and ToolCupboard opens them through UPnP while the server runs, so friends outside your network can join. It never opens RCON or the dashboard. If your router has UPnP off, or your internet provider shares your IP (CGNAT), it tells you why in the log.
  * **Automation** - Auto start, auto restart after crashes, auto update, and scheduled restarts and wipes, with warnings in game chat.
  * **Wipes** - Run a map, blueprint, or full wipe by hand or on a schedule. Wiped files are kept, and bans are never touched.
  * **RCON** - Live console and chat, safe save and shutdown.
  * **Web dashboard** - Everything from your browser. It only accepts connections from the same PC. The features below marked *(dashboard)* are only in the dashboard, which both editions include.
  * **Live stats and charts** *(dashboard)* - FPS, entities, queue, network, and the server's own CPU and memory, with 7 days of charts.
  * **Console tools** *(dashboard)* - Error and warning filters, quick commands, broadcast, and Kill Process for a stuck shutdown.
  * **Players** *(dashboard)* - Everyone who has played, with past names, play time, and history. Kick, ban, timed chat mutes, staff roles, and giving items or saved loadouts.
  * **Activity and chat logs** *(dashboard)* - Joins, leaves, deaths, chat, kicks, bans, and mutes, searchable by player.
  * **Leaderboard** *(dashboard)* - Kills, deaths, K/D, and headshot percentage for any date range.
  * **F7 reports** *(dashboard)* - Player reports and feedback from the in-game F7 menu, with screenshots, status, and notes.
  * **Teams and clans** *(dashboard)* - Change leaders and roles, kick members, withdraw invites, and disband.
  * **Live map** *(dashboard)* - The real map with the grid, monuments, and players with their Steam avatars. Teleport players by clicking the map.
  * **Demos** *(dashboard)* - Record a player for a set time, always record chosen players, then pin, note, and download the demos.
  * **Files and notes** *(dashboard)* - Browse, edit, upload, and download server files, and keep Markdown notes.
  * **Plugins** - Search uMod from the app or the dashboard, then install, disable, reload, or delete plugins on Oxide or Carbon. In the dashboard you can also edit plugin configs and update plugins.
  * **Server settings** - Name, tags, game mode, PvE, radiation, idle kick, map, and more, plus the public or staging branch.
  * **Backups** - Back up and restore server files.
  * **Oxide and Carbon** - Install, update, or remove a mod framework on a server.
  * **Discord** - Get alerts through a webhook and control servers with a bot.
  * **CPU controls** - Set process priority and affinity for each server.
  * **Self-update** - **System** in the dashboard shows when a new version is out and installs it. Running servers keep going.

## The ToolCupboard server mod
Several dashboard features need information Rust only has inside the game, so ToolCupboard copies a small Harmony mod into each server's `HarmonyMods` folder before it starts. It uses Rust's built-in Harmony support, so it works without Oxide or Carbon, and the server is still listed as not modded. If a Rust update changes something the mod uses, only that one feature stops and a warning appears in the server log.

## Default ports
Each new server takes the next free block of three ports.

| Port | Protocol | Default |
| ---- | -------- | ------- |
| Game | UDP | 28015 |
| RCON | TCP | 28016 |
| Query | UDP | 28017 |

## Discord bot
Turn on **Message Content Intent** for your bot in the Discord Developer Portal. The bot cannot connect without it.

## Building
Requires Windows and the .NET 10 SDK.

```bash
dotnet build ToolCupboard.sln
```

| Project | What it is |
| ------- | ---------- |
| `ToolCupboard.Core` | Server management, the dashboard, and the Discord bot. |
| `ToolCupboard` | The Desktop edition. |
| `ToolCupboard.Web` | The Web edition. |
| `ToolCupboard.Tests` | Unit tests. |
| `ToolCupboard.Mod` | The server mod. It is built against a Rust server's own DLLs, so it is not part of the solution. Run `build-mod.ps1 -RustManaged <server>\RustDedicated_Data\Managed` after changing it, and commit the rebuilt `ToolCupboard.Core\Mod\ToolCupboard.Mod.dll`. |

To build both download exes into `dist\`:

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```

The version number is set once, in `Directory.Build.props`.

## Credits
ToolCupboard is based on [WindowsGSM](https://github.com/WindowsGSM/WindowsGSM) by TatLead and its contributors.

## License
MIT License. See [LICENSE](LICENSE).
