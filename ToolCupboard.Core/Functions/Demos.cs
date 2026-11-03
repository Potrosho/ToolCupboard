using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace ToolCupboard.Functions
{
    public class DemoNotes
    {
        public bool Pinned { get; set; }
        public string Note { get; set; } = string.Empty;
        public string StartedBy { get; set; }
        public string EndedBy { get; set; }
    }

    public class DemoFile : DemoNotes
    {
        public string Path { get; set; }
        public string SteamId { get; set; }
        public DateTime Started { get; set; }
        public long Seconds { get; set; }
        public long Bytes { get; set; }
    }

    /// <summary>
    /// Server demos, which Rust writes to demos/{steamid}/*.dem in the server folder, plus ToolCupboard's pins and notes on them.
    /// </summary>
    public static class Demos
    {
        static readonly object _writing = new object();

        // The only paths the dashboard may name, so nothing outside the demos folder can be read or deleted
        public static bool IsDemoPath(string path) => path != null && Regex.IsMatch(path, @"\Ademos/\d{1,20}/[\w\-]{1,80}\.dem\z");

        static string NotesFile(string serverId) => System.IO.Path.Combine(ServerPath.GetServers(serverId), "demos.json");

        public static Dictionary<string, DemoNotes> LoadNotes(string serverId)
        {
            try { return File.Exists(NotesFile(serverId)) ? JsonConvert.DeserializeObject<Dictionary<string, DemoNotes>>(File.ReadAllText(NotesFile(serverId))) ?? new Dictionary<string, DemoNotes>() : new Dictionary<string, DemoNotes>(); }
            catch (JsonException) { return new Dictionary<string, DemoNotes>(); }
        }

        public static void ChangeNotes(string serverId, string path, Action<DemoNotes> change)
        {
            lock (_writing)
            {
                Dictionary<string, DemoNotes> notes = LoadNotes(serverId);
                if (!notes.TryGetValue(path, out DemoNotes entry)) { notes[path] = entry = new DemoNotes(); }
                change(entry);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NotesFile(serverId)));
                RustConfig.ReplaceFile(NotesFile(serverId), JsonConvert.SerializeObject(notes));
            }
        }

        public static string FullPath(string serverId, string path) => IsDemoPath(path) ? ServerPath.GetServersServerFiles(serverId, path.Replace('/', System.IO.Path.DirectorySeparatorChar)) : null;

        // Rust names demos yyyy-MM-dd-hhmmss with a 12-hour clock and creates the file only on its first write, so the start
        // is the name's time on whichever side of noon is closest to when the file appeared
        public static DateTime StartOf(string fileName, DateTime created)
        {
            if (!DateTime.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(fileName), "yyyy-MM-dd-hhmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime morning))
            {
                return created;
            }

            DateTime afternoon = morning.AddHours(12);
            return Math.Abs((afternoon - created).Ticks) < Math.Abs((morning - created).Ticks) ? afternoon : morning;
        }

        // Newest first. Length runs from the start in the name to the last write.
        public static List<DemoFile> List(string serverId)
        {
            string root = ServerPath.GetServersServerFiles(serverId, "demos");
            if (!Directory.Exists(root)) { return new List<DemoFile>(); }

            Dictionary<string, DemoNotes> notes = LoadNotes(serverId);
            return Directory.GetDirectories(root).SelectMany(folder => new DirectoryInfo(folder).GetFiles("*.dem").Select(file =>
            {
                string path = $"demos/{System.IO.Path.GetFileName(folder)}/{file.Name}";
                notes.TryGetValue(path, out DemoNotes note);
                DateTime started = StartOf(file.Name, file.CreationTime);
                return new DemoFile
                {
                    Path = path,
                    SteamId = System.IO.Path.GetFileName(folder),
                    Started = started,
                    Seconds = Math.Max(0, (long)(file.LastWriteTime - started).TotalSeconds),
                    Bytes = file.Length,
                    Pinned = note?.Pinned ?? false,
                    Note = note?.Note ?? string.Empty,
                    StartedBy = note?.StartedBy,
                    EndedBy = note?.EndedBy
                };
            })).Where(d => IsDemoPath(d.Path)).OrderByDescending(d => d.Started).ToList();
        }

        public static void WriteZip(string serverId, IEnumerable<string> paths, Stream output)
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (string path in paths.Where(IsDemoPath).Distinct())
                {
                    string full = FullPath(serverId, path);
                    if (File.Exists(full)) { zip.CreateEntryFromFile(full, path.Substring("demos/".Length).Replace('/', '_'), CompressionLevel.Fastest); }
                }
            }
        }

        // The demo.recordlist convar is a comma-separated list of Steam IDs
        public static List<string> ParseRecordList(string reply)
        {
            Match value = Regex.Match(reply ?? string.Empty, "\"([^\"]*)\"");
            return (value.Success ? value.Groups[1].Value : string.Empty).Split(',').Select(id => id.Trim()).Where(id => ulong.TryParse(id, out _)).Distinct().ToList();
        }
    }
}
