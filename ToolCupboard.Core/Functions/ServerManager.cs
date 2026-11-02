using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NCrontab;

namespace ToolCupboard.Functions
{
    public class ServerMetadata
    {

        public ServerStatus ServerStatus = ServerStatus.Stopped;
        public Process Process;
        public IntPtr MainWindow;
        public ServerConsole ServerConsole;
        public RustRcon Rcon;

        // Basic Game Server Settings
        public bool AutoRestart;
        public bool AutoStart;
        public bool AutoUpdate;
        public bool UpdateOnStart;
        public bool BackupOnStart;

        // Discord Alert Settings
        public bool DiscordAlert;
        public string DiscordMessage;
        public string DiscordWebhook;
        public bool AutoRestartAlert;
        public bool AutoStartAlert;
        public bool AutoUpdateAlert;
        public bool RestartCrontabAlert;
        public bool CrashAlert;

        // Restart Crontab Settings
        public bool RestartCrontab;
        public bool WipeCrontab;
        public string WipeCrontabFormat;
        public RustWipeType WipeType;
        public bool WipeNewSeed;
        public DateTime WipeLast;
        public bool ModUpdatePending;
        public string CrontabFormat;

        // Game Server Start Priority and Affinity
        public string CPUPriority;
        public string CPUAffinity;

        public bool EmbedConsole;
        public bool AutoScroll;
    }

    public enum ServerStatus
    {

        Started = 0,
        Starting = 1,
        Stopped = 2,
        Stopping = 3,
        Restarted = 4,
        Restarting = 5,
        Updated = 6,
        Updating = 7,
        Backuped = 8,
        Backuping = 9,
        Restored = 10,
        Restoring = 11,
        Deleting = 12
    }

    public enum WindowShowStyle : uint
    {

        Hide = 0,
        ShowNormal = 1,
        Show = 5,
        Minimize = 6,
        ShowMinNoActivate = 7
    }

    /// <summary>
    /// Runs the servers: starting, stopping, updating, backups, crash handling, schedules, and RCON.
    /// Everything runs on the context passed in, so state needs no locks.
    /// </summary>
    public partial class ServerManager
    {
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

        [DllImport("user32.dll")]
        private static extern int SetWindowText(IntPtr hWnd, string windowName);

        public readonly Dictionary<int, ServerMetadata> _serverMetadata = new Dictionary<int, ServerMetadata>();
        private readonly SynchronizationContext _context;

        /// <summary>Raised with each formatted log line.</summary>
        public event Action<string> Logged;

        /// <summary>Raised when a server's status or player count changes.</summary>
        public event Action<ServerTable> ServerChanged;

        /// <summary>Raised after a server's status changes.</summary>
        public event Action StatusChanged;

        /// <summary>Raised when a server is added or removed.</summary>
        public event Action TableChanged;

        public ServerManager(SynchronizationContext context)
        {
            _context = context;
            Bot = new DiscordBot.Bot(this);

            for (int i = 0; i <= AppInfo.MaxServers; i++)
            {
                _serverMetadata[i] = new ServerMetadata
                {
                    ServerStatus = ServerStatus.Stopped,
                    ServerConsole = new ServerConsole(i)
                };
            }

            ServerConsole.OutputReceived += (serverId, line) => _context.Post(_ => _serverMetadata[int.Parse(serverId)].ServerConsole.Add(line), null);
        }

        public ServerMetadata GetServerMetadata(object serverId) => _serverMetadata.TryGetValue(int.Parse(serverId.ToString()), out var metadata) ? metadata : null;

        public void Log(string serverId, string logText)
        {
            string title = int.TryParse(serverId, out int i) ? $"#{i}" : serverId;
            string log = $"[{DateTime.Now:MM/dd/yyyy-HH:mm:ss}][{title}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);
            File.AppendAllText(Path.Combine(logPath, $"L{DateTime.Now:yyyyMMdd}.log"), log);
            _recentLog.Add(log);
            if (_recentLog.Count > 200) { _recentLog.RemoveAt(0); }

            Logged?.Invoke(log);
        }

