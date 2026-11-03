using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// What RCON's playerlist leaves out: who is joining or queued, staff, bans and mutes.
    /// </summary>
    static class Roster
    {
        static object Waiting(Network.Connection c) => new { steamId = c.userid.ToString(), name = c.username };

        static List<Network.Connection> QueueList(string field)
        {
            ConnectionQueue queue = SingletonComponent<ServerMgr>.Instance?.connectionQueue;
            // The lists are private in ConnectionQueue
            return queue == null ? new List<Network.Connection>() : HarmonyLib.Traverse.Create(queue).Field(field).GetValue<List<Network.Connection>>() ?? new List<Network.Connection>();
        }

        public static string Get() => JsonConvert.SerializeObject(new
        {
            queued = QueueList("queue").Where(c => c != null).Select(Waiting).ToList(),
            joining = QueueList("joining").Where(c => c != null).Select(Waiting).ToList(),
            staff = ServerUsers.GetAll(ServerUsers.UserGroup.Owner).Select(u => new { steamId = u.steamid.ToString(), name = u.username, role = "owner" })
                .Concat(ServerUsers.GetAll(ServerUsers.UserGroup.Moderator).Select(u => new { steamId = u.steamid.ToString(), name = u.username, role = "moderator" }))
                .ToList(),
            banned = ServerUsers.GetAll(ServerUsers.UserGroup.Banned).Select(u => new { steamId = u.steamid.ToString(), name = u.username, reason = u.notes, expiry = u.expiry }).ToList(),
            // An expiry of 0 means muted until someone unmutes them
            muted = BasePlayer.allPlayerList.Where(p => p != null && p.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute))
                .Select(p => new { steamId = p.UserIDString, name = p.displayName, until = (long)p.State.chatMuteExpiryTimestamp }).ToList()
        });
    }
}
