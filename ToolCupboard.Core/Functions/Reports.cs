using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class ServerReport
    {
        public string Id { get; set; }
        public DateTime Time { get; set; }
        public string Type { get; set; }
        public string ReporterId { get; set; }
        public string ReporterName { get; set; }
        public string TargetId { get; set; }
        public string TargetName { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
        public bool HasScreenshot { get; set; }
        public string Status { get; set; } = "open";
        public string Note { get; set; } = string.Empty;

        // From the ToolCupboard mod for player reports: metres apart, and whether they shared a team
        public double? Distance { get; set; }
        public bool? SameTeam { get; set; }

        public bool IsPlayerReport => !string.IsNullOrEmpty(TargetId);
    }

    /// <summary>
    /// F7 reports, one file per report so its status and note can change.
    /// Rust posts them to ToolCupboard when server.reportsserverendpoint points here, which is the only way it sends screenshots.
    /// </summary>
    public static class Reports
    {
        public static readonly string[] Statuses = { "open", "looking", "resolved", "dismissed" };

        // Feedback types from Facepunch.Models.ReportType, and the player report types BasePlayer.OnPlayerReported checks for.
        // Other player report types are shown as Rust sends them.
        static readonly string[] FeedbackTypes = { "General", "Bug", "Cheating", "Abuse", "Idea", "Offensive", "Server rules" };
        static readonly Dictionary<string, string> PlayerTypes = new Dictionary<string, string>
        {
            ["cheat"] = "Cheating", ["break_server_rules"] = "Server rules"
        };

        static string Folder(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "reports");

        // The secret in each server's report address, new each time ToolCupboard starts
        static readonly ConcurrentDictionary<string, string> _keys = new ConcurrentDictionary<string, string>();
        public static string KeyFor(string serverId) => _keys.GetOrAdd(serverId, _ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        public static string ServerForKey(string key) => _keys.FirstOrDefault(pair => pair.Value == key).Key;

        static string TypeName(JToken type, string playerType)
        {
            if (!string.IsNullOrEmpty(playerType)) { return PlayerTypes.TryGetValue(playerType, out string name) ? name : playerType; }
            if (type?.Type == JTokenType.Integer) { int index = type.Value<int>(); return index >= 0 && index < FeedbackTypes.Length ? FeedbackTypes[index] : "General"; }
            return type?.ToString() ?? "General";
        }

        /// <summary>From the "data" field Rust posts to the report address. Returns the report and its screenshot, if any.</summary>
        public static (ServerReport, byte[]) FromEndpoint(string data, string userId)
        {
            JObject feedback = JObject.Parse(data);
            byte[] screenshot = null;
            string image = feedback["AppInfo"]?.Value<string>("Image");
            if (!string.IsNullOrEmpty(image))
            {
                try { screenshot = Convert.FromBase64String(image.Contains(',') ? image.Substring(image.IndexOf(',') + 1) : image); }
                catch (FormatException) { }
            }

            var report = new ServerReport
            {
                Time = DateTime.Now,
                Type = TypeName(feedback["Type"], feedback.Value<string>("TargetReportType")),
                ReporterId = userId,
                TargetId = feedback.Value<string>("TargetId"),
                TargetName = feedback.Value<string>("TargetName"),
                Subject = feedback.Value<string>("Subject"),
                Message = feedback.Value<string>("Message"),
                HasScreenshot = screenshot != null && screenshot.Length > 0
            };
            return (report, report.HasScreenshot ? screenshot : null);
        }

        /// <summary>From an RCON message of type Report, which Rust sends for player reports only and without a screenshot.</summary>
        public static ServerReport FromRcon(string json)
        {
            JObject report = JObject.Parse(json);
            return new ServerReport
            {
                Time = DateTime.Now,
                Type = TypeName(null, report.Value<string>("Type")),
                ReporterId = report.Value<string>("PlayerId"),
                ReporterName = report.Value<string>("PlayerName"),
                TargetId = report.Value<string>("TargetId"),
                TargetName = report.Value<string>("TargetName"),
                Subject = report.Value<string>("Subject"),
                Message = report.Value<string>("Message")
            };
        }

        public static void Add(string serverId, ServerReport report, byte[] screenshot)
        {
            report.Id = $"{report.Time:yyyyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
            Directory.CreateDirectory(Folder(serverId));
            if (screenshot != null) { File.WriteAllBytes(Path.Combine(Folder(serverId), report.Id + ".img"), screenshot); }
            Save(serverId, report);
        }

        static void Save(string serverId, ServerReport report) => RustConfig.ReplaceFile(Path.Combine(Folder(serverId), report.Id + ".json"), JsonConvert.SerializeObject(report, Formatting.Indented));

        // Newest first
        public static List<ServerReport> List(string serverId)
        {
            if (!Directory.Exists(Folder(serverId))) { return new List<ServerReport>(); }

            var reports = new List<ServerReport>();
            foreach (string file in Directory.GetFiles(Folder(serverId), "*.json"))
            {
                try { reports.Add(JsonConvert.DeserializeObject<ServerReport>(File.ReadAllText(file))); }
                catch (Exception e) when (e is JsonException || e is IOException) { }
            }

            return reports.Where(r => r != null).OrderByDescending(r => r.Time).ToList();
        }

        static bool IsValidId(string id) => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, @"\A\d{14}-[0-9a-f]{6}\z");

        // Returns null when saved, otherwise the reason it was refused
        public static string Update(string serverId, string id, string status, string note)
        {
            ServerReport report = IsValidId(id) ? List(serverId).FirstOrDefault(r => r.Id == id) : null;
            if (report == null) { return "That report does not exist."; }
            if (!Statuses.Contains(status)) { return "The status is not valid."; }
            if ((note ?? string.Empty).Length > 2000) { return "The note is too long."; }

            report.Status = status;
            report.Note = note ?? string.Empty;
            Save(serverId, report);
            return null;
        }

        // A data: URL for the dashboard, which may only show images from data: URLs
        public static string ScreenshotUrl(string serverId, string id)
        {
            string path = IsValidId(id) ? Path.Combine(Folder(serverId), id + ".img") : null;
            if (path == null || !File.Exists(path)) { return null; }

            byte[] image = File.ReadAllBytes(path);
            string type = image.Length > 3 && image[0] == 0x89 && image[1] == 0x50 ? "image/png" : "image/jpeg";
            return $"data:{type};base64,{Convert.ToBase64String(image)}";
        }
    }
}
