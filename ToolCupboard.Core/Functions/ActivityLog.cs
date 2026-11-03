using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class ActivityEntry
    {
        public DateTime Time { get; set; }
        public string Type { get; set; }
        public string SteamId { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }

        // The other player in a kill, so searching for either one finds it
        public string OtherId { get; set; }
        public string OtherName { get; set; }

        // From a join, leave, kick, ban or death event sent by the ToolCupboard mod, otherwise null
        public static ActivityEntry FromMod(string type, JToken data, DateTime time)
        {
            if (data == null) { return null; }

            switch (type)
            {
                case "join":
                    return new ActivityEntry { Time = time, Type = type, SteamId = data.Value<string>("steamId"), Name = data.Value<string>("name"), Detail = data.Value<string>("ip") };
                case "leave":
                    return new ActivityEntry { Time = time, Type = type, SteamId = data.Value<string>("steamId"), Name = data.Value<string>("name") };
                case "kick":
                case "ban":
                    // Rust puts "Kicked: " in front of the reason it was given
                    string reason = data.Value<string>("reason") ?? string.Empty;
                    return new ActivityEntry { Time = time, Type = type, SteamId = data.Value<string>("steamId"), Name = data.Value<string>("name"), Detail = reason.StartsWith("Kicked: ") ? reason.Substring(8) : reason };
                case "death":
                    PlayerDeath death = PlayerDeath.FromMod(data, time);
                    return death == null ? null : new ActivityEntry { Time = time, Type = type, SteamId = death.Victim, Name = death.VictimName, OtherId = death.Killer, OtherName = death.KillerName, Detail = DescribeDeath(death) };
                default:
                    return null;
            }
        }

        public static string DescribeDeath(PlayerDeath death)
        {
            if (string.IsNullOrEmpty(death.Killer)) { return string.IsNullOrEmpty(death.Cause) ? "Died" : $"Died: {death.Cause}"; }

            var details = new List<string>();
            if (death.Headshot) { details.Add("headshot"); }
            if (death.Distance > 0) { details.Add($"{death.Distance:0} m"); }
            string weapon = string.IsNullOrEmpty(death.Weapon) ? string.Empty : $" with {death.Weapon.Replace(".entity", string.Empty)}";
            return $"Killed by {death.KillerName}{weapon}{(details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty)}";
        }
    }

    /// <summary>
    /// Joins, leaves, kicks, bans and deaths kept per day. Chat is kept by ChatLog and joined in when searching.
    /// </summary>
    public static class ActivityLog
    {
        static string Folder(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "activity");

        public static void Append(string serverId, ActivityEntry entry) => JsonDayLog.Append(Folder(serverId), entry.Time, entry);

        // Newest first. The query matches a player name or Steam ID, or the details.
        public static List<ActivityEntry> Search(string serverId, string query, string type, int days, int limit = 300)
        {
            query = query?.Trim() ?? string.Empty;
            DateTime from = DateTime.Today.AddDays(1 - days);

            IEnumerable<ActivityEntry> events = type == "chat" ? Enumerable.Empty<ActivityEntry>() : JsonDayLog.ReadNewestFirst<ActivityEntry>(Folder(serverId), from)
                .Where(e => string.IsNullOrEmpty(type) || e.Type == type)
                .Where(e => query.Length == 0 || e.SteamId == query || e.OtherId == query || Contains(e.Name, query) || Contains(e.OtherName, query) || Contains(e.Detail, query))
                .Take(limit);

            IEnumerable<ActivityEntry> chat = !string.IsNullOrEmpty(type) && type != "chat" ? Enumerable.Empty<ActivityEntry>() : ChatLog.Search(serverId, query, string.Empty, days, limit)
                .Select(m => new ActivityEntry { Time = m.Time, Type = "chat", SteamId = m.SteamId, Name = m.Name, Detail = $"[{m.Channel}] {m.Message}" });

            return events.Concat(chat).OrderByDescending(e => e.Time).Take(limit).ToList();
        }

        static bool Contains(string text, string query) => text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
