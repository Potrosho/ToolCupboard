using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class LeaderboardTests
    {
        static PlayerDeath Death(string victim, string killer, bool headshot = false, string killerName = null) =>
            new PlayerDeath { Time = DateTime.Now, Victim = victim, VictimName = victim + "-name", Killer = killer, KillerName = killerName ?? killer + "-name", Headshot = headshot };

        [Fact]
        public void CountsKillsDeathsKdAndHeadshots()
        {
            var rows = Leaderboard.Build(new[]
            {
                Death("bob", "alice", headshot: true),
                Death("bob", "alice"),
                Death("carol", "alice", headshot: true),
                Death("alice", "bob"),
                Death("bob", null),      // killed by an animal or the environment
                Death("carol", "carol")  // suicide
            });

            LeaderboardRow alice = rows.Single(r => r.SteamId == "alice");
            Assert.Equal(3, alice.Kills);
            Assert.Equal(1, alice.Deaths);
            Assert.Equal(3, alice.KillDeath);
            Assert.Equal(66.7, alice.HeadshotPercent);

            LeaderboardRow bob = rows.Single(r => r.SteamId == "bob");
            Assert.Equal(1, bob.Kills);
            Assert.Equal(3, bob.Deaths);
            Assert.Equal(0.33, bob.KillDeath);

            LeaderboardRow carol = rows.Single(r => r.SteamId == "carol");
            Assert.Equal(0, carol.Kills);
            Assert.Equal(2, carol.Deaths);
            Assert.Equal("alice", rows[0].SteamId);
        }

        [Fact]
        public void KeepsTheNewestNameAndNoDeathsMeansKdIsKills()
        {
            var rows = Leaderboard.Build(new[] { Death("bob", "alice", killerName: "Alice New"), Death("bob", "alice", killerName: "Alice Old") });
            LeaderboardRow alice = rows.Single(r => r.SteamId == "alice");
            Assert.Equal("Alice New", alice.Name);
            Assert.Equal(2, alice.KillDeath);
        }

        [Fact]
        public void ReadsModEventsAndFiltersByDate()
        {
            Assert.Null(PlayerDeath.FromMod(JObject.Parse("{\"victim\":null}"), DateTime.Now));
            PlayerDeath death = PlayerDeath.FromMod(JObject.Parse("{\"victim\":\"7656\",\"victimName\":\"Bob\",\"killer\":\"7657\",\"killerName\":\"Alice\",\"headshot\":true,\"weapon\":\"ak47u.entity\",\"distance\":42}"), DateTime.Now);
            Assert.True(death.Headshot);
            Assert.Equal(42, death.Distance);

            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-board-" + Guid.NewGuid().ToString("N"));
            try
            {
                Leaderboard.Append("1", death);
                var old = Death("x", "y");
                old.Time = DateTime.Now.AddMonths(-2);
                Leaderboard.Append("1", old);

                var thisMonth = Leaderboard.Get("1", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today.AddDays(1));
                Assert.Equal(new[] { "7657", "7656" }, thisMonth.Select(r => r.SteamId));
                Assert.Equal(4, Leaderboard.Get("1", DateTime.Today.AddMonths(-3), DateTime.Today.AddDays(1)).Count);
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
