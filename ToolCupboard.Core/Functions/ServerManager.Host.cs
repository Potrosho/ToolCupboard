using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NCrontab;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// The server list, setup, and everything the dashboard and Discord ask for.
    /// </summary>
    public partial class ServerManager
    {
        private Process _installer;
        private bool g_WebJobRunning;
        private string g_WebJobKind = string.Empty;
        private string g_WebJobError;
        private readonly List<string> g_WebJobLog = new List<string>();
        private readonly SystemMetrics _metrics = new SystemMetrics();
        private bool _metricsLoaded;
        private readonly List<string> _recentLog = new List<string>();

        /// <summary>Every server, in id order.</summary>
        public List<ServerTable> Servers { get; } = new List<ServerTable>();

        public DiscordBot.Bot Bot { get; }
        public bool BotRunning { get; private set; }
        public bool BotBusy { get; private set; }

        /// <summary>True while a server is being installed or imported.</summary>
        public bool InstallBusy { get; private set; }

        /// <summary>Raised with each formatted Discord log line.</summary>
        public event Action<string> DiscordLogged;

        /// <summary>Raised when the Discord bot starts, stops, or is busy.</summary>
        public event Action BotChanged;

        /// <summary>Raised when an app option changes.</summary>
        public event Action OptionsChanged;

        public void ReloadServers()
        {
            LoadServers();
            TableChanged?.Invoke();
        }

        /// <summary>Runs work on the manager's context and waits for it.</summary>
        public Task<T> Run<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _context.Post(_ =>
            {
                try { result.SetResult(work()); }
                catch (Exception e) { result.SetException(e); }
            }, null);
            return result.Task;
        }

        public async Task<T> Run<T>(Func<Task<T>> work) => await await Run<Task<T>>(work);

        public async Task Run(Func<Task> work) => await await Run<Task>(work);

        // Finds servers still running from before the app last closed
        public void ReattachServers()
        {
            foreach (var server in Servers.ToList())
            {
                int pid = ServerCache.GetPID(server.ID);
                if (pid != -1)
                {
                    Process p = null;
                    try
                    {
                        p = Process.GetProcessById(pid);
                    }
                    catch
                    {
                        continue;
                    }

                    string pName = ServerCache.GetProcessName(server.ID);
                    if (!string.IsNullOrWhiteSpace(pName) && p.ProcessName == pName)
                    {
                        _serverMetadata[int.Parse(server.ID)].Process = p;
                        _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Started;
                        SetServerStatus(server, "Started");

                        _serverMetadata[int.Parse(server.ID)].MainWindow = ServerCache.GetWindowsIntPtr(server.ID);
                        p.Exited += (sender, e) => OnGameServerExited(server);

                        StartAutoUpdateCheck(server);
                        StartRestartCrontabCheck(server);
                        StartWipeCrontabCheck(server);
                        StartQuery(server);
                        StartRcon(server, p);
                    }
                }
            }
        }

        public async Task<string> InstallServer(string name, Action<string> onLine)
        {
            InstallBusy = true;
            try
            {
                return await RunInstall(name, onLine);
            }
            finally
            {
                InstallBusy = false;
            }
        }

        public async Task<string> ImportServer(string name, string sourcePath)
        {
            InstallBusy = true;
            try
            {
                return await RunImport(name, sourcePath);
            }
            finally
            {
                InstallBusy = false;
            }
        }

        private List<string> UsedPorts() => Servers.Select(server => server.Port).ToList();

        public async Task StartBot()
        {
            BotBusy = true;
            BotChanged?.Invoke();
            BotRunning = await Bot.Start();
            DiscordBotLog("Discord Bot " + (BotRunning ? "started." : "fail to start. Check the bot token and Message Content Intent."));
            BotBusy = false;
            BotChanged?.Invoke();
        }

        public async Task StopBot()
        {
            BotBusy = true;
            BotChanged?.Invoke();
            await Bot.Stop();
            BotRunning = false;
            DiscordBotLog("Discord Bot stopped.");
            BotBusy = false;
            BotChanged?.Invoke();
        }

        public async Task<object> GetWebSystem()
        {
            if (!_metricsLoaded)
            {
                await Task.Run(() =>
                {
                    _metrics.GetCPUStaticInfo();
                    _metrics.GetRAMStaticInfo();
                    _metrics.GetDiskStaticInfo();
                });
                _metricsLoaded = true;
            }

            double cpu = Math.Min(100, await Task.Run(() => _metrics.GetCPUUsage()));
            double ram = Math.Min(100, await Task.Run(() => _metrics.GetRAMUsage()));
            double disk = Math.Min(100, await Task.Run(() => _metrics.GetDiskUsage()));
            int started = GetStartedServerCount();

            object Row(string label, double percent, string detail) => new { label, percent = Math.Round(percent, 1), detail };

            return new
            {
                version = AppInfo.Version,
                usage = new[]
                {
                    Row("CPU", cpu, _metrics.CPUType),
                    Row("Memory", ram, SystemMetrics.GetMemoryRatioString(ram, _metrics.RAMTotalSize)),
                    Row("Disk", disk, SystemMetrics.GetDiskRatioString(disk, _metrics.DiskTotalSize)),
                    Row("Servers", Servers.Count * 100.0 / AppInfo.MaxServers, $"{Servers.Count}/{AppInfo.MaxServers}"),
                    Row("Running", Servers.Count == 0 ? 0 : started * 100.0 / Servers.Count, $"{started}/{Servers.Count}")
                },
                players = GetActivePlayers(),
                log = string.Concat(_recentLog),
                options = new Web.WebAppOptions { StartOnLogin = AppOptions.StartOnLogin, RestartOnCrash = AppOptions.RestartOnCrash }
            };
        }

        public bool SaveWebOptions(Web.WebAppOptions options)
        {
            bool startOnLogin = options?.StartOnLogin == true;
            if (startOnLogin != AppOptions.StartOnLogin) { AppOptions.SetStartOnLogin(startOnLogin); }
            AppOptions.RestartOnCrash = options?.RestartOnCrash == true;
            OptionsChanged?.Invoke();
            return true;
        }

        public Web.WebDiscord GetWebDiscord()
        {
            return new Web.WebDiscord
            {
                Running = BotRunning,
                Busy = BotBusy,
                HasToken = !string.IsNullOrWhiteSpace(DiscordBot.Configs.GetBotToken()),
                Prefix = DiscordBot.Configs.GetBotPrefix(),
                AutoStart = AppOptions.DiscordBotAutoStart,
                Commands = DiscordBot.Configs.GetCommandsList(),
                InviteLink = BotRunning ? Bot.GetInviteLink() : string.Empty,
                Admins = DiscordBot.Configs.GetBotAdminList().Select(admin => new Web.WebDiscordAdmin { Id = admin.Item1, Servers = admin.Item2 }).ToArray()
            };
        }

        public bool SetWebDiscordRunning(bool running)
        {
            if (BotBusy) { return false; }

            if (running != BotRunning) { _ = running ? StartBot() : StopBot(); }
            return true;
        }

        public void DiscordBotLog(string logText)
        {
            string log = $"[{DateTime.Now.ToString("MM/dd/yyyy-HH:mm:ss")}] {logText}" + Environment.NewLine;
            string logPath = ServerPath.GetLogs();
            Directory.CreateDirectory(logPath);

            string logFile = Path.Combine(logPath, $"L{DateTime.Now.ToString("yyyyMMdd")}-DiscordBot.log");
            File.AppendAllText(logFile, log);
            DiscordLogged?.Invoke(log);
        }

        public void LoadServers()
        {
            // Running servers hold on to their row, so a reload must update it rather than replace it
            Dictionary<string, ServerTable> known = Servers.ToDictionary(item => item.ID);
            Servers.Clear();

            //Add server to datagrid
            for (int i = 1; i <= AppInfo.MaxServers; i++)
            {
                string serverid_path = Path.Combine(AppInfo.RootPath, "servers", i.ToString());
                if (!Directory.Exists(serverid_path)) { continue; }

                string configpath = ServerPath.GetServersConfigs(i.ToString(), "ToolCupboard.cfg");
                if (!File.Exists(configpath)) { continue; }

                var serverConfig = new ServerConfig(i.ToString());

                //If Game server not exist return
                if (GameServer.Data.Class.Get(serverConfig.ServerGame) == null) { continue; }

                string status;
                switch (GetServerMetadata(i).ServerStatus)
                {
                    case ServerStatus.Started: status = "Started"; break;
                    case ServerStatus.Starting: status = "Starting"; break;
                    case ServerStatus.Stopped: status = "Stopped"; break;
                    case ServerStatus.Stopping: status = "Stopping"; break;
                    case ServerStatus.Restarted: status = "Restarted"; break;
                    case ServerStatus.Restarting: status = "Restarting"; break;
                    case ServerStatus.Updated: status = "Updated"; break;
                    case ServerStatus.Updating: status = "Updating"; break;
                    case ServerStatus.Backuped: status = "Backuped"; break;
                    case ServerStatus.Backuping: status = "Backuping"; break;
                    case ServerStatus.Restored: status = "Restored"; break;
                    case ServerStatus.Restoring: status = "Restoring"; break;
                    case ServerStatus.Deleting: status = "Deleting"; break;
                    default:
                        {
                            _serverMetadata[i].ServerStatus = ServerStatus.Stopped;
                            status = "Stopped";
                            break;
                        }
                }

                try
                {
                    string icon = "/ToolCupboard;component/Images/ToolCupboard.png";
                    string serverId = i.ToString();
                    string pidString = string.Empty;

                    try
                    {
                        pidString = Process.GetProcessById(ServerCache.GetPID(serverId)).Id.ToString();
                    }
                    catch { }

                    if (!known.TryGetValue(serverId, out ServerTable server))
                    {
                        server = new ServerTable { ID = serverId };
                    }

                    server.PID = pidString;
                    server.Game = serverConfig.ServerGame;
                    server.Icon = icon;
                    server.Status = status;
                    server.Framework = RustMods.Detect(serverId).ToString();
                    server.Name = serverConfig.ServerName;
                    server.IP = serverConfig.ServerIP;
                    server.Port = serverConfig.ServerPort;
                    server.QueryPort = serverConfig.ServerQueryPort;
                    server.Defaultmap = serverConfig.ServerMap;
                    if (GetServerMetadata(i).ServerStatus != ServerStatus.Started || server.Maxplayers == null)
                    {
                        server.Maxplayers = serverConfig.ServerMaxPlayer;
                    }

                    SaveServerConfigToServerMetadata(i, serverConfig);
                    Servers.Add(server);
                }
                catch
                {
                    // ignore
                }
            }
        }

        public void SaveServerConfigToServerMetadata(object serverId, ServerConfig serverConfig)
        {
            int i = int.Parse(serverId.ToString());

            // Basic Game Server Settings
            _serverMetadata[i].AutoRestart = serverConfig.AutoRestart;
            _serverMetadata[i].AutoStart = serverConfig.AutoStart;
            _serverMetadata[i].AutoUpdate = serverConfig.AutoUpdate;
            _serverMetadata[i].UpdateOnStart = serverConfig.UpdateOnStart;
            _serverMetadata[i].BackupOnStart = serverConfig.BackupOnStart;

            // Discord Alert Settings
            _serverMetadata[i].DiscordAlert = serverConfig.DiscordAlert;
            _serverMetadata[i].DiscordMessage = serverConfig.DiscordMessage;
            _serverMetadata[i].DiscordWebhook = serverConfig.DiscordWebhook;
            _serverMetadata[i].AutoRestartAlert = serverConfig.AutoRestartAlert;
            _serverMetadata[i].AutoStartAlert = serverConfig.AutoStartAlert;
            _serverMetadata[i].AutoUpdateAlert = serverConfig.AutoUpdateAlert;
            _serverMetadata[i].RestartCrontabAlert = serverConfig.RestartCrontabAlert;
            _serverMetadata[i].CrashAlert = serverConfig.CrashAlert;

            // Restart Crontab Settings
            _serverMetadata[i].RestartCrontab = serverConfig.RestartCrontab;
            _serverMetadata[i].WipeCrontab = serverConfig.WipeCrontab;
            _serverMetadata[i].WipeCrontabFormat = serverConfig.WipeCrontabFormat;
            _serverMetadata[i].WipeType = serverConfig.WipeType;
            _serverMetadata[i].WipeNewSeed = serverConfig.WipeNewSeed;
            _serverMetadata[i].WipeLast = serverConfig.WipeLast;
            _serverMetadata[i].CrontabFormat = serverConfig.CrontabFormat;

            // Game Server Start Priority and Affinity
            _serverMetadata[i].CPUPriority = serverConfig.CPUPriority;
            _serverMetadata[i].CPUAffinity = serverConfig.CPUAffinity;

            _serverMetadata[i].EmbedConsole = serverConfig.EmbedConsole;
            _serverMetadata[i].AutoScroll = serverConfig.AutoScroll;
            _serverMetadata[i].OpenRouterPorts = serverConfig.OpenRouterPorts;
        }

        // Opens or closes the ports right away when the server is running, otherwise on its next start or stop
        public void SetOpenRouterPorts(ServerTable server, bool open)
        {
            ServerMetadata metadata = GetServerMetadata(server.ID);
            if (metadata.OpenRouterPorts == open) { return; }

            metadata.OpenRouterPorts = open;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.OpenRouterPorts, open ? "1" : "0");
            if (metadata.ServerStatus == ServerStatus.Started)
            {
                _ = open ? OpenRouterPorts(server) : CloseRouterPorts(server);
            }
        }

        public async void AutoStartServer()
        {
            foreach (ServerTable server in Servers.ToList())
            {
                int serverId = int.Parse(server.ID);

                if (GetServerMetadata(serverId).AutoStart && GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped)
                {
                    await GameServer_Start(server, " | Auto Start");

                    if (GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started)
                    {
                        if (GetServerMetadata(serverId).DiscordAlert && GetServerMetadata(serverId).AutoStartAlert)
                        {
                            var webhook = new DiscordWebhook(GetServerMetadata(serverId).DiscordWebhook, GetServerMetadata(serverId).DiscordMessage);
                            await webhook.Send(server.ID, server.Game, "Started | Auto Start", server.Name, server.IP, server.Port);
                        }
                    }
                }
            }
        }

        // Shared by the install panel and the web UI. Returns null when installed, otherwise the reason it failed
        private async Task<string> RunInstall(string servername, Action<string> onLine)
        {
            if (_installer != null)
            {
                if (!_installer.HasExited) { _installer.Kill(); }
                _installer = null;
            }

            var newServerConfig = new ServerConfig(null);
            if (newServerConfig.ServerID == null) { return "The maximum number of servers is reached."; }

            string installPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(installPath))
            {
                try
                {
                    Directory.Delete(installPath, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return $"{installPath} is not accessible!";
                }
            }

            Directory.CreateDirectory(installPath);

            string servergame = GameServer.RUST.FullName;
            newServerConfig.CreateServerDirectory();

            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig);
            _installer = await gameServer.Install();

            if (_installer != null)
            {
                //Wait installer exit. Example: steamcmd.exe
                await Task.Run(() =>
                {
                    var reader = _installer.StandardOutput;
                    while (!reader.EndOfStream)
                    {
                        var nextLine = reader.ReadLine();
                        _context.Post(_ => onLine(nextLine), null);
                    }

                    _installer?.WaitForExit();
                });
            }

            if (!gameServer.IsInstallValid())
            {
                return _installer != null ? $"Fail to install [ERROR] Exit code: {_installer.ExitCode}" : $"Fail to install [ERROR] {gameServer.Error}";
            }

            newServerConfig.ServerIP = newServerConfig.GetIPAddress();
            newServerConfig.ServerPort = newServerConfig.GetAvailablePort((string)gameServer.Port, (int)gameServer.PortIncrements, UsedPorts());

            // Create ToolCupboard.cfg
            newServerConfig.SetData(servergame, servername, gameServer, UsedPorts());
            newServerConfig.CreateToolCupboardConfig();

            // Create ToolCupboard.cfg and game server config
            try
            {
                gameServer = GameServer.Data.Class.Get(servergame, newServerConfig);
                gameServer.CreateServerCFG();
            }
            catch
            {
                // ignore
            }

            ReloadServers();
            Log(newServerConfig.ServerID, "Install: Success");
            return null;
        }

        // Shared by the import panel and the web UI. Returns null when imported, otherwise the reason it failed
        private async Task<string> RunImport(string servername, string sourcePath)
        {
            if (!Directory.Exists(sourcePath)) { return "Server Dir is invalid"; }

            string servergame = GameServer.RUST.FullName;
            var newServerConfig = new ServerConfig(null);
            if (newServerConfig.ServerID == null) { return "The maximum number of servers is reached."; }

            dynamic gameServer = GameServer.Data.Class.Get(servergame, newServerConfig);
            if (!gameServer.IsImportValid(sourcePath)) { return gameServer.Error; }

            string importPath = ServerPath.GetServersServerFiles(newServerConfig.ServerID);
            if (Directory.Exists(importPath))
            {
                try
                {
                    Directory.Delete(importPath, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return $"{importPath} is not accessible!";
                }
            }

            string importLog = await Task.Run(() =>
            {
                try
                {
                    // Copied, because a move can lose files and fails across drives
                    Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(sourcePath, importPath);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            });

            if (importLog != null)
            {
                return $"Fail to copy the directory. {importLog}";
            }

            // Create ToolCupboard.cfg
            newServerConfig.SetData(servergame, servername, gameServer, UsedPorts());
            newServerConfig.CreateToolCupboardConfig();

            ReloadServers();
            Log(newServerConfig.ServerID, "Import: Success");
            return null;
        }

        public int GetStartedServerCount()
        {
            return Servers.Where(s => s.Status == "Started").Count();
        }

        public int GetActivePlayers()
        {
            return Servers.Where(s => s.Maxplayers != null && s.Maxplayers.Contains('/')).Sum(s => int.TryParse(s.Maxplayers.Split('/')[0], out int count) ? count : 0);
        }

        public async Task RemoveFramework(ServerTable server)
        {
            RustModFramework framework = RustMods.Detect(server.ID);
            if (framework == RustModFramework.Carbon)
            {
                RustMods.RemoveCarbon(server.ID);
            }
            else
            {
                RustMods.RemoveOxide(server.ID);
                await GameServer_Update(server, validate: true);
            }

            server.Framework = RustMods.Detect(server.ID).ToString();
            ServerChanged?.Invoke(server);
            Log(server.ID, $"Mods: {framework} removed");
        }

        // Returns null when saved, otherwise what is wrong with the values
        public string SaveServerSettings(ServerTable server, string worldSize, string seed, string tickRate, string saveInterval, string description, string url, string headerImage, bool rustPlus)
        {
            if (!IsNumberInRange(worldSize, 1000, 6000)) { return "World size must be between 1000 and 6000"; }
            if (!IsNumberInRange(seed, 0, int.MaxValue)) { return "Seed must be a positive number"; }
            if (!IsNumberInRange(tickRate, 10, 100)) { return "Tick rate must be between 10 and 100"; }
            if (!IsNumberInRange(saveInterval, 60, 3600)) { return "Save interval must be between 60 and 3600 seconds"; }

            RustConfig.Set(server.ID, "server.worldsize", worldSize);
            RustConfig.Set(server.ID, "server.seed", seed);
            RustConfig.Set(server.ID, "server.tickrate", tickRate);
            RustConfig.Set(server.ID, "server.saveinterval", saveInterval);
            RustConfig.Set(server.ID, "server.description", description ?? string.Empty);
            RustConfig.Set(server.ID, "server.url", url ?? string.Empty);
            RustConfig.Set(server.ID, "server.headerimage", headerImage ?? string.Empty);

            // The app port defaults to the highest server port + 67, so only the disabled state is stored
            if (!rustPlus || RustConfig.Get(server.ID, "app.port") == "-1")
            {
                RustConfig.Set(server.ID, "app.port", rustPlus ? (int.Parse(server.Port) + 68).ToString() : "-1");
            }

            Log(server.ID, "Settings: server.cfg updated");
            return null;
        }

        public static bool IsNumberInRange(string text, int min, int max)
        {
            return int.TryParse(text, out int value) && value >= min && value <= max;
        }

        public static string CleanServerName(string name)
        {
            return System.Text.RegularExpressions.Regex.Replace(name ?? string.Empty, "[\"\\r\\n]", "'").Trim();
        }

        public object GetWebJob()
        {
            return new { running = g_WebJobRunning, kind = g_WebJobKind, error = g_WebJobError, lines = g_WebJobLog.ToArray() };
        }

        // Returns null when the job started, otherwise the reason it was refused
        public string StartWebJob(string kind, string name, string path)
        {
            string serverName = CleanServerName(name);
            if (serverName.Length == 0 || serverName.Length > 100) { return "Enter a server name of 100 characters or fewer."; }
            if (Servers.Count >= AppInfo.MaxServers) { return "The maximum number of servers is reached."; }
            if (InstallBusy || g_WebJobRunning) { return "Another install or import is already running."; }
            if (kind == "import" && string.IsNullOrWhiteSpace(path)) { return "Enter the folder of the existing server."; }

            g_WebJobRunning = true;
            g_WebJobKind = kind;
            g_WebJobError = null;
            g_WebJobLog.Clear();
            RunWebJob(kind, serverName, path?.Trim());
            return null;
        }

        private async void RunWebJob(string kind, string name, string path)
        {
            try
            {
                g_WebJobError = kind == "import" ? await ImportServer(name, path) : await InstallServer(name, line =>
                {
                    g_WebJobLog.Add(line);
                    if (g_WebJobLog.Count > 300) { g_WebJobLog.RemoveAt(0); }
                });
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                g_WebJobError = e.Message;
            }

            g_WebJobRunning = false;
        }

        // Returns null when deleted, otherwise the reason it was refused
        public async Task<string> DeleteWebServer(string serverId, Web.WebConfirmed request)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }
            if (!Web.WebAuth.IsConfirmed(request?.Confirm, server.Name)) { return "The server name did not match."; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return "Stop the server before deleting it."; }

            return await GameServer_Delete(server) ? null : "The server could not be deleted. Check the log.";
        }

        public string GetWebConfigFile(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            string path = ServerPath.GetServersServerFiles(server.ID, "server.cfg");
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        // Returns null when saved, otherwise the reason it was refused
        public string SaveWebConfigFile(string serverId, string text)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return "Stop the server before editing server.cfg."; }
            if (text == null || text.Length > 100000) { return "The file is too large."; }

            RustConfig.ReplaceFile(ServerPath.GetServersServerFiles(server.ID, "server.cfg"), text);
            Log(server.ID, "Settings: server.cfg edited from the web UI");
            return null;
        }

        public Web.WebSettings GetWebSettings(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            ServerMetadata metadata = GetServerMetadata(server.ID);
            return new Web.WebSettings
            {
                AutoStart = metadata.AutoStart,
                AutoRestart = metadata.AutoRestart,
                AutoUpdate = metadata.AutoUpdate,
                UpdateOnStart = metadata.UpdateOnStart,
                BackupOnStart = metadata.BackupOnStart,
                RestartSchedule = metadata.RestartCrontab,
                RestartCrontab = metadata.CrontabFormat,
                WipeSchedule = metadata.WipeCrontab,
                WipeCrontab = metadata.WipeCrontabFormat,
                WipeType = metadata.WipeType.ToString(),
                WipeNewSeed = metadata.WipeNewSeed,
                NextWipe = CrontabSchedule.TryParse(metadata.WipeCrontabFormat)?.GetNextOccurrence(DateTime.Now).ToString("ddd, MM/dd/yyyy HH:mm"),
                MaximumBackups = new BackupConfig(server.ID).MaximumBackups,
                WorldSize = RustConfig.Get(server.ID, "server.worldsize"),
                Seed = RustConfig.Get(server.ID, "server.seed"),
                TickRate = RustConfig.Get(server.ID, "server.tickrate"),
                SaveInterval = RustConfig.Get(server.ID, "server.saveinterval"),
                Description = RustConfig.Get(server.ID, "server.description"),
                Url = RustConfig.Get(server.ID, "server.url"),
                HeaderImage = RustConfig.Get(server.ID, "server.headerimage"),
                RustPlus = RustConfig.Get(server.ID, "app.port") != "-1",
                Name = server.Name,
                Ip = server.IP,
                Port = server.Port,
                QueryPort = server.QueryPort,
                Map = server.Defaultmap,
                MaxPlayers = new ServerConfig(server.ID).ServerMaxPlayer,
                Parameters = new ServerConfig(server.ID).ServerParam,
                CpuPriority = Functions.CPU.Priority.GetPriorityInteger(metadata.CPUPriority),
                CpuAffinity = Functions.CPU.Affinity.GetAffinityValidatedString(metadata.CPUAffinity),
                DiscordAlert = metadata.DiscordAlert,
                DiscordMessage = metadata.DiscordMessage,
                HasWebhook = !string.IsNullOrWhiteSpace(metadata.DiscordWebhook),
                AutoStartAlert = metadata.AutoStartAlert,
                AutoRestartAlert = metadata.AutoRestartAlert,
                AutoUpdateAlert = metadata.AutoUpdateAlert,
                RestartCrontabAlert = metadata.RestartCrontabAlert,
                CrashAlert = metadata.CrashAlert,
                OpenRouterPorts = metadata.OpenRouterPorts,
                RouterStatus = metadata.RouterStatus
            };
        }

        // Returns null when saved, otherwise the reason it was refused
        public string SaveWebSettings(string serverId, Web.WebSettings settings)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }

            if (CrontabSchedule.TryParse(settings.RestartCrontab ?? string.Empty) == null) { return "The restart schedule is not valid."; }
            if (CrontabSchedule.TryParse(settings.WipeCrontab ?? string.Empty) == null) { return "The wipe schedule is not valid."; }
            if (!Enum.TryParse(settings.WipeType, out RustWipeType wipeType) || !Enum.IsDefined(typeof(RustWipeType), wipeType)) { return "The wipe type is not valid."; }
            if (settings.MaximumBackups < 1 || settings.MaximumBackups > 100) { return "Maximum backups must be between 1 and 100."; }

            string serverName = CleanServerName(settings.Name);
            if (serverName.Length == 0 || serverName.Length > 100) { return "Enter a server name of 100 characters or fewer."; }
            if (!ServerManager.IsValidIPAddress(settings.Ip)) { return "The IP address is not valid."; }
            if (!ServerManager.IsValidPort(settings.Port)) { return "The port is not valid."; }
            if (!ServerManager.IsValidPort(settings.QueryPort)) { return "The query port is not valid."; }
            if (!IsNumberInRange(settings.MaxPlayers, 1, 1000)) { return "Maximum players must be between 1 and 1000."; }
            if (settings.CpuPriority < 0 || settings.CpuPriority > 5) { return "The CPU priority is not valid."; }
            if (settings.CpuAffinity == null || settings.CpuAffinity.Length != Environment.ProcessorCount || settings.CpuAffinity.Any(c => c != '0' && c != '1') || !settings.CpuAffinity.Contains('1')) { return "Keep at least one CPU core selected."; }

            string newWebhook = settings.NewWebhook?.Trim();
            if (!string.IsNullOrEmpty(newWebhook) && !Web.WebAuth.IsDiscordWebhook(newWebhook)) { return "Enter a Discord webhook URL."; }

            bool identityChanged = serverName != server.Name || settings.Ip != server.IP || settings.Port != server.Port || settings.QueryPort != server.QueryPort;
            if (identityChanged && GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return "Stop the server before changing its name, address, or ports."; }

            string error = SaveServerSettings(server, settings.WorldSize, settings.Seed, settings.TickRate, settings.SaveInterval, settings.Description, settings.Url, settings.HeaderImage, settings.RustPlus);
            if (error != null) { return error; }

            ServerMetadata metadata = _serverMetadata[int.Parse(server.ID)];
            void SaveFlag(string name, bool value) => ServerConfig.SetSetting(server.ID, name, value ? "1" : "0");

            metadata.AutoStart = settings.AutoStart; SaveFlag(ServerConfig.SettingName.AutoStart, settings.AutoStart);
            metadata.AutoRestart = settings.AutoRestart; SaveFlag(ServerConfig.SettingName.AutoRestart, settings.AutoRestart);
            metadata.AutoUpdate = settings.AutoUpdate; SaveFlag(ServerConfig.SettingName.AutoUpdate, settings.AutoUpdate);
            metadata.UpdateOnStart = settings.UpdateOnStart; SaveFlag(ServerConfig.SettingName.UpdateOnStart, settings.UpdateOnStart);
            metadata.BackupOnStart = settings.BackupOnStart; SaveFlag(ServerConfig.SettingName.BackupOnStart, settings.BackupOnStart);
            metadata.RestartCrontab = settings.RestartSchedule; SaveFlag(ServerConfig.SettingName.RestartCrontab, settings.RestartSchedule);
            metadata.CrontabFormat = settings.RestartCrontab;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CrontabFormat, settings.RestartCrontab);

            // Saving other settings must not push back a wipe that is already due
            if (metadata.WipeCrontab != settings.WipeSchedule || metadata.WipeCrontabFormat != settings.WipeCrontab)
            {
                metadata.WipeLast = DateTime.Now;
                ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeLast, metadata.WipeLast.ToString("o"));
            }

            metadata.WipeCrontab = settings.WipeSchedule; SaveFlag(ServerConfig.SettingName.WipeCrontab, settings.WipeSchedule);
            metadata.WipeCrontabFormat = settings.WipeCrontab;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeCrontabFormat, settings.WipeCrontab);
            metadata.WipeType = wipeType;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.WipeType, wipeType.ToString());
            metadata.WipeNewSeed = settings.WipeNewSeed; SaveFlag(ServerConfig.SettingName.WipeNewSeed, settings.WipeNewSeed);
            BackupConfig.SetMaximumBackups(server.ID, settings.MaximumBackups);

            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerName, serverName);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerIP, settings.Ip);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerPort, settings.Port);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerQueryPort, settings.QueryPort);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerMap, (settings.Map ?? string.Empty).Replace("\"", "'"));
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerMaxPlayer, settings.MaxPlayers);
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.ServerParam, settings.Parameters ?? string.Empty);

            metadata.DiscordAlert = settings.DiscordAlert; SaveFlag(ServerConfig.SettingName.DiscordAlert, settings.DiscordAlert);
            metadata.DiscordMessage = (settings.DiscordMessage ?? string.Empty).Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordMessage, metadata.DiscordMessage);
            metadata.AutoStartAlert = settings.AutoStartAlert; SaveFlag(ServerConfig.SettingName.AutoStartAlert, settings.AutoStartAlert);
            metadata.AutoRestartAlert = settings.AutoRestartAlert; SaveFlag(ServerConfig.SettingName.AutoRestartAlert, settings.AutoRestartAlert);
            metadata.AutoUpdateAlert = settings.AutoUpdateAlert; SaveFlag(ServerConfig.SettingName.AutoUpdateAlert, settings.AutoUpdateAlert);
            metadata.RestartCrontabAlert = settings.RestartCrontabAlert; SaveFlag(ServerConfig.SettingName.RestartCrontabAlert, settings.RestartCrontabAlert);
            metadata.CrashAlert = settings.CrashAlert; SaveFlag(ServerConfig.SettingName.CrashAlert, settings.CrashAlert);
            SetOpenRouterPorts(server, settings.OpenRouterPorts);

            if (settings.ClearWebhook || !string.IsNullOrEmpty(newWebhook))
            {
                metadata.DiscordWebhook = settings.ClearWebhook ? string.Empty : newWebhook;
                ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.DiscordWebhook, metadata.DiscordWebhook);
            }

            metadata.CPUPriority = settings.CpuPriority.ToString();
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CPUPriority, metadata.CPUPriority);
            metadata.CPUAffinity = settings.CpuAffinity;
            ServerConfig.SetSetting(server.ID, ServerConfig.SettingName.CPUAffinity, metadata.CPUAffinity);
            if (metadata.Process != null && !metadata.Process.HasExited)
            {
                metadata.Process = Functions.CPU.Priority.SetProcessWithPriority(metadata.Process, settings.CpuPriority);
                metadata.Process.ProcessorAffinity = Functions.CPU.Affinity.GetAffinityIntPtr(metadata.CPUAffinity);
            }

            ReloadServers();

            StatusChanged?.Invoke();
            return null;
        }

        public object GetWebWipeInfo(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            return server == null ? null : new
            {
                map = RustWipe.GetFiles(server.ID, RustWipeType.Map).Count,
                blueprints = RustWipe.GetFiles(server.ID, RustWipeType.Blueprints).Count,
                full = RustWipe.GetFiles(server.ID, RustWipeType.Full).Count
            };
        }

        // Returns null when wiped, otherwise the reason it was refused
        public async Task<string> RunWebWipe(string serverId, Web.WebConfirmed request)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }
            if (!Web.WebAuth.IsConfirmed(request?.Confirm, server.Name)) { return "The server name did not match."; }
            if (!Enum.TryParse(request.Type, out RustWipeType type) || !Enum.IsDefined(typeof(RustWipeType), type)) { return "The wipe type is not valid."; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return "Stop the server before wiping it."; }

            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Restoring;
            SetServerStatus(server, "Wiping");
            bool wiped = await WipeServerData(server, type, request.NewSeed);
            _serverMetadata[int.Parse(server.ID)].ServerStatus = ServerStatus.Stopped;
            SetServerStatus(server, "Stopped");

            return wiped ? null : "The wipe failed, and the files were put back. Check the log.";
        }

        public object[] GetWebBackups(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            var location = new DirectoryInfo(new BackupConfig(server.ID).BackupLocation);
            if (!location.Exists) { return Array.Empty<object>(); }

            return location.GetFiles($"ToolCupboard-Backup-Server-{server.ID}-*.zip")
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => (object)new { file = file.Name, sizeMb = Math.Round(file.Length / 1048576.0, 1), created = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm") })
                .ToArray();
        }

        // Returns null when restored, otherwise the reason it was refused
        public async Task<string> RestoreWebBackup(string serverId, Web.WebConfirmed request)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }
            if (!Web.WebAuth.IsConfirmed(request?.Confirm, server.Name)) { return "The server name did not match."; }
            if (GetServerMetadata(server.ID).ServerStatus != ServerStatus.Stopped) { return "Stop the server before restoring a backup."; }

            // Only names from the backup list are accepted, so a path cannot be passed in
            string backup = new DirectoryInfo(new BackupConfig(server.ID).BackupLocation).GetFiles($"ToolCupboard-Backup-Server-{server.ID}-*.zip").FirstOrDefault(file => file.Name == request.File)?.Name;
            if (backup == null) { return "That backup does not exist."; }

            return await GameServer_RestoreBackup(server, backup) ? null : "The restore failed, and the server was left as it was. Check the log.";
        }

        // Returns null when added, otherwise the reason it was refused
        public string AddWebPlugin(string serverId, Web.WebPluginUpload upload)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }

            RustModFramework framework = RustMods.Detect(server.ID);
            if (framework == RustModFramework.None) { return "Install Oxide or Carbon first."; }

            string fileName = upload?.FileName ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !UmodCatalog.IsValidPluginName(name)) { return "Choose a .cs plugin file with a plain file name."; }
            if (string.IsNullOrEmpty(upload.Text) || upload.Text.Length > 5 * 1024 * 1024) { return "The plugin file is empty or too large."; }

            RustMods.WritePlugin(server.ID, framework, name, System.Text.Encoding.UTF8.GetBytes(upload.Text));
            Log(server.ID, $"Mods: Added plugin {name} from the web UI");
            return null;
        }

        public RustPlugin[] GetWebPlugins(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            RustModFramework framework = RustMods.Detect(server.ID);
            return framework == RustModFramework.None ? Array.Empty<RustPlugin>() : RustMods.GetPlugins(server.ID, framework).ToArray();
        }

        public async Task<string> InstallWebPlugin(string serverId, string slug)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return "That server does not exist."; }

            string error = await UmodCatalog.Install(server.ID, RustMods.Detect(server.ID), slug);
            Log(server.ID, error == null ? $"Mods: Installed {slug} from uMod" : $"[ERROR] Fail to install {slug}: {error}");
            return error;
        }

        // The name comes from the page, so it must match a plugin that is on disk
        public async Task<bool?> RunWebPluginAction(string serverId, string name, string action)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            RustModFramework framework = RustMods.Detect(server.ID);
            RustPlugin plugin = framework == RustModFramework.None ? null : RustMods.GetPlugins(server.ID, framework).FirstOrDefault(p => p.Name == name);
            if (plugin == null) { return null; }

            switch (action)
            {
                case "enable": RustMods.SetEnabled(server.ID, framework, plugin, true); break;
                case "disable": RustMods.SetEnabled(server.ID, framework, plugin, false); break;
                case "delete": File.Delete(plugin.Path); break;
                case "reload":
                    RustRcon rcon = GetServerMetadata(server.ID).Rcon;
                    if (rcon == null || !rcon.IsConnected) { return false; }
                    await rcon.SendAsync(RustMods.GetReloadCommand(framework, plugin.Name));
                    break;
                default: return null;
            }

            Log(server.ID, $"Mods: {action} plugin {plugin.Name} from the web UI");
            return true;
        }

        public async Task<bool> RunWebInstall(ServerTable server, RustModFramework framework)
        {
            string error = await RustMods.Install(server.ID, framework);
            Log(server.ID, error == null ? $"Mods: {framework} installed" : $"[ERROR] Fail to install {framework}: {error}");
            server.Framework = RustMods.Detect(server.ID).ToString();
            ServerChanged?.Invoke(server);
            return error == null;
        }

        // Returns null for an unknown server or action, and false if the server is in the wrong state
        public async Task<bool?> RunWebAction(string serverId, string action)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null) { return null; }

            ServerStatus status = GetServerMetadata(server.ID).ServerStatus;
            bool stopped = status == ServerStatus.Stopped;
            bool started = status == ServerStatus.Started;

            switch (action)
            {
                case "start": if (!stopped) { return false; } await GameServer_Start(server); return true;
                case "stop": if (!started) { return false; } await GameServer_Stop(server); return true;
                case "restart": if (!started) { return false; } await GameServer_Restart(server); return true;
                case "update": if (!stopped) { return false; } await GameServer_Update(server); return true;
                case "backup": if (!stopped) { return false; } await GameServer_Backup(server); return true;
                case "install-oxide": return stopped && await RunWebInstall(server, RustModFramework.Oxide);
                case "install-carbon": return stopped && await RunWebInstall(server, RustModFramework.Carbon);
                case "test-webhook":
                    ServerMetadata metadata = GetServerMetadata(server.ID);
                    if (!metadata.DiscordAlert || string.IsNullOrWhiteSpace(metadata.DiscordWebhook)) { return false; }
                    return await new DiscordWebhook(metadata.DiscordWebhook, metadata.DiscordMessage).Send(server.ID, server.Game, "Webhook Test Alert", server.Name, server.IP, server.Port);
                case "remove-framework":
                    if (!stopped || RustMods.Detect(server.ID) == RustModFramework.None) { return false; }
                    await RemoveFramework(server);
                    return true;
                default: return null;
            }
        }

        public object[] GetWebServers()
        {
            return Servers.Select(server => (object)new
            {
                id = server.ID,
                name = server.Name,
                status = server.Status,
                framework = RustMods.Detect(server.ID).ToString(),
                ip = server.IP,
                port = server.Port,
                map = server.Defaultmap,
                players = server.Maxplayers,
                uptime = server.Uptime
            }).ToArray();
        }

        public string[] GetWebConsole(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            return server == null ? null : GetServerMetadata(server.ID).ServerConsole.Get().Split(Environment.NewLine);
        }

        public bool SendWebCommand(string serverId, string command)
        {
            ServerTable server = GetServerTableById(serverId);
            if (server == null || GetServerMetadata(server.ID).Process == null) { return false; }

            SendCommand(server, command);
            return true;
        }

        public RustRcon GetWebRcon(string serverId)
        {
            ServerTable server = GetServerTableById(serverId);
            return server == null ? null : GetServerMetadata(server.ID).Rcon;
        }

        // Returns null when saved, otherwise the reason it was refused
        public string SaveWebDiscord(Web.WebDiscord discord)
        {
            string prefix = discord?.Prefix?.Trim() ?? string.Empty;
            if (prefix.Length > 10 || prefix.Any(char.IsWhiteSpace)) { return "The prefix must be 10 characters or fewer, with no spaces."; }

            var admins = (discord?.Admins ?? Array.Empty<Web.WebDiscordAdmin>()).Select(admin => (admin.Id?.Trim(), admin.Servers?.Replace(" ", string.Empty))).ToList();
            if (admins.Any(admin => !Web.WebAuth.IsValidDiscordAdmin(admin.Item1, admin.Item2))) { return "Each admin needs a Discord user ID and server IDs like 1,2 or 0 for all."; }

            string token = discord?.NewToken?.Trim();
            if (!string.IsNullOrEmpty(token) && token.Any(char.IsWhiteSpace)) { return "That bot token is not valid."; }

            DiscordBot.Configs.SetBotPrefix(prefix);
            if (!string.IsNullOrEmpty(token)) { DiscordBot.Configs.SetBotToken(token); }
            DiscordBot.Configs.SetBotAdminList(admins);
            AppOptions.DiscordBotAutoStart = discord?.AutoStart == true;
            OptionsChanged?.Invoke();
            return null;
        }

        public int GetServerCount()
        {
            return Servers.Count;
        }

        public List<(string, string, string)> GetServerList()
        {
            var list = new List<(string, string, string)>();

            for (int i = 0; i < Servers.Count; i++)
            {
                var server = Servers[i];
                list.Add((server.ID, server.Status, server.Name));
            }

            return list;
        }

        public bool IsServerExist(string serverId)
        {
            for (int i = 0; i < Servers.Count; i++)
            {
                var server = Servers[i];
                if (server.ID == serverId) { return true; }
            }

            return false;
        }

        public ServerStatus GetServerStatus(string serverId)
        {
            return GetServerMetadata(serverId).ServerStatus;
        }

        public string GetServerName(string serverId)
        {
            var server = GetServerTableById(serverId);
            return server?.Name ?? string.Empty;
        }

        public ServerTable GetServerTableById(string serverId)
        {
            for (int i = 0; i < Servers.Count; i++)
            {
                var server = Servers[i];
                if (server.ID == serverId) { return server; }
            }

            return null;
        }

        public async Task<bool> StartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive START action | {adminName} ({adminID})");
            await GameServer_Start(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> StopServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive STOP action | {adminName} ({adminID})");
            await GameServer_Stop(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> RestartServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive RESTART action | {adminName} ({adminID})");
            await GameServer_Restart(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Started;
        }

        public async Task<bool> SendCommandById(string serverId, string command, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive SEND action | {adminName} ({adminID}) | {command}");
            SendCommand(server, command);
            return true;
        }

        public async Task<bool> BackupServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive BACKUP action | {adminName} ({adminID})");
            await GameServer_Backup(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }

        public async Task<bool> UpdateServerById(string serverId, string adminID, string adminName)
        {
            var server = GetServerTableById(serverId);
            if (server == null) { return false; }

            DiscordBotLog($"Discord: Receive UPDATE action | {adminName} ({adminID})");
            await GameServer_Update(server);
            return GetServerMetadata(server.ID).ServerStatus == ServerStatus.Stopped;
        }
    }
}