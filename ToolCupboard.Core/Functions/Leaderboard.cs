using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ToolCupboard.Functions
{
    public class PlayerDeath
    {
        public DateTime Time { get; set; }
        public string Victim { get; set; }
        public string VictimName { get; set; }
        public string Killer { get; set; }
        public string KillerName { get; set; }
        public bool Headshot { get; set; }
        public string Weapon { get; set; }
        public double Distance { get; set; }
        public string Cause { get; set; }

        // From the data of a "death" event sent by the ToolCupboard mod
        public static PlayerDeath FromMod(JToken data, DateTime time)
        {
            string victim = data?.Value<string>("victim");
            if (string.IsNullOrEmpty(victim)) { return null; }

            return new PlayerDeath
            {
                Time = time,
                Victim = victim,
                VictimName = data.Value<string>("victimName"),
                Killer = data.Value<string>("killer"),
                KillerName = data.Value<string>("killerName"),
                Headshot = data.Value<bool?>("headshot") ?? false,
                Weapon = data.Value<string>("weapon"),
                Distance = data.Value<double?>("distance") ?? 0,
                Cause = data.Value<string>("cause")
            };
        }
    }

    public class LeaderboardRow
    {
        public string SteamId { get; set; }
        public string Name { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Headshots { get; set; }
        public double KillDeath => Deaths == 0 ? Kills : Math.Round((double)Kills / Deaths, 2);
        public double HeadshotPercent => Kills == 0 ? 0 : Math.Round(100.0 * Headshots / Kills, 1);
    }

    /// <summary>
    /// Player deaths kept per day, and the kills, deaths, K/D and headshot share worked out from them.
    /// </summary>
    public static class Leaderboard
    {
        static string Folder(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "deaths");

        public static void Append(string serverId, PlayerDeath death) => JsonDayLog.Append(Folder(serverId), death.Time, death);

        // Kills count only player against player. Deaths count every death, so K/D matches what players see in game.
        public static List<LeaderboardRow> Build(IEnumerable<PlayerDeath> deaths)
        {
            var rows = new Dictionary<string, LeaderboardRow>();
            LeaderboardRow Row(string id, string name)
            {
                if (!rows.TryGetValue(id, out LeaderboardRow row)) { rows[id] = row = new LeaderboardRow { SteamId = id, Name = name }; }
                if (string.IsNullOrEmpty(row.Name)) { row.Name = name; }
                return row;
            }

            // Newest first, so each player keeps the latest name they used
            foreach (PlayerDeath death in deaths)
            {
                Row(death.Victim, death.VictimName).Deaths++;
                if (!string.IsNullOrEmpty(death.Killer) && death.Killer != death.Victim)
                {
                    LeaderboardRow killer = Row(death.Killer, death.KillerName);
                    killer.Kills++;
                    if (death.Headshot) { killer.Headshots++; }
                }
            }

            return rows.Values.OrderByDescending(r => r.Kills).ThenBy(r => r.Deaths).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<LeaderboardRow> Get(string serverId, DateTime from, DateTime to)
        {
            return Build(JsonDayLog.ReadNewestFirst<PlayerDeath>(Folder(serverId), from.Date).Where(d => d.Time >= from && d.Time < to));
        }
    }
}
