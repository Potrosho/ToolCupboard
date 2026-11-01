using System;
using System.IO;
using System.Linq;
using NCrontab;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public sealed class RustServerFilesTests : IDisposable
    {
        private const string SERVER_ID = "1";
        private readonly string _data;

        private static readonly string[] MapFiles = { "proceduralmap.1000.1234.289.map", "proceduralmap.1000.1234.289.sav", "proceduralmap.1000.1234.289.sav.1", "proceduralmap.1000.1234.289.navmesh", "proceduralmap.1000.1234.289_occlusion_3.dat" };
        private static readonly string[] BlueprintFiles = { "player.blueprints.18.db", "player.blueprints.18.db-wal" };
        private static readonly string[] PlayerFiles = { "player.identities.18.db", "player.identities.18.db-wal", "player.states.289.db", "player.deaths.18.db", "sv.files.289.db", "relationship.289.db", "clans.289.db" };
        private static readonly string[] KeptFiles = { "player.tokens.db", "player.tokens.db-wal", "companion.id" };

        public RustServerFilesTests()
        {
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "rustgsm-tests-" + Guid.NewGuid().ToString("N"));
            _data = Path.Combine(ServerPath.GetServersServerFiles(SERVER_ID, "server"), "test");
            Directory.CreateDirectory(Path.Combine(_data, "cfg"));

            foreach (string file in MapFiles.Concat(BlueprintFiles).Concat(PlayerFiles).Concat(KeptFiles))
            {
                File.WriteAllText(Path.Combine(_data, file), "data");
            }

            File.WriteAllText(Path.Combine(_data, "cfg", "bans.cfg"), "ban");
            File.WriteAllText(ConfigPath, "+rcon.port 28016\r\n+rcon.password \"abc123\"\r\n+server.identity \"test\"\r\n+server.seed 1234\r\n-logfile \"server.log\"\r\n");
        }

        private static string ConfigPath => ServerPath.GetServersServerFiles(SERVER_ID, "server.cfg");

        private string[] Left() => Directory.GetFiles(_data).Select(Path.GetFileName).ToArray();

        public void Dispose()
        {
            Directory.Delete(AppInfo.RootPath, true);
        }

        [Fact]
        public void MapWipe_RemovesOnlyMapFiles()
        {
            RustWipe.Wipe(SERVER_ID, RustWipeType.Map);

            Assert.Empty(Left().Intersect(MapFiles));
            Assert.Equal(BlueprintFiles.Length + PlayerFiles.Length + KeptFiles.Length, Left().Length);
        }

        [Fact]
        public void BlueprintWipe_RemovesOnlyBlueprints()
        {
            RustWipe.Wipe(SERVER_ID, RustWipeType.Blueprints);

            Assert.Empty(Left().Intersect(BlueprintFiles));
            Assert.Equal(MapFiles.Length + PlayerFiles.Length + KeptFiles.Length, Left().Length);
        }

        [Fact]
        public void FullWipe_KeepsBansAndRustPlusPairing()
        {
            string backup = RustWipe.Wipe(SERVER_ID, RustWipeType.Full);

            Assert.Equal(KeptFiles.OrderBy(x => x), Left().OrderBy(x => x));
            Assert.True(File.Exists(Path.Combine(_data, "cfg", "bans.cfg")));
            Assert.Equal(MapFiles.Length + BlueprintFiles.Length + PlayerFiles.Length, Directory.GetFiles(backup).Length);
        }

        [Fact]
        public void FailedWipe_PutsMovedFilesBack()
        {
            int before = Left().Length;

            // The last file in the map wipe order is locked, so earlier files are moved first
            using (File.Open(Path.Combine(_data, MapFiles[4]), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => RustWipe.Wipe(SERVER_ID, RustWipeType.Map));
            }

            Assert.Equal(before, Left().Length);
            Assert.Empty(Directory.GetDirectories(Path.Combine(ServerPath.GetServers(SERVER_ID), "wipe-backups")));
        }

        [Fact]
        public void Wipe_KeepsOnlyTheLastThreeBackups()
        {
            string backups = Path.Combine(ServerPath.GetServers(SERVER_ID), "wipe-backups");
            foreach (string name in new[] { "20260101-000000-Map", "20260201-000000-Map", "20260301-000000-Map" })
            {
                Directory.CreateDirectory(Path.Combine(backups, name));
            }

            RustWipe.Wipe(SERVER_ID, RustWipeType.Blueprints);

            Assert.Equal(3, Directory.GetDirectories(backups).Length);
            Assert.False(Directory.Exists(Path.Combine(backups, "20260101-000000-Map")));
        }

        [Fact]
        public void SetRandomSeed_ReplacesTheSeedOnly()
        {
            int seed = RustWipe.SetRandomSeed(SERVER_ID);
            string config = File.ReadAllText(ConfigPath);

            Assert.Equal(seed.ToString(), RustConfig.Get(SERVER_ID, "server.seed"));
            Assert.Single(config.Split('\n'), line => line.Contains("server.seed"));
            Assert.Contains("+rcon.port 28016", config);
        }

        [Fact]
        public void Set_CleansQuotesAndLineBreaks()
        {
            RustConfig.Set(SERVER_ID, "server.description", "Say \"hi\"\r\n+rcon.password x");

            Assert.Contains("+server.description \"Say 'hi' +rcon.password x\"", File.ReadAllText(ConfigPath));
            Assert.Equal("abc123", RustConfig.Get(SERVER_ID, "rcon.password"));
        }

        [Fact]
        public void Set_LeavesNoTempFileBehind()
        {
            RustConfig.Set(SERVER_ID, "server.tickrate", "30");

            Assert.False(File.Exists(ConfigPath + ".tmp"));
            Assert.Equal("30", RustConfig.Get(SERVER_ID, "server.tickrate"));
        }

        [Fact]
        public void ReadSettings_ReadsRconPortAndPassword()
        {
            Assert.Equal((28016, "abc123"), RustRcon.ReadSettings(SERVER_ID));
        }

        [Fact]
        public void ReadSettings_ReturnsNullWithoutAPassword()
        {
            File.WriteAllText(ConfigPath, "+rcon.port 28016\r\n");

            Assert.Null(RustRcon.ReadSettings(SERVER_ID));
        }

        [Theory]
        [InlineData(new string[0], "28015")]
        [InlineData(new[] { "28015" }, "28018")]
        [InlineData(new[] { "28018", "28015" }, "28021")]
        [InlineData(new[] { "28016" }, "28015")]
        [InlineData(new[] { "", "not a port" }, "28015")]
        public void AvailablePort_SkipsPortsAlreadyInUse(string[] used, string expected)
        {
            Assert.Equal(expected, new ServerConfig(null).GetAvailablePort("28015", 3, used));
        }

        [Fact]
        public void DefaultSchedule_IsTheFirstThursdayOfEachMonth()
        {
            var schedule = CrontabSchedule.Parse(RustWipe.DefaultSchedule);
            DateTime next = new DateTime(2026, 11, 1);

            for (int i = 0; i < 12; i++)
            {
                next = schedule.GetNextOccurrence(next);
                Assert.Equal(DayOfWeek.Thursday, next.DayOfWeek);
                Assert.True(next.Day <= 7);
            }
        }
    }
}
