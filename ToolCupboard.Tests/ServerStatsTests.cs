using System;
using System.IO;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class ServerStatsTests
    {
        [Fact]
        public void SavedSamplesReadBackInRangeAndDownsampled()
        {
            string root = Path.Combine(Path.GetTempPath(), "tc-stats-" + Guid.NewGuid().ToString("N"));
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = root;
            try
            {
                DateTime now = DateTime.Now;
                for (int minute = 120; minute >= 0; minute--)
                {
                    ServerStats.Append("1", new StatsSample { Time = now.AddMinutes(-minute), Fps = minute, Entities = 1000, NetIn = 2048.5 });
                }

                var lastHour = ServerStats.Read("1", TimeSpan.FromHours(1));
                Assert.InRange(lastHour.Count, 59, 61);
                Assert.All(lastHour, s => Assert.True(s.Fps <= 60));
                Assert.Equal(1000, lastHour[0].Entities);
                Assert.Equal(2048.5, lastHour[0].NetIn);

                var squeezed = ServerStats.Read("1", TimeSpan.FromHours(2), maxPoints: 10);
                Assert.InRange(squeezed.Count, 9, 11);
            }
            finally
            {
                AppInfo.RootPath = oldRoot;
                if (Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        }

        [Theory]
        [InlineData("not a sample")]
        [InlineData("2026-11-02T10:00:00,1,2")]
        [InlineData("2026-11-02T10:00:00,x,0,0,0,0,0,0,0")]
        public void BrokenLinesAreSkipped(string line)
        {
            Assert.Null(ServerStats.Parse(line));
        }
    }
}
