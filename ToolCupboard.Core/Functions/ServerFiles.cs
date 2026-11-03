using System;
using System.IO;
using System.Linq;

namespace ToolCupboard.Functions
{
    public class ServerFileEntry
    {
        public string Name { get; set; }
        public bool Folder { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
    }

    /// <summary>
    /// Browses and changes the files of one server. Every path is relative to the server folder and may not leave it.
    /// </summary>
    public static class ServerFiles
    {
        // The full path, or null when the path points outside the server folder
        public static string Resolve(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(fullRoot, (relative ?? string.Empty).TrimStart('/', '\\'))).TrimEnd(Path.DirectorySeparatorChar);
            return full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        public static bool IsRoot(string root, string full) => full.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        // Folders first, then files, each by name
        public static ServerFileEntry[] List(string folder)
        {
            var info = new DirectoryInfo(folder);
            return info.GetDirectories().Select(d => new ServerFileEntry { Name = d.Name, Folder = true, Modified = d.LastWriteTime })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Concat(info.GetFiles().Select(f => new ServerFileEntry { Name = f.Name, Size = f.Length, Modified = f.LastWriteTime }).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }
    }
}
