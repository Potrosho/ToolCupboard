using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class DemosTests
    {
        [Theory]
        [InlineData("demos/76561198000000001/2026-11-03-013000.dem", true)]
        [InlineData("demos/76561198000000001/../../configs/webtoken.txt", false)]
        [InlineData("demos/76561198000000001/x.dem/../../server.cfg", false)]
        [InlineData("server.cfg", false)]
        [InlineData("demos/abc/x.dem", false)]
        [InlineData("demos\\76561198000000001\\x.dem", false)]
        [InlineData(null, false)]
        public void OnlyDemoFilesCanBeNamed(string path, bool valid)
        {
            Assert.Equal(valid, Demos.IsDemoPath(path));
        }

        // Rust's file names use a 12-hour clock, so the side of noon comes from when the file appeared
        [Theory]
        [InlineData("2026-11-03-015004.dem", "2026-11-03T13:51:06", "2026-11-03T13:50:04")]
        [InlineData("2026-11-03-015004.dem", "2026-11-03T01:51:06", "2026-11-03T01:50:04")]
        [InlineData("2026-11-03-115959.dem", "2026-11-04T00:00:30", "2026-11-03T23:59:59")]
        [InlineData("renamed.dem", "2026-11-03T13:51:06", "2026-11-03T13:51:06")]
        public void ReadsTheStartFromTheFileName(string name, string created, string expected)
        {
            Assert.Equal(DateTime.Parse(expected), Demos.StartOf(name, DateTime.Parse(created)));
        }

        [Fact]
        public void ReadsTheAutoRecordList()
        {
            Assert.Equal(new[] { "76561198000000001", "76561198000000002" }, Demos.ParseRecordList("demo.recordlist: \"76561198000000001, 76561198000000002,76561198000000001,bad\""));
            Assert.Empty(Demos.ParseRecordList("demo.recordlist: \"\""));
            Assert.Empty(Demos.ParseRecordList(null));
        }

        [Fact]
        public void ListsDemosWithNotesAndZipsThem()
        {
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-demos-" + Guid.NewGuid().ToString("N"));
            try
            {
                string folder = ServerPath.GetServersServerFiles("1", "demos", "76561198000000001");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "2026-11-03-013000.dem"), "demo");
                File.WriteAllText(Path.Combine(folder, "not-a-demo.txt"), "x");
                const string path = "demos/76561198000000001/2026-11-03-013000.dem";

                Demos.ChangeNotes("1", path, n => { n.Pinned = true; n.Note = "Aim locks at 4:10"; });
                DemoFile demo = Assert.Single(Demos.List("1"));
                Assert.Equal(path, demo.Path);
                Assert.Equal("76561198000000001", demo.SteamId);
                Assert.True(demo.Pinned);
                Assert.Equal("Aim locks at 4:10", demo.Note);
                Assert.Equal(4, demo.Bytes);

                using var zip = new MemoryStream();
                Demos.WriteZip("1", new[] { path, path, "../../configs/webtoken.txt" }, zip);
                zip.Position = 0;
                using var archive = new ZipArchive(zip);
                Assert.Equal(new[] { "76561198000000001_2026-11-03-013000.dem" }, archive.Entries.Select(e => e.FullName));
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
