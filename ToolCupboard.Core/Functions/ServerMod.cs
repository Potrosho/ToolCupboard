using System.IO;
using System.Linq;
using System.Reflection;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// The ToolCupboard Harmony mod (ToolCupboard.Mod), shipped inside this assembly and copied into each server's HarmonyMods folder.
    /// Rebuild the bundled copy with build-mod.ps1.
    /// </summary>
    public static class ServerMod
    {
        public const string FileName = "ToolCupboard.Mod.dll";

        static byte[] Bundled()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        // Copies the bundled mod when it is missing or different. Returns true when the file changed.
        public static bool Install(string serverId)
        {
            string folder = ServerPath.GetServersServerFiles(serverId, "HarmonyMods");
            string path = Path.Combine(folder, FileName);
            byte[] bundled = Bundled();
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bundled)) { return false; }

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, bundled);
            return true;
        }
    }
}
