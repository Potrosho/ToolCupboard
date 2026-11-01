using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ToolCupboard.Functions
{
    public enum RustWipeType
    {
        Map,
        Blueprints,
        Full
    }

    /// <summary>
    /// Wipes the data of a stopped Rust server. Files are moved to a backup folder instead of being deleted.
    /// </summary>
    public static class RustWipe
    {
        public const string DefaultSchedule = "0 19 1-7 * 4"; // First Thursday of the month
        private const string DEFAULT_IDENTITY = "my_server_identity";
        private const string BACKUP_FOLDER = "wipe-backups";
        private const int BACKUPS_KEPT = 3;

        private static readonly string[] MapPatterns = { "*.map", "*.sav", "*.sav.*", "*.navmesh", "*_occlusion_*.dat" };
        private static readonly string[] BlueprintPatterns = { "player.blueprints.*" };
        private static readonly string[] PlayerPatterns = { "player.deaths.*", "player.identities.*", "player.states.*", "sv.files.*", "relationship.*", "clans.*" };

        public static string GetDataPath(string serverId)
        {
            string identity = RustConfig.Get(serverId, "server.identity");
            return Path.Combine(ServerPath.GetServersServerFiles(serverId, "server"), identity.Length > 0 ? identity : DEFAULT_IDENTITY);
        }

        public static List<string> GetFiles(string serverId, RustWipeType type)
        {
            string dataPath = GetDataPath(serverId);
            if (!Directory.Exists(dataPath)) { return new List<string>(); }

            IEnumerable<string> patterns = Array.Empty<string>();
            if (type != RustWipeType.Blueprints) { patterns = patterns.Concat(MapPatterns); }
            if (type != RustWipeType.Map) { patterns = patterns.Concat(BlueprintPatterns); }
            if (type == RustWipeType.Full) { patterns = patterns.Concat(PlayerPatterns); }

            return patterns.SelectMany(pattern => Directory.GetFiles(dataPath, pattern, SearchOption.TopDirectoryOnly)).Distinct().ToList();
        }

        /// <summary>
        /// Moves the wiped files to a new backup folder and returns its path.
        /// </summary>
        public static string Wipe(string serverId, RustWipeType type)
        {
            string backupPath = Path.Combine(ServerPath.GetServers(serverId), BACKUP_FOLDER, $"{DateTime.Now:yyyyMMdd-HHmmss}-{type}");
            Directory.CreateDirectory(backupPath);

            var moved = new List<string>();
            try
            {
                foreach (string file in GetFiles(serverId, type))
                {
                    File.Move(file, Path.Combine(backupPath, Path.GetFileName(file)));
                    moved.Add(file);
                }
            }
            catch
            {
                // Never leave a half wiped server
                foreach (string file in moved)
                {
                    File.Move(Path.Combine(backupPath, Path.GetFileName(file)), file);
                }

                Directory.Delete(backupPath);
                throw;
            }

            PruneBackups(serverId);
            return backupPath;
        }

        public static int SetRandomSeed(string serverId)
        {
            int seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            RustConfig.Set(serverId, "server.seed", seed.ToString());
            return seed;
        }

        private static void PruneBackups(string serverId)
        {
            var backups = new DirectoryInfo(Path.Combine(ServerPath.GetServers(serverId), BACKUP_FOLDER)).GetDirectories()
                .OrderByDescending(directory => directory.Name)
                .Skip(BACKUPS_KEPT);

            foreach (DirectoryInfo backup in backups)
            {
                backup.Delete(true);
            }
        }
    }
}
