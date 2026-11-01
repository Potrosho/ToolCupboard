using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ToolCupboard
{
    /// <summary>
    /// Facts about the running app, the same for every edition.
    /// </summary>
    public static class AppInfo
    {
        public const int MaxServers = 50;

        public static readonly string Version = "v" + string.Concat(Assembly.GetEntryAssembly().GetName().Version.ToString().Reverse().Skip(2).Reverse());

        // Settable so tests can use a temporary folder
        public static string RootPath { get; set; } = System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
    }
}
