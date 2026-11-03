using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ToolCupboard.Functions
{
    public class NoteEntry
    {
        public string Name { get; set; }
        public DateTime Modified { get; set; }
    }

    /// <summary>
    /// Markdown notes shared by all servers, one .md file per note in the notes folder.
    /// </summary>
    public static class Notes
    {
        static string Folder => ServerPath.Get("notes");

        // The name becomes a file name
        public static bool IsValidName(string name) => name != null && Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9 _-]{0,79}\z") && !name.EndsWith(' ');

        static string PathOf(string name) => Path.Combine(Folder, name + ".md");

        public static NoteEntry[] List()
        {
            if (!Directory.Exists(Folder)) { return Array.Empty<NoteEntry>(); }

            return new DirectoryInfo(Folder).GetFiles("*.md")
                .Select(f => new NoteEntry { Name = Path.GetFileNameWithoutExtension(f.Name), Modified = f.LastWriteTime })
                .Where(n => IsValidName(n.Name))
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Null when there is no such note
        public static string Read(string name) => IsValidName(name) && File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : null;

        public static void Save(string name, string text)
        {
            Directory.CreateDirectory(Folder);
            RustConfig.ReplaceFile(PathOf(name), text);
        }

        public static bool Delete(string name)
        {
            if (!IsValidName(name) || !File.Exists(PathOf(name))) { return false; }

            File.Delete(PathOf(name));
            return true;
        }
    }
}
