using UnityEngine;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Reports players joining, leaving, being kicked and being banned, for the activity feed.
    /// A mistake in any of these must never stop the game's own code, so each one catches everything.
    /// </summary>
    static class PlayerEvents
    {
        static void Report(string type, System.Func<object> data)
        {
            try { Events.Add(type, data()); }
            catch (System.Exception e) { Debug.LogWarning($"[ToolCupboard] {type} event failed: {e.Message}"); }
        }

        static bool IsPlayer(BasePlayer player) => player != null && !player.IsNpc;

        // BasePlayer.PlayerInit(Connection)
        public static void Join(BasePlayer __instance, object[] __args)
        {
            if (!IsPlayer(__instance)) { return; }
            Report("join", () =>
            {
                string address = (__args.Length > 0 ? __args[0] as Network.Connection : null)?.ipaddress ?? string.Empty;
                int port = address.LastIndexOf(':');
                return new { steamId = __instance.UserIDString, name = __instance.displayName, ip = port > 0 ? address.Substring(0, port) : address };
            });
        }

        public static void Leave(BasePlayer __instance)
        {
            if (!IsPlayer(__instance)) { return; }
            Report("leave", () => new { steamId = __instance.UserIDString, name = __instance.displayName });
        }

        // BasePlayer.Kick(string reason)
        public static void Kick(BasePlayer __instance, object[] __args)
        {
            if (!IsPlayer(__instance)) { return; }
            Report("kick", () => new { steamId = __instance.UserIDString, name = __instance.displayName, reason = __args.Length > 0 ? __args[0] as string : null });
        }

        // ServerUsers.Set(ulong uid, UserGroup group, string username, string notes, ...)
        public static void Ban(object[] __args)
        {
            if (__args.Length < 4 || !(__args[1] is ServerUsers.UserGroup group) || group != ServerUsers.UserGroup.Banned) { return; }
            Report("ban", () => new { steamId = __args[0]?.ToString(), name = __args[2] as string, reason = __args[3] as string });
        }
    }
}
