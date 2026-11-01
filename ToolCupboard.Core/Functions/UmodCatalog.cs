using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class UmodPlugin
    {
        public string Slug { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string Downloads { get; set; }
        public string Url { get; set; }
        public bool Unmaintained { get; set; }
        public bool Installed { get; set; }

        public string Notes => Installed ? "Installed" : Unmaintained ? "Unmaintained" : string.Empty;
    }

    public class UmodPage
    {
        public List<UmodPlugin> Items { get; set; }
        public int Page { get; set; }
        public int LastPage { get; set; }
    }

    /// <summary>
    /// Searches uMod's Rust plugins and installs them on a server.
    /// shortcut: uses the search.json feed behind uMod's site, which is not a promised API. Replace it if uMod retires the feed.
    /// </summary>
    public static class UmodCatalog
    {
        private const string SITE = "https://umod.org/plugins";
        private const int MAX_PLUGIN_BYTES = 5 * 1024 * 1024;

        private static readonly HttpClient _httpClient = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToolCupboard");
            return client;
        }

        public static UmodPage ParsePage(string json)
        {
            JObject page = JObject.Parse(json);
            return new UmodPage
            {
                Page = page.Value<int?>("current_page") ?? 1,
                LastPage = page.Value<int?>("last_page") ?? 1,
                Items = (page["data"] as JArray ?? new JArray()).OfType<JObject>().Select(ParsePlugin).ToList()
            };
        }

        private static UmodPlugin ParsePlugin(JObject plugin)
        {
            return new UmodPlugin
            {
                Slug = plugin.Value<string>("slug"),
                Name = plugin.Value<string>("name"),
                Title = plugin.Value<string>("title"),
                Description = plugin.Value<string>("description"),
                Version = plugin.Value<string>("latest_release_version"),
                Downloads = plugin.Value<string>("downloads_shortened"),
                Url = plugin.Value<string>("url"),
                Unmaintained = (plugin.Value<string>("tags_all") ?? string.Empty).Split(',').Contains("unmaintained")
            };
        }

        public static async Task<UmodPage> Search(string query, int page)
        {
            string url = $"{SITE}/search.json?query={Uri.EscapeDataString(query ?? string.Empty)}&page={Math.Max(1, page)}&sort=downloads&sortdir=desc&categories%5B%5D=rust";
            return ParsePage(await _httpClient.GetStringAsync(url));
        }

        public static bool IsValidSlug(string slug) => slug != null && Regex.IsMatch(slug, @"\A[a-z0-9]+(-[a-z0-9]+)*\z");

        // The name becomes a file name in the plugins folder
        public static bool IsValidPluginName(string name) => name != null && Regex.IsMatch(name, @"\A[A-Za-z0-9_]{1,100}\z");

        public static bool IsTrustedUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "umod.org";
        }

        public static bool MatchesChecksum(byte[] content, string checksum)
        {
            return string.Equals(Convert.ToHexString(SHA1.HashData(content)), checksum, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Downloads the latest release of a plugin. Returns null on success, otherwise the reason it failed.
        /// </summary>
        public static async Task<string> Install(string serverId, RustModFramework framework, string slug)
        {
            if (framework == RustModFramework.None) { return "Install Oxide or Carbon first."; }
            if (!IsValidSlug(slug)) { return "That plugin name is not valid."; }

            try
            {
                JObject plugin = JObject.Parse(await _httpClient.GetStringAsync($"{SITE}/{slug}.json"));
                string name = plugin.Value<string>("name");
                string downloadUrl = plugin.Value<string>("download_url");
                string checksum = plugin.Value<string>("latest_release_version_checksum");

                if (!IsValidPluginName(name) || !IsTrustedUrl(downloadUrl) || string.IsNullOrEmpty(checksum))
                {
                    return "uMod returned details that could not be trusted.";
                }

                byte[] content;
                using (var response = await _httpClient.GetAsync(downloadUrl))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > MAX_PLUGIN_BYTES) { return "The plugin file is too large."; }
                    content = await response.Content.ReadAsByteArrayAsync();
                }

                if (content.Length > MAX_PLUGIN_BYTES) { return "The plugin file is too large."; }
                if (!MatchesChecksum(content, checksum)) { return "The download did not match uMod's checksum, so it was not installed."; }

                RustMods.WritePlugin(serverId, framework, name, content);
                return null;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is JsonException || e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                return e.Message;
            }
        }
    }
}
