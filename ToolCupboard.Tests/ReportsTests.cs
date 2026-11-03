using System;
using System.IO;
using System.Linq;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class ReportsTests
    {
        static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

        [Fact]
        public void ReadsWhatRustPostsAndBroadcasts()
        {
            string feedback = "{\"Subject\":\"Stuck in rock\",\"Message\":\"At G7\",\"Type\":1,\"TargetReportType\":null,\"TargetId\":null,\"AppInfo\":{\"Image\":\"" + Convert.ToBase64String(Jpeg) + "\"}}";
            var (bug, shot) = Reports.FromEndpoint(feedback, "7656");
            Assert.Equal("Bug", bug.Type);
            Assert.Equal("7656", bug.ReporterId);
            Assert.False(bug.IsPlayerReport);
            Assert.True(bug.HasScreenshot);
            Assert.Equal(Jpeg, shot);

            var (cheat, none) = Reports.FromEndpoint("{\"Subject\":\"aimbot\",\"Type\":2,\"TargetReportType\":\"cheat\",\"TargetId\":\"7657\",\"TargetName\":\"Bob\",\"AppInfo\":{}}", "7656");
            Assert.Equal("Cheating", cheat.Type);
            Assert.True(cheat.IsPlayerReport);
            Assert.Null(none);

            ServerReport fromRcon = Reports.FromRcon("{\"PlayerId\":\"7656\",\"PlayerName\":\"Alice\",\"TargetId\":\"7657\",\"TargetName\":\"Bob\",\"Subject\":\"rude\",\"Message\":\"\",\"Type\":\"abuse\"}");
            Assert.Equal("Alice", fromRcon.ReporterName);
            Assert.Equal("abuse", fromRcon.Type); // an unknown player report type is shown as Rust sends it
        }

        [Fact]
        public void EachServerHasItsOwnKey()
        {
            string a = Reports.KeyFor("1001"), b = Reports.KeyFor("1002");
            Assert.NotEqual(a, b);
            Assert.Equal(a, Reports.KeyFor("1001"));
            Assert.Equal("1002", Reports.ServerForKey(b));
            Assert.Null(Reports.ServerForKey("wrong"));
        }

        [Fact]
        public void SavesUpdatesAndShowsScreenshots()
        {
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-reports-" + Guid.NewGuid().ToString("N"));
            try
            {
                Reports.Add("1", new ServerReport { Time = DateTime.Now.AddMinutes(-1), Type = "Bug", Subject = "old" }, Jpeg);
                Reports.Add("1", new ServerReport { Time = DateTime.Now, Type = "Cheating", Subject = "new", TargetId = "7657" }, null);

                var list = Reports.List("1");
                Assert.Equal(new[] { "new", "old" }, list.Select(r => r.Subject));
                Assert.All(list, r => Assert.Equal("open", r.Status));

                Assert.Null(Reports.Update("1", list[1].Id, "resolved", "Moved them out"));
                ServerReport resolved = Reports.List("1").Single(r => r.Subject == "old");
                Assert.Equal("resolved", resolved.Status);
                Assert.Equal("Moved them out", resolved.Note);

                Assert.NotNull(Reports.Update("1", list[1].Id, "deleted", null));
                Assert.NotNull(Reports.Update("1", "../../configs/webtoken", "open", null));

                Assert.StartsWith("data:image/jpeg;base64,", Reports.ScreenshotUrl("1", resolved.Id));
                Assert.Null(Reports.ScreenshotUrl("1", list[0].Id));
                Assert.Null(Reports.ScreenshotUrl("1", "..\\..\\x"));
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