        const int UPDATE_INTERVAL_MINUTE = 30;

        public async Task GameServer_Start(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return; }

            string error = string.Empty;
            if (!string.IsNullOrWhiteSpace(server.IP) && !IsValidIPAddress(server.IP))
            {
                error += " IP address is not valid.";
            }

            if (!string.IsNullOrWhiteSpace(server.Port) && !IsValidPort(server.Port))
            {
                error += " Port number is not valid.";
            }

            if (!string.IsNullOrWhiteSpace(server.QueryPort) && !IsValidPort(server.QueryPort))
            {
                error += " Query port number is not valid.";
            }

            if (error != string.Empty)
            {
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR]" + error);

                return;
            }

            Process p = GetServerMetadata(server.ID).Process;
            if (p != null) { return; }

            if (GetServerMetadata(server.ID).BackupOnStart)
            {
                await GameServer_Backup(server, " | Backup on Start");
            }

            if (GetServerMetadata(server.ID).UpdateOnStart)
            {
                await GameServer_Update(server, " | Update on Start");
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
            Log(server.ID, "Action: Start" + notes);
            SetServerStatus(server, "Starting");

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Started");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        public async Task GameServer_Stop(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            //Begin stop
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
            Log(server.ID, "Action: Stop");
            SetServerStatus(server, "Stopping");

            bool stopGracefully = await Server_BeginStop(server, p);

            Log(server.ID, "Server: Stopped");
            if (!stopGracefully)
            {
                Log(server.ID, "[NOTICE] Server fail to stop gracefully");
            }
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");
        }

        public async Task GameServer_Restart(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started) { return; }

            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            _serverMetadata[int.Parse(server.ID)].Process = null;

            //Begin Restart
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
            Log(server.ID, "Action: Restart");
            SetServerStatus(server, "Restarting");

            await Server_BeginStop(server, p);

            await Task.Delay(1000);

