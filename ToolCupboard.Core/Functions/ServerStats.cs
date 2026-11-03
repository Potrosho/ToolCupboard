using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ToolCupboard.Functions
{
    /// <summary>
    /// The reply to Rust's serverinfo RCON command.
    /// </summary>
    public class RustServerInfo
    {
        public double Framerate { get; set; }
        public int Players { get; set; }
        public int MaxPlayers { get; set; }
        public int Queued { get; set; }
        public int Joining { get; set; }
        public int EntityCount { get; set; }
        public int Memory { get; set; }
        public long NetworkIn { get; set; }
        public long NetworkOut { get; set; }
        public string Version { get; set; }
        public string Protocol { get; set; }
    }

    public class StatsSample
    {
        public DateTime Time;
        public double Fps, Players, Queued, Entities, NetIn, NetOut, Cpu, MemoryMb;

        public static readonly string[] Names = { "fps", "players", "queued", "entities", "netIn", "netOut", "cpu", "memoryMb" };

        public double[] Values => new[] { Fps, Players, Queued, Entities, NetIn, NetOut, Cpu, MemoryMb };
    }

    /// <summary>
    /// Keeps one sample a minute per server in a CSV file per day, for 7 days.
    /// </summary>
    public static class ServerStats
    {
        public const int KeepDays = 7;

        static string Folder(string serverId) => Path.Combine(ServerPath.GetServers(serverId), "metrics");

        public static void Append(string serverId, StatsSample sample)
        {
            string folder = Folder(serverId);
            Directory.CreateDirectory(folder);
            string line = string.Join(",", new[] { sample.Time.ToString("o", CultureInfo.InvariantCulture) }
                .Concat(sample.Values.Select(v => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture))));
            File.AppendAllText(Path.Combine(folder, $"{sample.Time:yyyyMMdd}.csv"), line + Environment.NewLine);

            foreach (string file in Directory.GetFiles(folder, "*.csv"))
            {
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day) && day < DateTime.Today.AddDays(-KeepDays))
                {
                    File.Delete(file);
                }
            }
        }

        // Samples since now - range, averaged into at most maxPoints buckets
        public static List<StatsSample> Read(string serverId, TimeSpan range, int maxPoints = 300)
        {
            DateTime from = DateTime.Now - range;
            string folder = Folder(serverId);
            var samples = new List<StatsSample>();
            if (!Directory.Exists(folder)) { return samples; }

            for (DateTime day = from.Date; day <= DateTime.Today; day = day.AddDays(1))
            {
                string file = Path.Combine(folder, $"{day:yyyyMMdd}.csv");
                if (!File.Exists(file)) { continue; }

                foreach (string line in File.ReadLines(file))
                {
                    StatsSample sample = Parse(line);
                    if (sample != null && sample.Time >= from) { samples.Add(sample); }
                }
            }

            return Downsample(samples, from, range, maxPoints);
        }

        public static StatsSample Parse(string line)
        {
            string[] parts = line.Split(',');
            if (parts.Length != StatsSample.Names.Length + 1 || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time)) { return null; }

            var v = new double[StatsSample.Names.Length];
            for (int i = 0; i < v.Length; i++)
            {
                if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) { return null; }
            }

            return new StatsSample { Time = time, Fps = v[0], Players = v[1], Queued = v[2], Entities = v[3], NetIn = v[4], NetOut = v[5], Cpu = v[6], MemoryMb = v[7] };
        }

        static List<StatsSample> Downsample(List<StatsSample> samples, DateTime from, TimeSpan range, int maxPoints)
        {
            if (samples.Count <= maxPoints) { return samples; }

            double bucketTicks = (double)range.Ticks / maxPoints;
            return samples
                .GroupBy(s => (int)((s.Time - from).Ticks / bucketTicks))
                .Select(g => new StatsSample
                {
                    Time = g.First().Time,
                    Fps = g.Average(s => s.Fps),
                    Players = g.Average(s => s.Players),
                    Queued = g.Average(s => s.Queued),
                    Entities = g.Average(s => s.Entities),
                    NetIn = g.Average(s => s.NetIn),
                    NetOut = g.Average(s => s.NetOut),
                    Cpu = g.Average(s => s.Cpu),
                    MemoryMb = g.Average(s => s.MemoryMb)
                })
                .ToList();
        }
    }
}
