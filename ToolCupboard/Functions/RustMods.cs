using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ToolCupboard.Functions
{
    public enum RustModFramework
    {
        None,
        Oxide,
        Carbon
    }

    public class RustPlugin
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool Enabled { get; set; }
        public bool HasConfig { get; set; }
        public DateTime Modified { get; set; }

        public string Status => Enabled ? "Enabled" : "Disabled";
        public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// Installs Oxide or Carbon on a Rust server and manages its plugin files.
    /// </summary>
    public static class RustMods
    {
        private const string OXIDE_URL = "https://github.com/OxideMod/Oxide.Rust/releases/latest/download/Oxide.Rust.zip";
        private const string CARBON_URL = "https://github.com/CarbonCommunity/Carbon/releases/download/production_build/Carbon.Windows.Release.zip";
        private const string DISABLED_FOLDER = "disabled";

        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string[] CarbonLoaderFiles = { "winhttp.dll", "doorstop_config.ini" };

        public static RustModFramework Detect(string serverId)
        {
            if (File.Exists(ServerPath.GetServersServerFiles(serverId, CarbonLoaderFiles[0])))
            {
                return RustModFramework.Carbon;
            }

            return File.Exists(ServerPath.GetServersServerFiles(serverId, "RustDedicated_Data", "Managed", "Oxide.Core.dll")) ? RustModFramework.Oxide : RustModFramework.None;
        }

        /// <summary>
        /// Installs or updates a framework. Returns null on success, otherwise the reason it failed.
        /// </summary>
        public static async Task<string> Install(string serverId, RustModFramework framework)
        {
            RustModFramework installed = Detect(serverId);
            if (installed != RustModFramework.None && installed != framework)
            {
                return $"{installed} is already installed. Remove it before installing {framework}.";
            }

            string basePath = ServerPath.GetServersServerFiles(serverId);
            string zipPath = Path.Combine(basePath, $"{framework}.download.zip");

            try
            {
                using (var response = await _httpClient.GetAsync(framework == RustModFramework.Oxide ? OXIDE_URL : CARBON_URL, HttpCompletionOption.ResponseHeadersRead))
                using (var file = File.Create(zipPath))
                {
                    response.EnsureSuccessStatusCode();
                    await response.Content.CopyToAsync(file);
                }

                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, basePath, true));
                return null;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                return e.Message;
            }
            finally
            {
                File.Delete(zipPath);
            }
        }

        /// <summary>
        /// Checks that the latest Oxide release is newer than a Rust build.
        /// </summary>
        public static async Task<bool> IsOxideCurrent(DateTimeOffset rustBuildTime)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, OXIDE_URL))
                using (var response = await _httpClient.SendAsync(request))
                {
                    return response.IsSuccessStatusCode && response.Content.Headers.LastModified >= rustBuildTime;
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Removes the Carbon loader. Plugins and configs stay in the carbon folder.
        /// </summary>
        public static void RemoveCarbon(string serverId)
        {
            foreach (string file in CarbonLoaderFiles)
            {
                File.Delete(ServerPath.GetServersServerFiles(serverId, file));
            }
        }

        /// <summary>
        /// Removes the Oxide assemblies. Oxide also replaces game files, so an Update Validate must follow.
        /// Plugins and configs stay in the oxide folder.
        /// </summary>
        public static void RemoveOxide(string serverId)
        {
            foreach (string file in Directory.GetFiles(ServerPath.GetServersServerFiles(serverId, "RustDedicated_Data", "Managed"), "Oxide.*.dll"))
            {
                File.Delete(file);
            }
        }

        public static string GetPluginsPath(string serverId, RustModFramework framework)
        {
            return ServerPath.GetServersServerFiles(serverId, framework == RustModFramework.Carbon ? "carbon" : "oxide", "plugins");
        }

        public static string GetConfigPath(string serverId, RustModFramework framework, string pluginName)
        {
            return framework == RustModFramework.Carbon
                ? ServerPath.GetServersServerFiles(serverId, "carbon", "configs", $"{pluginName}.json")
                : ServerPath.GetServersServerFiles(serverId, "oxide", "config", $"{pluginName}.json");
        }

        public static List<RustPlugin> GetPlugins(string serverId, RustModFramework framework)
        {
            string pluginsPath = GetPluginsPath(serverId, framework);
            var plugins = new List<RustPlugin>();

            void AddFrom(string path, bool enabled)
            {
                if (!Directory.Exists(path)) { return; }

                plugins.AddRange(Directory.GetFiles(path, "*.cs").Select(file => new RustPlugin
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    Enabled = enabled,
                    HasConfig = File.Exists(GetConfigPath(serverId, framework, Path.GetFileNameWithoutExtension(file))),
                    Modified = File.GetLastWriteTime(file)
                }));
            }

            AddFrom(pluginsPath, true);
            AddFrom(Path.Combine(pluginsPath, DISABLED_FOLDER), false);
            return plugins.OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void AddPlugin(string serverId, RustModFramework framework, string sourcePath)
        {
            string pluginsPath = GetPluginsPath(serverId, framework);
            Directory.CreateDirectory(pluginsPath);
            File.Copy(sourcePath, Path.Combine(pluginsPath, Path.GetFileName(sourcePath)), true);
        }

        public static void SetEnabled(string serverId, RustModFramework framework, RustPlugin plugin, bool enabled)
        {
            string pluginsPath = GetPluginsPath(serverId, framework);
            string folder = enabled ? pluginsPath : Path.Combine(pluginsPath, DISABLED_FOLDER);
            Directory.CreateDirectory(folder);
            File.Move(plugin.Path, Path.Combine(folder, Path.GetFileName(plugin.Path)), true);
        }

        public static string GetReloadCommand(RustModFramework framework, string pluginName)
        {
            return framework == RustModFramework.Carbon ? $"c.reload {pluginName}" : $"oxide.reload {pluginName}";
        }
    }
}
