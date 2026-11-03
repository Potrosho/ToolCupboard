using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ToolCupboard.Mod
{
    /// <summary>
    /// The map image, monuments and player positions for the live map, and teleporting from it.
    /// </summary>
    static class MapCommands
    {
        static string Json(object value) => JsonConvert.SerializeObject(value);
        static string Error(string message) => Json(new { error = message });

        // Half a pixel per metre. The dashboard works out how many metres the image covers from this.
        const float PIXELS_PER_METRE = 0.5f;

        // Ocean around the playable square, in metres, so offshore monuments such as oil rigs are on the map
        public const float OCEAN_MARGIN = 1000f;

        // Renders the map as a JPG beside the server, north up
        public static string Render()
        {
            byte[] image = MapImageRenderer.Render(out int width, out int height, out _, PIXELS_PER_METRE, true, false, (int)(OCEAN_MARGIN * PIXELS_PER_METRE));
            if (image == null) { return Error("The map is not loaded yet."); }

            string path = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, $"toolcupboard-map-{World.Size}-{World.Seed}.jpg"));
            File.WriteAllBytes(path, image);
            return Json(new { path, worldSize = World.Size, seed = World.Seed, width, height });
        }

        // TerrainPath.Monuments is internal, so it is read through Harmony
        public static string Info() => Json(new
        {
            worldSize = World.Size,
            seed = World.Seed,
            monuments = ((TerrainMeta.Path == null ? null : HarmonyLib.Traverse.Create(TerrainMeta.Path).Field("Monuments").GetValue<System.Collections.Generic.List<MonumentInfo>>())
                ?? new System.Collections.Generic.List<MonumentInfo>())
                .Where(m => m != null && m.displayPhrase != null && !string.IsNullOrEmpty(m.displayPhrase.english))
                .Select(m => (object)new { name = m.displayPhrase.english, x = Mathf.Round(m.transform.position.x), z = Mathf.Round(m.transform.position.z) })
                .ToArray()
        });

        static object Position(BasePlayer player, bool online) => new
        {
            steamId = player.UserIDString,
            name = player.displayName,
            x = Mathf.Round(player.transform.position.x),
            z = Mathf.Round(player.transform.position.z),
            online,
            dead = player.IsDead(),
            team = player.currentTeam.ToString()
        };

        public static string Positions() => Json(
            BasePlayer.activePlayerList.Where(p => p != null && !p.IsNpc).Select(p => Position(p, true))
                .Concat(BasePlayer.sleepingPlayerList.Where(p => p != null && !p.IsNpc).Select(p => Position(p, false)))
                .ToList());

        // toolcupboard.teleport <steamid> <x> <z> puts a player on the ground at a map position
        // toolcupboard.teleport <steamid> <steamid> puts a player next to another one
        public static string Teleport(string who, string first, string second)
        {
            BasePlayer player = ulong.TryParse(who, out ulong id) ? BasePlayer.FindByID(id) : null;
            if (player == null || !player.IsConnected) { return Error("That player is not online."); }
            if (player.IsDead()) { return Error("That player is dead."); }

            Vector3 target;
            if (string.IsNullOrEmpty(second))
            {
                BasePlayer other = ulong.TryParse(first, out ulong otherId) ? BasePlayer.FindAwakeOrSleepingByID(otherId) : null;
                if (other == null) { return Error("That player was not found."); }
                target = other.transform.position;
            }
            else
            {
                if (!float.TryParse(first, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(second, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    return Error("That map position is not valid.");
                }

                float half = World.Size / 2f + OCEAN_MARGIN;
                if (Mathf.Abs(x) > half || Mathf.Abs(z) > half) { return Error("That position is off the map."); }
                target = new Vector3(x, 0, z);
                // Never under the sea floor or the water, so the player lands standing up
                target.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(target), TerrainMeta.WaterMap.GetHeight(target)) + 1f;
            }

            player.Teleport(target);
            return Json(new { ok = true, x = Mathf.Round(target.x), z = Mathf.Round(target.z) });
        }
    }
}
