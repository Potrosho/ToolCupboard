using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// Reads and writes +convar lines in a server's server.cfg. The lines are passed to the server as start parameters.
    /// </summary>
    public static class RustConfig
    {
        private static Regex LineOf(string convar) => new Regex($@"^(\s*)\+{Regex.Escape(convar)}\s+.*?(\r?)$", RegexOptions.Multiline);

        private static string GetPath(string serverId) => ServerPath.GetServersServerFiles(serverId, "server.cfg");

        public static string Get(string serverId, string convar)
        {
            string path = GetPath(serverId);
            if (!File.Exists(path)) { return string.Empty; }

            Match match = Regex.Match(File.ReadAllText(path), $@"^\s*\+{Regex.Escape(convar)}\s+""?(.*?)""?\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public static void Set(string serverId, string convar, string value)
        {
            string path = GetPath(serverId);
            string config = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

            // Quotes and line breaks would break out of the start parameter
            string safeValue = Regex.Replace(value, @"[\r\n]+", " ").Replace("\"", "'");
            string line = $"+{convar} {(Regex.IsMatch(safeValue, @"^-?\d+$") ? safeValue : $"\"{safeValue}\"")}";

            Regex existing = LineOf(convar);
            config = existing.IsMatch(config)
                ? existing.Replace(config, match => $"{match.Groups[1].Value}{line}{match.Groups[2].Value}", 1)
                : $"{config.TrimEnd()}{Environment.NewLine}{line}{Environment.NewLine}".TrimStart();

            ReplaceFile(path, config);
        }

        // A crash while writing must not leave an empty config
        public static void ReplaceFile(string path, string text)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, text);
            File.Move(tempPath, path, true);
        }
    }
}
