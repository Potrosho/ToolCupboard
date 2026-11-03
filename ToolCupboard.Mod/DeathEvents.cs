using UnityEngine;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// Reports each player death with who killed them, for the leaderboard and the activity feed.
    /// </summary>
    static class DeathEvents
    {
        // BasePlayer.OnDied(HitInfo info)
        public static void OnDied(BasePlayer __instance, object[] __args)
        {
            try
            {
                BasePlayer victim = __instance;
                if (victim == null || victim.IsNpc) { return; }
                HitInfo info = __args.Length > 0 ? __args[0] as HitInfo : null;

                // A player who bleeds out after being downed has no attacker on the last hit
                BasePlayer killer = info?.InitiatorPlayer ?? victim.lastAttacker as BasePlayer;
                bool pvp = killer != null && killer != victim && !killer.IsNpc;

                Events.Add("death", new
                {
                    victim = victim.UserIDString,
                    victimName = victim.displayName,
                    killer = pvp ? killer.UserIDString : null,
                    killerName = pvp ? killer.displayName : null,
                    killerNpc = killer != null && killer.IsNpc,
                    suicide = killer == victim,
                    headshot = pvp && info != null && info.isHeadshot,
                    weapon = info?.Weapon?.ShortPrefabName ?? info?.WeaponPrefab?.ShortPrefabName,
                    distance = pvp ? Mathf.Round(Vector3.Distance(victim.transform.position, killer.transform.position)) : 0f,
                    cause = victim.lastDamage.ToString()
                });
            }
            catch (System.Exception e)
            {
                // A mistake here must never stop the death itself
                Debug.LogWarning($"[ToolCupboard] Death event failed: {e.Message}");
            }
        }
    }
}
