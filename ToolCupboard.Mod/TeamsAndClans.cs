using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Lists teams and clans and runs admin actions on them, for the Teams and Clans tab.
    /// </summary>
    static class TeamsAndClans
    {
        static string Json(object value) => JsonConvert.SerializeObject(value);
        static string Error(string message) => Json(new { error = message });

        static string NameOf(ulong steamId) => BasePlayer.FindAwakeOrSleepingByID(steamId)?.displayName ?? ServerUsers.Get(steamId)?.username ?? steamId.ToString();
        static bool IsOnline(ulong steamId) => BasePlayer.FindByID(steamId)?.IsConnected == true;
        static object Member(ulong steamId) => new { steamId = steamId.ToString(), name = NameOf(steamId), online = IsOnline(steamId) };

        public static string Teams()
        {
            RelationshipManager teams = RelationshipManager.ServerInstance;
            if (teams == null) { return Json(new object[0]); }

            return Json(teams.teams.Values.Select(team => new
            {
                id = team.teamID.ToString(),
                name = team.teamName,
                leader = team.teamLeader.ToString(),
                members = team.members.Select(Member).ToList()
            }).ToList());
        }

        // toolcupboard.team promote|kick|disband <teamId> [steamId]
        public static string Team(string action, string teamText, string playerText)
        {
            RelationshipManager.PlayerTeam team = ulong.TryParse(teamText, out ulong teamId) ? RelationshipManager.ServerInstance?.FindTeam(teamId) : null;
            if (team == null) { return Error("That team no longer exists."); }
            ulong.TryParse(playerText, out ulong steamId);

            switch (action)
            {
                case "disband":
                    RelationshipManager.ServerInstance.DisbandTeam(team);
                    return Json(new { ok = true });
                case "promote":
                    if (!team.members.Contains(steamId)) { return Error("That player is not in the team."); }
                    team.SetTeamLeader(steamId);
                    return Json(new { ok = true });
                case "kick":
                    if (!team.members.Contains(steamId)) { return Error("That player is not in the team."); }
                    team.RemovePlayer(steamId);
                    return Json(new { ok = true });
                default:
                    return Error("Unknown team action.");
            }
        }

        // Clan calls are async but a console command must answer at once. While one is still running, the reply is
        // {"pending":true} and asking again with the same arguments returns its result, without running it a second time.
        static readonly ConcurrentDictionary<string, Task<string>> _running = new ConcurrentDictionary<string, Task<string>>();

        static string Await(string key, Func<Task<string>> work)
        {
            Task<string> task = _running.GetOrAdd(key, _ => Run(work));
            if (!task.IsCompleted) { return Json(new { pending = true }); }

            _running.TryRemove(key, out _);
            return task.Result;
        }

        static async Task<string> Run(Func<Task<string>> work)
        {
            try { return await work(); }
            catch (Exception e) { return Error(e.Message); }
        }

        static IClanBackend Backend => ClanManager.ServerInstance?.Backend;

        public static string Clans() => Await("clans", async () =>
        {
            IClanBackend backend = Backend;
            if (backend == null) { return Json(new { enabled = false, clans = new object[0] }); }

            ClanValueResult<List<ClanLeaderboardEntry>> board = await backend.GetLeaderboard(1000);
            if (!board.IsSuccess) { return Error($"Rust could not list clans: {board.Result}"); }

            var clans = new List<object>();
            foreach (ClanLeaderboardEntry entry in board.Value)
            {
                ClanValueResult<IClan> found = await backend.Get(entry.ClanId);
                if (!found.IsSuccess) { continue; }
                IClan clan = found.Value;
                clans.Add(new
                {
                    id = clan.ClanId.ToString(),
                    name = clan.Name,
                    score = clan.Score,
                    owner = Leader(clan).ToString(),
                    maxMembers = clan.MaxMemberCount,
                    roles = clan.Roles.OrderBy(r => r.Rank).Select(r => new { id = r.RoleId, name = r.Name, rank = r.Rank }).ToList(),
                    members = clan.Members.Select(m => new { steamId = m.SteamId.ToString(), name = NameOf(m.SteamId), online = IsOnline(m.SteamId), role = m.RoleId }).ToList(),
                    invites = clan.Invites.Select(i => new { steamId = i.SteamId.ToString(), name = NameOf(i.SteamId), by = NameOf(i.Recruiter) }).ToList()
                });
            }

            return Json(new { enabled = true, clans });
        });

        // The member with the top role, who is allowed to do everything in the clan
        static ulong Leader(IClan clan)
        {
            int top = clan.Roles.OrderBy(r => r.Rank).Select(r => r.RoleId).FirstOrDefault();
            return clan.Members.Where(m => m.RoleId == top).Select(m => m.SteamId).DefaultIfEmpty(clan.Creator).First();
        }

        // toolcupboard.clan kick|role|uninvite|disband <clanId> [steamId] [roleId]
        public static string Clan(string action, string clanText, string playerText, string roleText) => Await($"clan {action} {clanText} {playerText} {roleText}", async () =>
        {
            IClanBackend backend = Backend;
            if (backend == null || !long.TryParse(clanText, out long clanId)) { return Error("Clans are not available."); }

            ClanValueResult<IClan> found = await backend.Get(clanId);
            if (!found.IsSuccess) { return Error("That clan no longer exists."); }
            IClan clan = found.Value;
            ulong leader = Leader(clan);
            ulong.TryParse(playerText, out ulong steamId);

            ClanResult result;
            switch (action)
            {
                case "disband": result = await clan.Disband(leader); break;
                case "kick": result = await clan.Kick(steamId, leader); break;
                case "uninvite": result = await clan.CancelInvite(steamId, leader); break;
                case "role":
                    if (!int.TryParse(roleText, out int roleId)) { return Error("Choose a role."); }
                    result = await clan.SetPlayerRole(steamId, roleId, leader);
                    break;
                default: return Error("Unknown clan action.");
            }

            return result == ClanResult.Success ? Json(new { ok = true }) : Error($"Rust refused it: {result}");
        });
    }
}
