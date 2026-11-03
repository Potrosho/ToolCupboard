using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// Players' Steam profile pictures for the live map, read from their public profile and kept in the cache folder.
    /// </summary>
    public static class SteamAvatars
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

        static string Folder => ServerPath.Get(ServerPath.FolderName.Cache, "avatars");

        // The avatar address in a profile's ?xml=1 page, only when it is on Steam's own image hosts
        public static string ParseAvatarUrl(string profileXml)
        {
            Match match = Regex.Match(profileXml ?? string.Empty, @"<avatarMedium><!\[CDATA\[(https://[^\]]+)\]\]></avatarMedium>");
            if (!match.Success || !Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out Uri uri)) { return null; }

            return uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase) ? uri.ToString() : null;
        }

        /// <summary>A data: URL of the avatar, or null when the profile is private or Steam cannot be reached.</summary>
        public static async Task<string> Get(string steamId)
        {
            if (!ulong.TryParse(steamId, out _)) { return null; }

            string path = Path.Combine(Folder, steamId + ".jpg");
            if (!File.Exists(path) || DateTime.Now - File.GetLastWriteTime(path) > KeepFor)
            {
                try
                {
                    string url = ParseAvatarUrl(await _httpClient.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1"));
                    if (url == null) { return null; }

                    byte[] image = await _httpClient.GetByteArrayAsync(url);
                    Directory.CreateDirectory(Folder);
                    File.WriteAllBytes(path, image);
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException)
                {
                    if (!File.Exists(path)) { return null; }
                }
            }

            return "data:image/jpeg;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }
    }
}
