using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace ToolCupboard.Functions
{
    public class ServerConfig
    {
        public static class SettingName
        {
            public const string ServerGame = "servergame";
            public const string ServerName = "servername";
            public const string ServerIP = "serverip";
            public const string ServerPort = "serverport";
            public const string ServerQueryPort = "serverqueryport";
            public const string ServerMap = "servermap";
            public const string ServerMaxPlayer = "servermaxplayer";
            public const string ServerGSLT = "servergslt";
            public const string ServerParam = "serverparam";
            public const string AutoRestart = "autorestart";
            public const string AutoStart = "autostart";
            public const string AutoUpdate = "autoupdate";
            public const string UpdateOnStart = "updateonstart";
            public const string BackupOnStart = "backuponstart";
            public const string DiscordAlert = "discordalert";
            public const string DiscordMessage = "discordmessage";
            public const string DiscordWebhook = "discordwebhook";
            public const string RestartCrontab = "restartcrontab";
            public const string CrontabFormat = "crontabformat";
            public const string EmbedConsole = "embedconsole";
            public const string AutoStartAlert = "autostartalert";
            public const string AutoRestartAlert = "autorestartalert";
            public const string AutoUpdateAlert = "autoupdatealert";
            public const string RestartCrontabAlert = "restartcrontabalert";
            public const string CrashAlert = "crashalert";
            public const string CPUPriority = "cpupriority";
            public const string CPUAffinity = "cpuaffinity";
            public const string AutoScroll = "autoscroll";
            public const string WipeCrontab = "wipecrontab";
            public const string WipeCrontabFormat = "wipecrontabformat";
            public const string WipeType = "wipetype";
            public const string WipeNewSeed = "wipenewseed";
            public const string WipeLast = "wipelast";
        }

        public string ServerID;
        public string ServerGame;
        public string ServerName;
        public string ServerIP;
        public string ServerPort;
        public string ServerQueryPort;
        public string ServerMap;
        public string ServerMaxPlayer;
        public string ServerGSLT;
        public string ServerParam;
        public bool AutoRestart;
        public bool AutoStart;
        public bool AutoUpdate;
        public bool UpdateOnStart;
        public bool BackupOnStart;
        public bool DiscordAlert;
        public string DiscordMessage;
        public string DiscordWebhook;
        public bool RestartCrontab;
        public string CrontabFormat;
        public bool EmbedConsole;
        public bool AutoStartAlert;
        public bool AutoRestartAlert;
        public bool AutoUpdateAlert;
        public bool RestartCrontabAlert;
        public bool CrashAlert;
        public string CPUPriority;
        public string CPUAffinity;
        public bool AutoScroll;
        public bool WipeCrontab;
        public string WipeCrontabFormat = RustWipe.DefaultSchedule;
        public RustWipeType WipeType = RustWipeType.Map;
        public bool WipeNewSeed = true;
        public DateTime WipeLast = DateTime.Now;

        public ServerConfig(string serverid)
        {
            //Get next available ServerID
            if (string.IsNullOrEmpty(serverid))
            {
                for (int id = 1; id <= AppInfo.MaxServers; id++)
                {
                    string serverid_dir = AppInfo.RootPath + @"\servers\" + id;
                    if (Directory.Exists(serverid_dir))
                    {
                        string config = AppInfo.RootPath + @"\servers\" + id + @"\configs\ToolCupboard.cfg";
                        if (!File.Exists(config))
                        {
                            ServerID = id.ToString();
                            break;
                        }
                    }
                    else
                    {
                        ServerID = id.ToString();
                        break;
                    }
                }

                return;
            }

            ServerID = serverid;

            //Get values from configpath
            string configpath = ServerPath.GetServersConfigs(serverid, "ToolCupboard.cfg");
            if (File.Exists(configpath))
            {
                foreach (string line in File.ReadLines(configpath))
                {
                    string[] keyvalue = line.Split(new[] {'='}, 2);
                    if (keyvalue.Length == 2 && keyvalue[1].Length >= 2)
                    {
                        keyvalue[1] = keyvalue[1].Substring(1, keyvalue[1].Length - 2);

                        switch (keyvalue[0])
                        {
                            case SettingName.ServerGame: ServerGame = keyvalue[1]; break;
                            case SettingName.ServerName: ServerName = keyvalue[1]; break;
                            case SettingName.ServerIP: ServerIP = keyvalue[1]; break;
                            case SettingName.ServerPort: ServerPort = keyvalue[1]; break;
                            case SettingName.ServerQueryPort: ServerQueryPort = keyvalue[1]; break;
                            case SettingName.ServerMap: ServerMap = keyvalue[1]; break;
                            case SettingName.ServerMaxPlayer: ServerMaxPlayer = keyvalue[1]; break;
                            case SettingName.ServerGSLT: ServerGSLT = keyvalue[1]; break;
                            case SettingName.ServerParam: ServerParam = keyvalue[1]; break;
                            case SettingName.AutoRestart: AutoRestart = keyvalue[1] == "1"; break;
                            case SettingName.AutoStart: AutoStart = keyvalue[1] == "1"; break;
                            case SettingName.AutoUpdate: AutoUpdate = keyvalue[1] == "1"; break;
                            case SettingName.UpdateOnStart: UpdateOnStart = keyvalue[1] == "1"; break;
                            case SettingName.BackupOnStart: BackupOnStart = keyvalue[1] == "1"; break;
                            case SettingName.DiscordAlert: DiscordAlert = keyvalue[1] == "1"; break;
                            case SettingName.DiscordMessage: DiscordMessage = keyvalue[1]; break;
                            case SettingName.DiscordWebhook: DiscordWebhook = keyvalue[1]; break;
                            case SettingName.RestartCrontab: RestartCrontab = keyvalue[1] == "1"; break;
                            case SettingName.CrontabFormat: CrontabFormat = keyvalue[1]; break;
                            case SettingName.EmbedConsole: EmbedConsole = keyvalue[1] == "1"; break;
                            case SettingName.AutoStartAlert: AutoStartAlert = keyvalue[1] == "1"; break;
                            case SettingName.AutoRestartAlert: AutoRestartAlert = keyvalue[1] == "1"; break;
                            case SettingName.AutoUpdateAlert: AutoUpdateAlert = keyvalue[1] == "1"; break;
                            case SettingName.RestartCrontabAlert: RestartCrontabAlert = keyvalue[1] == "1"; break;
                            case SettingName.CrashAlert: CrashAlert = keyvalue[1] == "1"; break;
                            case SettingName.CPUPriority: CPUPriority = keyvalue[1]; break;
                            case SettingName.CPUAffinity: CPUAffinity = keyvalue[1]; break;
                            case SettingName.AutoScroll: AutoScroll = keyvalue[1] == "1"; break;
                            case SettingName.WipeCrontab: WipeCrontab = keyvalue[1] == "1"; break;
                            case SettingName.WipeCrontabFormat: WipeCrontabFormat = keyvalue[1]; break;
                            case SettingName.WipeType: Enum.TryParse(keyvalue[1], out WipeType); break;
                            case SettingName.WipeNewSeed: WipeNewSeed = keyvalue[1] == "1"; break;
                            case SettingName.WipeLast: DateTime.TryParse(keyvalue[1], out WipeLast); break;
                        }
                    }
                }
            }
        }

        public void SetData(string serverGame, string serverName, dynamic gameServer, IEnumerable<string> usedPorts)
        {
            ServerGame = serverGame;
            ServerName = serverName;
            ServerIP = GetIPAddress();
            ServerPort = GetAvailablePort((string)gameServer.Port, (int)gameServer.PortIncrements, usedPorts);
            ServerQueryPort = (int.Parse(ServerPort) - int.Parse(gameServer.Port) + int.Parse(gameServer.QueryPort)).ToString(); // Magic
            ServerMap = gameServer.Defaultmap;
            ServerMaxPlayer = gameServer.Maxplayers;
            ServerGSLT = string.Empty;
            ServerParam = gameServer.Additional;
            EmbedConsole = false;

            AutoRestart = false;
            AutoStart = false;
            AutoUpdate = false;
            UpdateOnStart = false;
            BackupOnStart = false;
            DiscordAlert = false;
            DiscordMessage = string.Empty;
            DiscordWebhook = string.Empty;
            RestartCrontab = false;
            CrontabFormat = "0 6 * * *";
            AutoStartAlert = true;
            AutoRestartAlert = true;
            AutoUpdateAlert = true;
            RestartCrontabAlert = true;
            CrashAlert = true;
            CPUPriority = "2";
            CPUAffinity = string.Concat(System.Linq.Enumerable.Repeat("1", Environment.ProcessorCount));
            AutoScroll = true;
        }

        public bool CreateToolCupboardConfig()
        {
            CreateServerDirectory();

            string configpath = ServerPath.GetServersConfigs(ServerID, "ToolCupboard.cfg");
            if (!File.Exists(configpath))
            {
                File.Create(configpath).Dispose();

                using (TextWriter textwriter = new StreamWriter(configpath))
                {
                    textwriter.WriteLine($"{SettingName.ServerGame}=\"{ServerGame}\"");
                    textwriter.WriteLine($"{SettingName.ServerName}=\"{ServerName}\"");
                    textwriter.WriteLine($"{SettingName.ServerIP}=\"{ServerIP}\"");
                    textwriter.WriteLine($"{SettingName.ServerPort}=\"{ServerPort}\"");
                    textwriter.WriteLine($"{SettingName.ServerQueryPort}=\"{ServerQueryPort}\"");
                    textwriter.WriteLine($"{SettingName.ServerMap}=\"{ServerMap}\"");
                    textwriter.WriteLine($"{SettingName.ServerMaxPlayer}=\"{ServerMaxPlayer}\"");
                    textwriter.WriteLine($"{SettingName.ServerGSLT}=\"{ServerGSLT}\"");
                    textwriter.WriteLine($"{SettingName.ServerParam}=\"{ServerParam}\"");
                    textwriter.WriteLine(string.Empty);
                    textwriter.WriteLine($"{SettingName.CPUPriority}=\"{CPUPriority}\"");
                    textwriter.WriteLine($"{SettingName.CPUAffinity}=\"{CPUAffinity}\"");
                    textwriter.WriteLine(string.Empty);
                    textwriter.WriteLine($"{SettingName.AutoRestart}=\"0\"");
                    textwriter.WriteLine($"{SettingName.AutoStart}=\"0\"");
                    textwriter.WriteLine($"{SettingName.AutoUpdate}=\"0\"");
                    textwriter.WriteLine($"{SettingName.UpdateOnStart}=\"0\"");
                    textwriter.WriteLine($"{SettingName.BackupOnStart}=\"0\"");
                    textwriter.WriteLine(string.Empty);
                    textwriter.WriteLine($"{SettingName.DiscordAlert}=\"0\"");
                    textwriter.WriteLine($"{SettingName.DiscordMessage}=\"{DiscordMessage}\"");
                    textwriter.WriteLine($"{SettingName.DiscordWebhook}=\"{DiscordWebhook}\"");
                    textwriter.WriteLine(string.Empty);
                    textwriter.WriteLine($"{SettingName.RestartCrontab}=\"0\"");
                    textwriter.WriteLine($"{SettingName.CrontabFormat}=\"{CrontabFormat}\"");
                    textwriter.WriteLine(string.Empty);
                    textwriter.WriteLine($"{SettingName.EmbedConsole}=\"{(EmbedConsole ? "1" : "0")}\"");
                    textwriter.WriteLine($"{SettingName.AutoScroll}=\"{(AutoScroll ? "1" : "0")}\"");
                    textwriter.WriteLine(string.Empty);
                    textwriter.WriteLine($"{SettingName.AutoStartAlert}=\"1\"");
                    textwriter.WriteLine($"{SettingName.AutoRestartAlert}=\"1\"");
                    textwriter.WriteLine($"{SettingName.AutoUpdateAlert}=\"1\"");
                    textwriter.WriteLine($"{SettingName.RestartCrontabAlert}=\"1\"");
                    textwriter.WriteLine($"{SettingName.CrashAlert}=\"1\"");
                }

                return true;
            }

            return false;
        }

        public void CreateServerDirectory()
        {
            Directory.CreateDirectory(ServerPath.GetServers(ServerID));
            Directory.CreateDirectory(ServerPath.GetServersConfigs(ServerID));
            Directory.CreateDirectory(ServerPath.GetServersServerFiles(ServerID));
        }

        public bool DeleteServerDirectory()
        {
            string serverid_dir = ServerPath.GetServers(ServerID);
            if (Directory.Exists(serverid_dir) && ServerID != null && ServerID != string.Empty)
            {
                try
                {
                    Directory.Delete(serverid_dir, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public string GetIPAddress()
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint.Address.ToString();
            }
        }

        public string GetAvailablePort(string defaultport, int increment, IEnumerable<string> usedPorts)
        {
            int port = int.Parse(defaultport);
            foreach (int used in usedPorts.Select(p => int.TryParse(p, out int value) ? value : 0).OrderBy(p => p))
            {
                if (port == used || port == 27020) //SourceTV port 27020
                {
                    port += increment;
                }
            }

            return port.ToString();
        }

        public string GetRCONPassword()
        {
            // Letters and digits only, the password is part of the WebRcon url
            string allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
            char[] chars = new char[20];

            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = allowedChars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(allowedChars.Length)];
            }

            return new string(chars);
        }

        public static string GetSetting(string serverId, string settingName)
        {
            string configFile = ServerPath.GetServersConfigs(serverId, "ToolCupboard.cfg");

            if (File.Exists(configFile))
            {
                //Read the config lines
                string[] lines = File.ReadAllLines(configFile);

                //Read all lines
                foreach (string line in lines)
                {
                    string[] keyvalue = line.Split(new char[] { '=' }, 2);
                    if (keyvalue.Length == 2 && keyvalue[1].Length >= 2)
                    {
                        if (settingName == keyvalue[0])
                        {
                            return keyvalue[1].Substring(1, keyvalue[1].Length - 2);
                        }
                    }
                }
            }

            return string.Empty;
        }

        public static void SetSetting(string serverId, string settingName, string data)
        {
            string configFile = ServerPath.GetServersConfigs(serverId, "ToolCupboard.cfg");

            if (File.Exists(configFile))
            {
                bool saved = false;
                var output = new System.Text.StringBuilder();

                //Read the config lines
                foreach (string line in File.ReadAllLines(configFile))
                {
                    string[] keyvalue = line.Split(new[] { '=' }, 2);
                    if (keyvalue.Length == 2 && settingName == keyvalue[0])
                    {
                        output.AppendLine($"{settingName}=\"{data}\"");
                        saved = true;
                    }
                    else
                    {
                        output.AppendLine(line);
                    }
                }

                if (!saved)
                {
                    output.AppendLine($"{settingName}=\"{data}\"");
                }

                RustConfig.ReplaceFile(configFile, output.ToString());
            }
        }
    }
}
