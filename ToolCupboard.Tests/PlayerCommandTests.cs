using System;
using System.IO;
using ToolCupboard.Functions;
using ToolCupboard.Web;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class PlayerCommandTests
    {
        const string Id = "76561198000000001";

        static string[] Commands(string action, WebPlayerAction request, string currentRole = "none") =>
            ServerManager.PlayerCommands(Id, action, request, "Bob \"the\" Builder", currentRole, out _);

        [Fact]
        public void BuildsTheConsoleCommands()
        {
            Assert.Equal(new[] { $"kick {Id} \"spam  quit\"" }, Commands("kick", new WebPlayerAction { Reason = "spam; quit" }));
            Assert.Equal(new[] { $"banid {Id} \"Bob  the  Builder\" \"Banned by an admin\"" }, Commands("ban", new WebPlayerAction()));
            Assert.Equal(new[] { $"mute {Id} 600s" }, Commands("mute", new WebPlayerAction { Minutes = 10 }));
            Assert.Equal(new[] { $"mute {Id}" }, Commands("mute", new WebPlayerAction { Minutes = 0 }));
            Assert.Equal(new[] { $"inventory.giveto {Id} rifle.ak 2" }, Commands("give", new WebPlayerAction { Item = "rifle.ak", Amount = 2 }));
            Assert.Equal(new[] { $"removemoderator {Id}", $"ownerid {Id} \"Bob  the  Builder\" \"Set from ToolCupboard\"" }, Commands("role", new WebPlayerAction { Role = "owner" }, "moderator"));
            Assert.Equal(new[] { $"removeowner {Id}" }, Commands("role", new WebPlayerAction { Role = "none" }, "owner"));
        }

        // Every value from the page ends up in a console command
        [Fact]
        public void RefusesAnythingElse()
        {
            Assert.Null(ServerManager.PlayerCommands("1; quit", "kick", new WebPlayerAction(), "x", "none", out _));
            Assert.Null(Commands("quit", new WebPlayerAction()));
            Assert.Null(Commands("give", new WebPlayerAction { Item = "rifle.ak; quit", Amount = 1 }));
            Assert.Null(Commands("give", new WebPlayerAction { Item = "rifle.ak", Amount = 0 }));
            Assert.Null(Commands("give", new WebPlayerAction { Item = "rifle.ak", Amount = 100001 }));
            Assert.Null(Commands("mute", new WebPlayerAction { Minutes = -5 }));
            Assert.Null(Commands("role", new WebPlayerAction { Role = "admin" }));
            Assert.Null(Commands("role", new WebPlayerAction { Role = "moderator" }, "moderator"));
            Assert.Equal(new string('a', 120), ServerManager.ConsoleText(new string('a', 300)));
            Assert.Equal("a b", ServerManager.ConsoleText("a\r\nb"));
        }

        [Fact]
        public void RemembersNamesAndPlayTime()
        {
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-players-" + Guid.NewGuid().ToString("N"));
            try
            {
                DateTime start = DateTime.Now.AddHours(-2);
                PlayerDirectory.Joined("1", Id, "Bob", "10.0.0.5", start);
                PlayerDirectory.Left("1", Id, "Bob", start.AddMinutes(30));
                PlayerDirectory.Joined("1", Id, "Robert", "10.0.0.6", start.AddHours(1));
                PlayerDirectory.Left("1", Id, "Robert", start.AddHours(1).AddMinutes(15));
                PlayerDirectory.Left("1", "76561198000000002", "Unseen", start); // a leave without a join adds no time

                KnownPlayer bob = PlayerDirectory.Load("1")[Id];
                Assert.Equal("Robert", bob.Name);
                Assert.Equal(new[] { "Bob" }, bob.OtherNames);
                Assert.Equal("10.0.0.6", bob.LastIp);
                Assert.Equal(45 * 60, bob.PlaySeconds);
                Assert.Equal(start, bob.FirstSeen, TimeSpan.FromMinutes(1));
                Assert.Equal(0, PlayerDirectory.Load("1")["76561198000000002"].PlaySeconds);
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
