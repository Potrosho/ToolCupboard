using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class AppRelease
    {
        public string Current { get; set; }
        public string Latest { get; set; }
        public bool Newer { get; set; }
        public string Notes { get; set; }
        public string Url { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public bool CanInstall => Newer && DownloadUrl != null && Sha256 != null;
    }

    /// <summary>
    /// Updates the running exe from the latest GitHub release of ToolCupboard.
    /// </summary>
    public static class SelfUpdate
    {
        private const string REPO = "Potrosho/ToolCupboard";
        private const long MAX_EXE_BYTES = 300L * 1024 * 1024;

        /// <summary>Raised once the new exe is in place. Each edition closes itself; running servers are reattached on the next start.</summary>
        public static event Action ExitRequested;

        private static readonly HttpClient _httpClient = CreateClient();
        private static int _installing;

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToolCupboard");
            return client;
        }

        private static string ExePath => Environment.ProcessPath;

        // shortcut: the repo is fixed, so forks update from upstream releases. Make it a setting if forks ship their own.
        public static async Task<AppRelease> GetLatest()
        {
            string json = await _httpClient.GetStringAsync($"https://api.github.com/repos/{REPO}/releases/latest");
            return Parse(json, Path.GetFileName(ExePath), AppInfo.Version);
        }

        public static AppRelease Parse(string json, string exeName, string currentVersion)
        {
            JObject release = JObject.Parse(json);
            string latest = release.Value<string>("tag_name");
            JObject asset = (release["assets"] as JArray ?? new JArray()).OfType<JObject>()
                .FirstOrDefault(a => string.Equals(a.Value<string>("name"), exeName, StringComparison.OrdinalIgnoreCase));
            string downloadUrl = asset?.Value<string>("browser_download_url");
            string digest = asset?.Value<string>("digest");

            return new AppRelease
            {
                Current = currentVersion,
                Latest = latest,
                Newer = IsNewer(latest, currentVersion),
                Notes = release.Value<string>("body") ?? string.Empty,
                Url = release.Value<string>("html_url"),
                DownloadUrl = downloadUrl != null && downloadUrl.StartsWith($"https://github.com/{REPO}/releases/download/", StringComparison.Ordinal) ? downloadUrl : null,
                Sha256 = digest != null && digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest.Substring(7) : null
            };
        }

        public static bool IsNewer(string latest, string current)
        {
            return Version.TryParse(latest?.TrimStart('v'), out Version a) && Version.TryParse(current?.TrimStart('v'), out Version b) && a > b;
        }

        /// <summary>
        /// Downloads and checks the new exe, swaps it in, and restarts. Returns null when the restart is on its way, otherwise why it failed.
        /// </summary>
        public static async Task<string> Install()
        {
            if (System.Threading.Interlocked.Exchange(ref _installing, 1) == 1) { return "An update is already being installed."; }

            string error = await Replace();
            if (error != null) { _installing = 0; }
            return error;
        }

        private static async Task<string> Replace()
        {
            string exe = ExePath;
            string newPath = exe + ".new";
            try
            {
                AppRelease release = await GetLatest();
                if (!release.Newer) { return "ToolCupboard is already up to date."; }
                if (!release.CanInstall) { return $"Release {release.Latest} has no checked download for {Path.GetFileName(exe)}. Download it from GitHub instead."; }

                byte[] content;
                using (var response = await _httpClient.GetAsync(release.DownloadUrl))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > MAX_EXE_BYTES) { return "The download is too large."; }
                    content = await response.Content.ReadAsByteArrayAsync();
                }

                if (!string.Equals(Convert.ToHexString(SHA256.HashData(content)), release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return "The download did not match GitHub's checksum, so it was not installed.";
                }

                File.WriteAllBytes(newPath, content);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is Newtonsoft.Json.JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                try { if (File.Exists(newPath)) { File.Delete(newPath); } } catch (IOException) { }
                return e.Message;
            }

            // The running exe must stay in place: a single-file app reads its own code from it until it exits.
            // So the swap waits until this process is gone, which also frees the dashboard port. If it never exits, nothing changes.
            string id = Environment.ProcessId.ToString();
            string script = $"Wait-Process -Id {id} -Timeout 120 -ErrorAction SilentlyContinue; if (Get-Process -Id {id} -ErrorAction SilentlyContinue) {{ exit 1 }}; "
                + $"Move-Item -LiteralPath '{Quote(newPath)}' -Destination '{Quote(exe)}' -Force; Start-Process -FilePath '{Quote(exe)}'";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            _ = Task.Delay(1000).ContinueWith(_ => ExitRequested?.Invoke());
            return null;
        }

        private static string Quote(string path) => path.Replace("'", "''");

        // A download left behind when the swap could not happen
        public static void RemoveOldVersion()
        {
            try { File.Delete(ExePath + ".new"); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