            var gameServer = await Server_BeginStart(server);
            if (gameServer == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                SetServerStatus(server, "Stopped");
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
            Log(server.ID, "Server: Restarted");
            if (!string.IsNullOrWhiteSpace(gameServer.Notice))
            {
                Log(server.ID, "[Notice] " + gameServer.Notice);
            }
            SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());
        }

        public async Task<bool> GameServer_Update(ServerTable server, string notes = "", bool validate = false)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin Update
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
            Log(server.ID, "Action: Update" + notes);
            SetServerStatus(server, "Updating");

            var (p, remoteVersion, gameServer) = await Server_BeginUpdate(server, silenceCheck: validate, forceUpdate: true, validate: validate);

            if (p == null && string.IsNullOrEmpty(gameServer.Error)) // Update success (non-steamcmd server)
            {
                Log(server.ID, $"Server: Updated {(validate ? "Validate " : string.Empty)}({remoteVersion})");
                await UpdateModFramework(server);
            }
            else if (p != null) // p stores process of steamcmd
            {
                await Task.Run(() => { p.WaitForExit(); });
                if (p.ExitCode == 0)
                {
                    Log(server.ID, $"Server: Updated {(validate ? "Validate " : string.Empty)}({remoteVersion})");
                    await UpdateModFramework(server);
                }
                else
                {
                    Log(server.ID, "Server: Fail to update");
                    Log(server.ID, $"[ERROR] SteamCMD exit code: {p.ExitCode}");
                }
            }
            else
            {
                Log(server.ID, "Server: Fail to update");
                Log(server.ID, "[ERROR] " + gameServer.Error);
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            return true;
        }

        public async Task<bool> GameServer_Backup(ServerTable server, string notes = "")
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin backup
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Backuping;
            Log(server.ID, "Action: Backup" + notes);
            SetServerStatus(server, "Backuping");

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string backupLocation = ServerPath.GetBackups(server.ID);
            if (!Directory.Exists(backupLocation))
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, "[ERROR] Backup location not found");
                SetServerStatus(server, "Stopped");
                return false;
            }

            string zipFileName = $"ToolCupboard-Backup-Server-{server.ID}-";

            string startPath = ServerPath.GetServers(server.ID);
            string zipFile = Path.Combine(ServerPath.GetBackups(server.ID), $"{zipFileName}{DateTime.Now.ToString("yyyyMMddHHmmss")}.zip");

            string error = string.Empty;
            await Task.Run(() =>
            {
                string tempFile = zipFile + ".tmp";

                try
                {
                    ZipFile.CreateFromDirectory(startPath, tempFile);
                    File.Move(tempFile, zipFile);

                    // Remove the oldest Backup files once the new one is safe
                    var backupConfig = new BackupConfig(server.ID);
                    var oldBackups = new DirectoryInfo(backupLocation).GetFiles("*.zip")
                        .Where(x => x.Name.Contains(zipFileName))
                        .OrderByDescending(x => x.LastWriteTime)
                        .Skip(Math.Max(1, backupConfig.MaximumBackups));

                    foreach (var fi in oldBackups)
                    {
                        fi.Delete();
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                    File.Delete(tempFile);
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to backup");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");

                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Backuped");
            SetServerStatus(server, "Stopped");

            return true;
        }

        public async Task<bool> GameServer_RestoreBackup(ServerTable server, string backupFile)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            string backupLocation = ServerPath.GetBackups(server.ID);
            string backupPath = Path.Combine(backupLocation, backupFile);
            if (!File.Exists(backupPath))
            {
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, "[ERROR] Backup not found");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            Log(server.ID, "Action: Restore Backup");
            SetServerStatus(server, "Restoring");

            string extractPath = ServerPath.GetServers(server.ID);
            string restorePath = extractPath + ".restoring";

            string error = string.Empty;
            await Task.Run(() =>
            {
                try
                {
                    // Keep the current server until the whole backup is unpacked
                    if (Directory.Exists(restorePath)) { Directory.Delete(restorePath, true); }
                    ZipFile.ExtractToDirectory(backupPath, restorePath);
                    FolderSwap.Replace(extractPath, restorePath);
                }
                catch (Exception e)
                {
                    error = e.Message;
                    FolderSwap.DeleteIfPossible(restorePath);
                }
            });

            if (error != string.Empty)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to restore backup");
                Log(server.ID, $"[ERROR] {error}");
                SetServerStatus(server, "Stopped");
                return false;
            }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            Log(server.ID, "Server: Restored");
            SetServerStatus(server, "Stopped");

            return true;
        }

        public async Task<bool> GameServer_Delete(ServerTable server)
        {
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped)
            {
                return false;
            }

            //Begin delete
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Deleting;
            Log(server.ID, "Action: Delete");
            SetServerStatus(server, "Deleting");

            //Remove firewall rule
            var firewall = new WindowsFirewall(null, ServerPath.GetServers(server.ID));
            firewall.RemoveRuleEx();

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(1000);

            string serverPath = ServerPath.GetServers(server.ID);

            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(serverPath))
                    {
                        Directory.Delete(serverPath, true);
                    }
                }
                catch
                {

                }
            });

            await Task.Delay(1000);

            if (Directory.Exists(serverPath))
            {
                string appCfgPath = ServerPath.GetServersConfigs(server.ID, "ToolCupboard.cfg");
                if (File.Exists(appCfgPath))
                {
                    Log(server.ID, "Server: Fail to delete server");
                    Log(server.ID, "[ERROR] Directory is not accessible");

                    _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                    SetServerStatus(server, "Stopped");

                    return false;
                }
            }

            Log(server.ID, "Server: Deleted server");

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            ReloadServers();

            return true;
        }

        public async void OnGameServerExited(ServerTable server)
        {
            _context.Post(async _ =>
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                {
                    bool autoRestart = GetServerMetadata(serverId).AutoRestart;
                    _serverMetadata[int.Parse(server.ID)].ServerStatus = autoRestart ? ServerStatus.Restarting : ServerStatus.Stopped;
                    Log(server.ID, "Server: Crashed");
                    SetServerStatus(server, autoRestart ? "Restarting" : "Stopped");

                    if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).CrashAlert)
                    {
                        var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                        await webhook.Send(server.ID, server.Game, "Crashed", server.Name, server.IP, server.Port);
                    }

                    _serverMetadata[int.Parse(server.ID)].Process = null;

                    if (autoRestart)
                    {
                        if (GetServerMetadata(server.ID).BackupOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Backup(server, " | Backup on Start");
                        }

                        if (GetServerMetadata(server.ID).UpdateOnStart)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            await GameServer_Update(server, " | Update on Start");
                        }

                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null)
                        {
                            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                            return;
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Started | Auto Restart");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoRestartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Restarted | Auto Restart", server.Name, server.IP, server.Port);
                        }
                    }
                }
            }, null);
        }

        public async void StartAutoUpdateCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID));

            string localVersion = gameServer.GetLocalBuild();

            while (p != null && !p.HasExited)
            {
                await Task.Delay(60000 * UPDATE_INTERVAL_MINUTE);

                if (!GetServerMetadata(server.ID).AutoUpdate || GetServerMetadata(server.ID).ServerStatus == ServerStatus.Updating)
                {
                    continue;
                }

                if (p == null || p.HasExited) { break; }

                //Try to get local build again if not found just now
                if (string.IsNullOrWhiteSpace(localVersion))
                {
                    localVersion = gameServer.GetLocalBuild();
                }

                //Get remote build
                string remoteVersion = await gameServer.GetRemoteBuild();

                //Continue if success to get localVersion and remoteVersion
                if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion))
                {
                    if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started)
                    {
                        break;
                    }

                    Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");

                    if (localVersion != remoteVersion)
                    {
                        if (!await AnnounceRestart(server, p, "for an update")) { return; }

                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin stop
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopping;
                        SetServerStatus(server, "Stopping");

                        //Stop the server
                        await Server_BeginStop(server, p);

                        if (p != null && !p.HasExited)
                        {
                            p.Kill();
                        }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Updating;
                        SetServerStatus(server, "Updating");

                        //Update the server
                        Process updater = await gameServer.Update();
                        if (updater != null)
                        {
                            await Task.Run(() => updater.WaitForExit());
                            if (updater.ExitCode != 0)
                            {
                                gameServer.Error = $"SteamCMD exit code: {updater.ExitCode}";
                            }
                        }

                        if (string.IsNullOrWhiteSpace(gameServer.Error))
                        {
                            Log(server.ID, $"Server: Updated ({remoteVersion})");
                            await UpdateModFramework(server);

                            if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoUpdateAlert)
                            {
                                var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                                await webhook.Send(server.ID, server.Game, "Updated | Auto Update", server.Name, server.IP, server.Port);
                            }
                        }
                        else
                        {
                            Log(server.ID, "Server: Fail to update");
                            Log(server.ID, "[ERROR] " + gameServer.Error);
                        }

                        //Start the server
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Starting;
                        SetServerStatus(server, "Starting");

                        var gameServerStart = await Server_BeginStart(server);
                        if (gameServerStart == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        break;
                    }
                }
                else if (string.IsNullOrWhiteSpace(localVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get local build.");
                }
                else if (string.IsNullOrWhiteSpace(remoteVersion))
                {
                    Log(server.ID, $"[NOTICE] Fail to get remote build.");
                }
            }
        }

        public async void StartRestartCrontabCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                //If not enable return
                if (!GetServerMetadata(serverId).RestartCrontab)
                {
                    await Task.Delay(1000);

                    continue;
                }

                //Try get next DataTime restart
                DateTime? crontabTime = CrontabSchedule.TryParse(GetServerMetadata(serverId).CrontabFormat)?.GetNextOccurrence(DateTime.Now);

                //Delay 1 second for later compare
                await Task.Delay(1000);

                //Return if crontab expression is invalid
                if (crontabTime == null) { continue; }

                //If now >= crontab time
                if (DateTime.Compare(DateTime.Now, crontabTime ?? DateTime.Now) >= 0)
                {
                    ServerChanged?.Invoke(server);

                    if (p == null || p.HasExited)
                    {
                        break;
                    }

                    //Restart the server
                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        if (!await AnnounceRestart(server, p, "as scheduled")) { return; }

                        _serverMetadata[int.Parse(server.ID)].Process = null;

                        //Begin Restart
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restarting;
                        Log(server.ID, "Action: Restart");
                        SetServerStatus(server, "Restarting");

                        await Server_BeginStop(server, p);
                        var gameServer = await Server_BeginStart(server);
                        if (gameServer == null) { return; }

                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        Log(server.ID, "Server: Restarted | Restart Crontab");
                        if (!string.IsNullOrWhiteSpace(gameServer.Notice))
                        {
                            Log(server.ID, "[Notice] " + gameServer.Notice);
                        }
                        SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).RestartCrontabAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Restarted | Restart Crontab", server.Name, server.IP, server.Port);
                        }

                        break;
                    }
                }
            }
        }

        public bool IsWipeDue(string serverId)
        {
            ServerMetadata metadata = GetServerMetadata(serverId);
            if (!metadata.WipeCrontab) { return false; }

            DateTime? next = CrontabSchedule.TryParse(metadata.WipeCrontabFormat)?.GetNextOccurrence(metadata.WipeLast);
            return next != null && DateTime.Now >= next;
        }

        // Server_BeginStart runs a due wipe, so a wipe missed here still happens at the next start
        public async void StartWipeCrontabCheck(ServerTable server)
        {
            int serverId = int.Parse(server.ID);

            //Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            while (p != null && !p.HasExited)
            {
                await Task.Delay(1000);

                if (!IsWipeDue(server.ID) || p.HasExited || GetServerMetadata(server.ID).ServerStatus != ServerStatus.Started)
                {
                    continue;
                }

                if (!await AnnounceRestart(server, p, "for a wipe")) { return; }

                _serverMetadata[serverId].Process = null;

                _serverMetadata[serverId].ServerStatus = ServerStatus.Restarting;
                Log(server.ID, "Action: Scheduled Wipe");
                SetServerStatus(server, "Restarting");

                await Server_BeginStop(server, p);

                var gameServer = await Server_BeginStart(server);
                if (gameServer == null) { return; }

                _serverMetadata[serverId].ServerStatus = ServerStatus.Started;
                Log(server.ID, "Server: Restarted | Scheduled Wipe");
                SetServerStatus(server, "Started", ServerCache.GetPID(server.ID).ToString());

                break;
            }
        }

        public async Task<bool> WipeServerData(ServerTable server, RustWipeType type, bool newSeed)
        {
            try
            {
                string backupPath = await Task.Run(() => RustWipe.Wipe(server.ID, type));
                Log(server.ID, $"Wipe: {type} wipe complete. Wiped files were moved to {backupPath.Replace(AppInfo.RootPath, string.Empty)}");

                if (newSeed && type != RustWipeType.Blueprints)
                {
                    Log(server.ID, $"Wipe: New seed {RustWipe.SetRandomSeed(server.ID)}");
                }

                _serverMetadata[int.Parse(server.ID)].WipeLast = DateTime.Now;
                ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeLast, DateTime.Now.ToString("o"));
                return true;
            }
            catch (Exception e)
            {
                Log(server.ID, $"[ERROR] Wipe failed: {e.Message}");
                return false;
            }
        }

        // Keeps an RCON connection open while the server runs. The port opens late in the boot, so it retries.
        public async void StartRcon(ServerTable server, Process p)
        {
            int serverId = int.Parse(server.ID);

            while (p != null && !p.HasExited)
            {
                (int port, string password)? settings = null;
                RustRcon client = null;

                try
                {
                    settings = RustRcon.ReadSettings(server.ID);
                    if (settings != null)
                    {
                        client = new RustRcon("127.0.0.1", settings.Value.port, settings.Value.password);
                    }
                }
                catch (Exception e) when (e is IOException || e is FormatException || e is OverflowException)
                {
                    Log(server.ID, $"[NOTICE] RCON is unavailable: {e.Message}");
                    return;
                }

                if (client == null)
                {
                    Log(server.ID, "[NOTICE] RCON is unavailable: rcon.port or rcon.password is missing from server.cfg");
                    return;
                }

                using (RustRcon rcon = client)
                {
                    rcon.MessageReceived += text => _context.Post(_ => AddConsoleOutput(serverId, text), null);

                    if (await rcon.ConnectAsync())
                    {
                        _serverMetadata[serverId].Rcon = rcon;
                        Log(server.ID, "RCON: Connected");
                        await rcon.Completion;
                        _serverMetadata[serverId].Rcon = null;
                        Log(server.ID, "RCON: Disconnected");
                    }
                }

                await Task.Delay(5000);
            }
        }

        public void AddConsoleOutput(int serverId, string text)
        {
            foreach (string line in text.Split('\n'))
            {
                _serverMetadata[serverId].ServerConsole.Add(line.TrimEnd());
            }
        }

        // Warns players in chat before an automatic restart. Returns false if the server stopped during the countdown.
        public async Task<bool> AnnounceRestart(ServerTable server, Process p, string reason)
        {
            (int seconds, string label)[] warnings = { (240, "5 minutes"), (50, "1 minute"), (10, "10 seconds") };

            foreach (var (seconds, label) in warnings)
            {
                RustRcon rcon = GetServerMetadata(server.ID).Rcon;
                if (rcon == null || !rcon.IsConnected) { return true; }

                await rcon.SayAsync($"Server restarting {reason} in {label}");
                await Task.Delay(seconds * 1000);

                if (p.HasExited || GetServerMetadata(server.ID).Process != p) { return false; }
            }

            return true;
        }

        public async void StartQuery(ServerTable server)
        {
            if (string.IsNullOrWhiteSpace(server.IP) || string.IsNullOrWhiteSpace(server.QueryPort)) { return; }

            // Check the server support Query Method
            dynamic gameServer = GameServer.Data.Class.Get(server.Game);
            if (gameServer == null) { return; }
            if (gameServer.QueryMethod == null) { return; }

            // Save the process of game server
            Process p = GetServerMetadata(server.ID).Process;

            // Query server every 5 seconds
            while (p != null && !p.HasExited)
            {
                if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    break;
                }

                if (!IsValidIPAddress(server.IP) || !IsValidPort(server.QueryPort))
                {
                    return;
                }

                dynamic query = gameServer.QueryMethod;
                query.SetAddressPort(server.IP, int.Parse(server.QueryPort));
                string players = await query.GetPlayersAndMaxPlayers();

                if (players != null)
                {
                    server.Maxplayers = players;

                    ServerChanged?.Invoke(server);
                }

                await Task.Delay(5000);
            }
        }

        public async Task EndAllRunningProcess(string serverId)
        {
            await Task.Run(() =>
            {
                //LINQ query for windowsgsm old processes
                var processes = (from p in Process.GetProcesses()
                                 where ((Predicate<Process>)(p_ =>
                                 {
                                     try
                                     {
                                         return p_.MainModule.FileName.Contains(Path.Combine(AppInfo.RootPath, "servers", serverId) + "\\");
                                     }
                                     catch
                                     {
                                         return false;
                                     }
                                 }))(p)
                                 select p).ToList();

                // Kill all processes
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        //ignore
                    }
                }
            });
        }

        public void SetServerStatus(ServerTable server, string status, string pid = null)
        {
            server.Status = status;
            if (pid != null)
            {
                server.PID = pid;
            }
            if (status == "Stopped")
            {
                server.PID = string.Empty;
            }

            if (server.Status != "Started" && server.Maxplayers.Contains('/'))
            {
                var serverConfig = new ServerConfig(server.ID);
                server.Maxplayers = serverConfig.ServerMaxPlayer;
            }

            ServerChanged?.Invoke(server);
            StatusChanged?.Invoke();
        }

        public async void SendCommand(ServerTable server, string command)
        {
            Process p = GetServerMetadata(server.ID).Process;
            if (p == null) { return; }

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon != null && rcon.IsConnected)
            {
                int serverId = int.Parse(server.ID);
                AddConsoleOutput(serverId, $"> {command}");
                string reply = await rcon.SendAsync(command);
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    AddConsoleOutput(serverId, reply);
                }
                return;
            }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Input(p, command, GetServerMetadata(server.ID).MainWindow);
        }

        public static bool IsValidIPAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            string[] splitValues = ip.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            return splitValues.All(r => byte.TryParse(r, out byte tempForParsing));
        }

        public static bool IsValidPort(string port)
        {
            if (!int.TryParse(port, out int portnum))
            {
                return false;
            }

            return portnum > 1 && portnum < 65535;
        }

        public async Task<dynamic> Server_BeginStart(ServerTable server)
        {
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID));
            if (gameServer == null) { return null; }

            //End All Running Process
            await EndAllRunningProcess(server.ID);
            await Task.Delay(500);

            if (GetServerMetadata(server.ID).ModUpdatePending)
            {
                await UpdateModFramework(server);
            }

            if (IsWipeDue(server.ID) && !await WipeServerData(server, GetServerMetadata(server.ID).WipeType, GetServerMetadata(server.ID).WipeNewSeed))
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] The scheduled wipe failed, so the server was left stopped");
                SetServerStatus(server, "Stopped");

                return null;
            }

            //Add Start File to WindowsFirewall before start
            string startPath = ServerPath.GetServersServerFiles(server.ID, gameServer.StartPath);
            if (!string.IsNullOrWhiteSpace(gameServer.StartPath))
            {
                WindowsFirewall firewall = new WindowsFirewall(Path.GetFileName(startPath), startPath);
                if (!await firewall.IsRuleExist())
                {
                    await firewall.AddRule();
                }
            }

            gameServer.AllowsEmbedConsole = GetServerMetadata(server.ID).EmbedConsole;
            Process p = await gameServer.Start();

            //Fail to start
            if (p == null)
            {
                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] " + gameServer.Error);
                SetServerStatus(server, "Stopped");

                return null;
            }

            _serverMetadata[int.Parse(server.ID)].Process = p;
            p.Exited += (sender, e) => OnGameServerExited(server);

            await Task.Run(() =>
            {
                try
                {
                    if (!p.StartInfo.CreateNoWindow)
                    {
                        while (!p.HasExited && !ShowWindow(p.MainWindowHandle, WindowShowStyle.Minimize))
                        {
                            //Debug.WriteLine("Try Setting ShowMinNoActivate Console Window");
                        }

                        Debug.WriteLine("Set ShowMinNoActivate Console Window");

                        //Save MainWindow
                        _serverMetadata[int.Parse(server.ID)].MainWindow = p.MainWindowHandle;
                    }

                    //Fix for Factorio - The WaitForInputIdle never returns my guess is because Factorio is built
                    //  without a message loop seems to work fine with this commented out
                    //p.WaitForInputIdle();

                    if (!p.StartInfo.CreateNoWindow)
                    {
                        ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);
                    }
                }
                catch
                {
                    Debug.WriteLine("No Window require to hide");
                }
            });

            //An error may occur on ShowWindow if not adding this
            if (p == null || p.HasExited)
            {
                _serverMetadata[int.Parse(server.ID)].Process = null;

                _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
                Log(server.ID, "Server: Fail to start");
                Log(server.ID, "[ERROR] Exit Code: " + p.ExitCode.ToString());
                SetServerStatus(server, "Stopped");

                return null;
            }

            // Set Priority
            p = Functions.CPU.Priority.SetProcessWithPriority(p, Functions.CPU.Priority.GetPriorityInteger(GetServerMetadata(server.ID).CPUPriority));

            // Set Affinity
            try
            {
                p.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(GetServerMetadata(server.ID).CPUAffinity);
            }
            catch (Exception e)
            {
                Log(server.ID, $"[NOTICE] Fail to set affinity. ({e.Message})");
            }

            // Save Cache
            ServerCache.SavePID(server.ID, p.Id);
            ServerCache.SaveProcessName(server.ID, p.ProcessName);
            ServerCache.SaveWindowsIntPtr(server.ID, GetServerMetadata(server.ID).MainWindow);

            SetWindowText(p.MainWindowHandle, server.Name);

            ShowWindow(p.MainWindowHandle, WindowShowStyle.Hide);

            StartAutoUpdateCheck(server);

            StartRestartCrontabCheck(server);

            StartWipeCrontabCheck(server);


            StartQuery(server);

            StartRcon(server, p);

            return gameServer;
        }

        public async Task<bool> Server_BeginStop(ServerTable server, Process p)
        {
            _serverMetadata[int.Parse(server.ID)].Process = null;

            RustRcon rcon = GetServerMetadata(server.ID).Rcon;
            if (rcon == null || !rcon.IsConnected || !await rcon.SaveAndQuitAsync())
            {
                dynamic gameServer = GameServer.Data.Class.Get(server.Game);
                await gameServer.Stop(p);
            }

            // Saving a large map can take a while
            for (int i = 0; i < 60; i++)
            {
                if (p == null || p.HasExited) { break; }
                await Task.Delay(1000);
            }

            _serverMetadata[int.Parse(server.ID)].ServerConsole.Clear();

            // Save Cache
            ServerCache.SavePID(server.ID, -1);
            ServerCache.SaveProcessName(server.ID, string.Empty);
            ServerCache.SaveWindowsIntPtr(server.ID, (IntPtr)0);

            if (p != null && !p.HasExited)
            {
                p.Kill();
                return false;
            }

            return true;
        }

        public async Task<(Process, string, dynamic)> Server_BeginUpdate(ServerTable server, bool silenceCheck, bool forceUpdate, bool validate = false, string custum = null)
        {
            dynamic gameServer = GameServer.Data.Class.Get(server.Game, new ServerConfig(server.ID));

            string localVersion = gameServer.GetLocalBuild();
            if (string.IsNullOrWhiteSpace(localVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            string remoteVersion = await gameServer.GetRemoteBuild();
            if (string.IsNullOrWhiteSpace(remoteVersion) && !silenceCheck)
            {
                Log(server.ID, $"[NOTICE] {gameServer.Error}");
            }

            if (!silenceCheck)
            {
                Log(server.ID, $"Checking: Version ({localVersion}) => ({remoteVersion})");
            }

            if ((!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(remoteVersion) && localVersion != remoteVersion) || forceUpdate)
            {
                try
                {
                    return (await gameServer.Update(validate, custum), remoteVersion, gameServer);
                }
                catch
                {
                    return (await gameServer.Update(), remoteVersion, gameServer);
                }
            }

            return (null, remoteVersion, gameServer);
        }

        public async Task UpdateModFramework(ServerTable server)
        {
            RustModFramework framework = RustMods.Detect(server.ID);
            if (framework == RustModFramework.None) { return; }

            // The Oxide zip contains game files, so an older release would replace the new ones
            if (framework == RustModFramework.Oxide)
            {
                var steamCMD = new Installer.SteamCMD();
                await steamCMD.GetRemoteBuild(new GameServer.RUST(null).AppId);

                if (steamCMD.RemoteBuildTime == null || !await RustMods.IsOxideCurrent(steamCMD.RemoteBuildTime.Value))
                {
                    _serverMetadata[int.Parse(server.ID)].ModUpdatePending = true;
                    Log(server.ID, "[NOTICE] Oxide has no release for this Rust update yet. Plugins stay off until a restart after Oxide releases.");
                    return;
                }
            }

            _serverMetadata[int.Parse(server.ID)].ModUpdatePending = false;
            string error = await RustMods.Install(server.ID, framework);
            Log(server.ID, error == null ? $"Mods: {framework} updated" : $"[ERROR] Fail to update {framework}: {error}");
        }
    }
}