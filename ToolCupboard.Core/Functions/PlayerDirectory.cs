using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ToolCupboard.Functions
{
    public class KnownPlayer
    {
        public string SteamId { get; set; }
        public string Name { get; set; }
        public List<string> OtherNames { get; set; } = new List<string>();
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string LastIp { get; set; }
        public long PlaySeconds { get; set; }
    }

    /// <summary>
    /// Everyone who has joined a server, kept in one file per server and updated on each join and leave.
    /// </summary>
    public static class PlayerDirectory
    {
        static readonly object _writing = new object();

        // When each online player joined, to add up their time when they leave
        static readonly ConcurrentDictionary<string, DateTime> _joined = new ConcurrentDictionary<string, DateTime>();

        static string FileOf(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "players.json");

        public static Dictionary<string, KnownPlayer> Load(string serverId)
        {
            string path = FileOf(serverId);
            try { return File.Exists(path) ? JsonConvert.DeserializeObject<Dictionary<string, KnownPlayer>>(File.ReadAllText(path)) ?? new Dictionary<string, KnownPlayer>() : new Dictionary<string, KnownPlayer>(); }
            catch (JsonException) { return new Dictionary<string, KnownPlayer>(); }
        }

        static void Change(string serverId, string steamId, DateTime time, Action<KnownPlayer> change)
        {
            if (string.IsNullOrEmpty(steamId)) { return; }

            lock (_writing)
            {
                Dictionary<string, KnownPlayer> players = Load(serverId);
                if (!players.TryGetValue(steamId, out KnownPlayer player)) { players[steamId] = player = new KnownPlayer { SteamId = steamId, FirstSeen = time }; }
                change(player);
                Directory.CreateDirectory(Path.GetDirectoryName(FileOf(serverId)));
                RustConfig.ReplaceFile(FileOf(serverId), JsonConvert.SerializeObject(players));
            }
        }

        static void Rename(KnownPlayer player, string name)
        {
            if (string.IsNullOrEmpty(name) || name == player.Name) { return; }
            if (!string.IsNullOrEmpty(player.Name) && !player.OtherNames.Contains(player.Name)) { player.OtherNames.Insert(0, player.Name); }
            player.OtherNames.Remove(name);
            if (player.OtherNames.Count > 20) { player.OtherNames.RemoveRange(20, player.OtherNames.Count - 20); }
            player.Name = name;
        }

        public static void Joined(string serverId, string steamId, string name, string ip, DateTime time)
        {
            _joined[$"{serverId}/{steamId}"] = time;
            Change(serverId, steamId, time, player =>
            {
                Rename(player, name);
                player.LastSeen = time;
                if (!string.IsNullOrEmpty(ip)) { player.LastIp = ip; }
            });
        }

        public static void Left(string serverId, string steamId, string name, DateTime time)
        {
            bool known = _joined.TryRemove($"{serverId}/{steamId}", out DateTime joined);
            Change(serverId, steamId, time, player =>
            {
                Rename(player, name);
                player.LastSeen = time;
                // A join that ToolCupboard did not see, for example before it started, adds nothing
                if (known && time > joined) { player.PlaySeconds += (long)(time - joined).TotalSeconds; }
            });
        }
    }
}
