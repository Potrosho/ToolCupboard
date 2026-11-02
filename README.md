# ToolCupboard

ToolCupboard is a Windows manager for Rust dedicated servers. It installs, imports, starts, stops, restarts, updates, wipes, and backs up servers, and can be controlled remotely through Discord.

**Status: early release.** Server management, backups, and scheduled restarts, wipes, and update checks have been tested on a real Rust server with no players on it.

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
  * **Automation** - Auto start, auto restart after crashes, auto update, and scheduled restarts and wipes, with warnings in game chat.
  * **Wipes** - Run a map, blueprint, or full wipe by hand or on a schedule. Wiped files are kept, and bans are never touched.
  * **RCON** - Live console and chat, safe save and shutdown.
  * **Players** - See who is online, and kick or ban players.
  * **Web dashboard** - Everything from your browser: the console, players, wipes, backups, plugins, settings, and Discord. It only accepts connections from the same PC.
  * **Plugins** - Search uMod from the app or the dashboard, then install, disable, reload, or delete plugins on Oxide or Carbon.
  * **Backups** - Back up and restore server files.
  * **Oxide and Carbon** - Install, update, or remove a mod framework on a server.
  * **Discord** - Get alerts through a webhook and control servers with a bot.
  * **CPU controls** - Set process priority and affinity for each server.

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

To build both download exes into `dist\`:

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```

The version number is set once, in `Directory.Build.props`.

## Credits
ToolCupboard is based on [WindowsGSM](https://github.com/WindowsGSM/WindowsGSM) by TatLead and its contributors.

## License
MIT License. See [LICENSE](LICENSE).
