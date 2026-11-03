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
        static string Folder(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "chat");

        public static void Append(string serverId, ChatMessage message) => JsonDayLog.Append(Folder(serverId), message.Time, message);

        // Newest first. The query matches the message, the player name, or the Steam ID.
        public static List<ChatMessage> Search(string serverId, string query, string channel, int days, int limit = 300)
        {
            query = query?.Trim() ?? string.Empty;
            return JsonDayLog.ReadNewestFirst<ChatMessage>(Folder(serverId), DateTime.Today.AddDays(1 - days))
                .Where(message => string.IsNullOrEmpty(channel) || message.Channel == channel)
                .Where(message => query.Length == 0 || Contains(message.Message, query) || Contains(message.Name, query) || message.SteamId == query)
                .Take(limit)
                .ToList();
        }

        static bool Contains(string text, string query) => text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
