using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class ChatMessage
    {
        public DateTime Time { get; set; }
        public string Channel { get; set; }
        public string SteamId { get; set; }
        public string Name { get; set; }
        public string Message { get; set; }

        static readonly string[] Channels = { "Global", "Team", "Server", "Cards", "Local", "Clan" };

        // Reads the JSON of an RCON packet with Type "Chat"
        public static ChatMessage FromRcon(string json)
        {
            try
            {
                JObject chat = JObject.Parse(json);
                int channel = chat.Value<int?>("Channel") ?? 0;
                long? unix = chat.Value<long?>("Time");
                return new ChatMessage
                {
                    Time = unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix.Value).LocalDateTime : DateTime.Now,
                    Channel = channel >= 0 && channel < Channels.Length ? Channels[channel] : channel.ToString(CultureInfo.InvariantCulture),
                    SteamId = chat.Value<string>("UserId") ?? string.Empty,
                    Name = chat.Value<string>("Username") ?? string.Empty,
                    Message = chat.Value<string>("Message") ?? string.Empty
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Keeps every chat message in a JSON-lines file per day.
    /// </summary>
    public static class ChatLog
    {
        static readonly object _writing = new object();

        static string Folder(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "chat");

        public static void Append(string serverId, ChatMessage message)
        {
            string line = JsonConvert.SerializeObject(message) + Environment.NewLine;
            lock (_writing)
            {
                Directory.CreateDirectory(Folder(serverId));
                using (var stream = new FileStream(Path.Combine(Folder(serverId), $"{message.Time:yyyyMMdd}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(line);
                }
            }
        }

        // Newest first. The query matches the message, the player name, or the Steam ID.
        public static List<ChatMessage> Search(string serverId, string query, string channel, int days, int limit = 300)
        {
            var found = new List<ChatMessage>();
            string folder = Folder(serverId);
            if (!Directory.Exists(folder)) { return found; }

            string from = DateTime.Today.AddDays(1 - days).ToString("yyyyMMdd");
            query = query?.Trim() ?? string.Empty;

            foreach (string file in Directory.GetFiles(folder, "*.jsonl").Where(f => string.CompareOrdinal(Path.GetFileNameWithoutExtension(f), from) >= 0).OrderByDescending(f => f))
            {
                foreach (string line in Enumerable.Reverse(ReadLinesShared(file)))
                {
                    ChatMessage message;
                    try { message = JsonConvert.DeserializeObject<ChatMessage>(line); }
                    catch (JsonException) { continue; }
                    if (message == null) { continue; }

                    if (!string.IsNullOrEmpty(channel) && message.Channel != channel) { continue; }
                    if (query.Length > 0 && !Contains(message.Message, query) && !Contains(message.Name, query) && message.SteamId != query) { continue; }

                    found.Add(message);
                    if (found.Count >= limit) { return found; }
                }
            }

            return found;
        }

        static bool Contains(string text, string query) => text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        // Reads while the file may be open for appending
        static List<string> ReadLinesShared(string file)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length > 0) { lines.Add(line); }
                }
            }

            return lines;
        }
    }
}
