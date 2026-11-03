using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// A log kept as one JSON object per line, in one file per day (yyyyMMdd.jsonl).
    /// </summary>
    public static class JsonDayLog
    {
        static readonly object _writing = new object();

        public static void Append(string folder, DateTime time, object entry)
        {
            string line = JsonConvert.SerializeObject(entry) + Environment.NewLine;
            lock (_writing)
            {
                Directory.CreateDirectory(folder);
                using (var stream = new FileStream(Path.Combine(folder, $"{time:yyyyMMdd}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(line);
                }
            }
        }

        // Entries from the given day onwards, newest first. Lines that do not parse are skipped.
        public static IEnumerable<T> ReadNewestFirst<T>(string folder, DateTime fromDay) where T : class
        {
            if (!Directory.Exists(folder)) { yield break; }

            string from = fromDay.ToString("yyyyMMdd");
            foreach (string file in Directory.GetFiles(folder, "*.jsonl").Where(f => string.CompareOrdinal(Path.GetFileNameWithoutExtension(f), from) >= 0).OrderByDescending(f => f))
            {
                foreach (string line in Enumerable.Reverse(ReadLinesShared(file)))
                {
                    T entry;
                    try { entry = JsonConvert.DeserializeObject<T>(line); }
                    catch (JsonException) { continue; }
                    if (entry != null) { yield return entry; }
                }
            }
        }

        // Reads while the file may be open for appending
        static List<string> ReadLinesShared(string file)
        {
            var lines = new List<string>();
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length > 0) { lines.Add(line); }
                }
            }

            return lines;
        }
    }
}
