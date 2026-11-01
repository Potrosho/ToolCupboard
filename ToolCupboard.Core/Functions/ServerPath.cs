using System.IO;

namespace ToolCupboard.Functions
{
    public static class ServerPath
    {
        public static class FolderName
        {
            public static string Bin = "bin";
            public static string Backups = "backups";
            public static string Installer = "installer";
            public static string Logs = "logs";
            public static string Servers = "servers";
            public static string Configs = "configs";
            public static string Cache = "cache";
            public static string Serverfiles = "serverfiles";
        }

        public static void CreateAndFixDirectories()
        {
            Directory.CreateDirectory(Get(FolderName.Backups));
            Directory.CreateDirectory(Get(FolderName.Bin));
            Directory.CreateDirectory(Get(FolderName.Configs));
            Directory.CreateDirectory(Get(FolderName.Logs));
            Directory.CreateDirectory(Get(FolderName.Servers));

            // Servers made before the rename keep their settings
            foreach (string server in Directory.GetDirectories(Get(FolderName.Servers)))
            {
                string oldConfig = Path.Combine(server, FolderName.Configs, "WindowsGSM.cfg");
                string config = Path.Combine(server, FolderName.Configs, "ToolCupboard.cfg");
                if (File.Exists(oldConfig) && !File.Exists(config))
                {
                    File.Move(oldConfig, config);
                }
            }
        }

        /// <summary>
        /// Get ToolCupboard.exe parent directory path. Example: "C://ToolCupboard/"
        /// </summary>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <param name="path3"></param>
        /// <returns></returns>
        public static string Get(string path1 = "", string path2 = "", string path3 = "")
        {
            return Path.Combine(AppInfo.RootPath, path1, path2, path3);
        }

        /// <summary>
        /// Get backup directory path with serverId. Example: "C://ToolCupboard/backups/1/"
        /// </summary>
        /// <param name="serverId"></param>
        /// <returns></returns>
        public static string GetBackups(string serverId)
        {
            var backupConfig = new BackupConfig(serverId);
            if (Directory.Exists(backupConfig.BackupLocation))
            {
                return backupConfig.BackupLocation;
            }

            string defaultBackupPath = Path.Combine(AppInfo.RootPath, FolderName.Backups, serverId);
            Directory.CreateDirectory(defaultBackupPath);
            return defaultBackupPath;
        }

        /// <summary>
        /// Get bin directory path. Example: "C://ToolCupboard/bin/"
        /// </summary>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <param name="path3"></param>
        /// <returns></returns>
        public static string GetBin(string path1 = "", string path2 = "", string path3 = "")
        {
            return Path.Combine(AppInfo.RootPath, FolderName.Bin, path1, path2, path3);
        }

        public static string GetInstaller(string path = "") // Unused
        {
            return Path.Combine(AppInfo.RootPath, FolderName.Installer, path);
        }

        /// <summary>
        /// Get log directory path. Example: "C://ToolCupboard/logs/"
        /// </summary>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <param name="path3"></param>
        /// <returns></returns>
        public static string GetLogs(string path1 = "", string path2 = "", string path3 = "")
        {
            return Path.Combine(AppInfo.RootPath, FolderName.Logs, path1, path2, path3);
        }

        /// <summary>
        /// Get servers directory path with serverId. Example: "C://ToolCupboard/servers/1/"
        /// </summary>
        /// <param name="serverId"></param>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <returns></returns>
        public static string GetServers(string serverId, string path1 = "", string path2 = "")
        {
            return Path.Combine(AppInfo.RootPath, FolderName.Servers, serverId, path1, path2);
        }

        /// <summary>
        /// Get servers configs directory path with serverId. Example: "C://ToolCupboard/servers/1/configs/"
        /// </summary>
        /// <param name="serverId"></param>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <returns></returns>
        public static string GetServersConfigs(string serverId, string path1 = "", string path2 = "")
        {
            return Path.Combine(GetServers(serverId), FolderName.Configs, path1, path2);
        }

        /// <summary>
        /// Get servers cache directory path with serverId. Example: "C://ToolCupboard/servers/1/cache/"
        /// </summary>
        /// <param name="serverId"></param>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <returns></returns>
        public static string GetServersCache(string serverId, string path1 = "", string path2 = "")
        {
            return Path.Combine(GetServers(serverId), FolderName.Cache, path1, path2);
        }

        /// <summary>
        /// Get serverfiles directory path with serverId. Example: "C://ToolCupboard/servers/1/serverfiles/"
        /// </summary>
        /// <param name="serverId"></param>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <param name="path3"></param>
        /// <returns></returns>
        public static string GetServersServerFiles(string serverId, string path1 = "", string path2 = "", string path3 = "")
        {
            return Path.Combine(GetServers(serverId), FolderName.Serverfiles, path1, path2, path3);
        }
    }
}
