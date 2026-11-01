using System;
using System.Security.Cryptography;
using System.Text;

namespace ToolCupboard.Web
{
    /// <summary>
    /// Decides which requests may reach the local web API.
    /// </summary>
    public static class WebAuth
    {
        public static string CreateToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        // A public site can point its own domain at 127.0.0.1, so only local host names are accepted
        public static bool IsLocalHost(string host, int port)
        {
            return string.Equals(host, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase);
        }

        // Alerts carry server details, so they may only go to Discord
        public static bool IsDiscordWebhook(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && (uri.Host == "discord.com" || uri.Host == "discordapp.com")
                && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal);
        }

        // An admin is a Discord user id, and their servers are ids separated by commas, with 0 meaning all
        public static bool IsValidDiscordAdmin(string id, string servers)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(id ?? string.Empty, @"\A\d{5,25}\z")
                && System.Text.RegularExpressions.Regex.IsMatch(servers ?? string.Empty, @"\A\d+(,\d+)*\z");
        }

        public static bool IsConfirmed(string typed, string serverName)
        {
            return !string.IsNullOrEmpty(serverName) && typed == serverName;
        }

        public static bool IsValidToken(string token, string expected)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expected)) { return false; }

            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected));
        }
    }
}
