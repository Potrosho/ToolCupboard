# ToolCupboard

ToolCupboard is a Windows desktop manager for Rust dedicated servers. It installs, imports, starts, stops, restarts, updates, and backs up servers from one window, and can be controlled remotely through Discord.

**Status: early release.** Core server management has been tested on a real Rust server. Discord features and backups of full-size servers have not been fully tested yet.

## Features
  * **Install or import** - Install a server through SteamCMD, or import an existing one.
  * **Multiple servers** - Each server gets its own files and its own game, RCON, and query ports.
  * **Automation** - Auto start, auto restart after crashes, auto update, and scheduled restarts.
  * **RCON** - Live console and chat, safe save and shutdown, and in-game restart warnings.
  * **Players** - See who is online, and kick or ban players.
  * **Backups** - Back up and restore server files.
  * **Oxide** - Install or update Oxide on a server.
  * **Discord** - Get alerts through a webhook and control servers with a bot.
  * **CPU controls** - Set process priority and affinity for each server.

## Default ports
Each new server takes the next free block of three ports.

| Port | Protocol | Default |
| ---- | -------- | ------- |
| Game | UDP | 28015 |
| RCON | TCP | 28016 |
| Query | UDP | 28017 |

## Building
Requires Windows and the .NET 10 SDK.

```bash
dotnet build ToolCupboard.sln
```

The app runs as administrator, so launching it shows a UAC prompt.

## Discord bot
Turn on **Message Content Intent** for your bot in the Discord Developer Portal. The bot cannot connect without it.

## Credits
ToolCupboard is based on [WindowsGSM](https://github.com/WindowsGSM/WindowsGSM) by TatLead and its contributors.

## License
MIT License. See [LICENSE](LICENSE).
