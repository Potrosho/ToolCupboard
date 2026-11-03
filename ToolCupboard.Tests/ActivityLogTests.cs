using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class ActivityLogTests
    {
        [Fact]
        public void DescribesModEvents()
        {
            DateTime now = DateTime.Now;
            Assert.Equal("10.0.0.5", ActivityEntry.FromMod("join", JObject.Parse("{\"steamId\":\"1\",\"name\":\"Bob\",\"ip\":\"10.0.0.5\"}"), now).Detail);
            Assert.Equal("Spamming", ActivityEntry.FromMod("kick", JObject.Parse("{\"steamId\":\"1\",\"name\":\"Bob\",\"reason\":\"Kicked: Spamming\"}"), now).Detail);
            Assert.Null(ActivityEntry.FromMod("loaded", JObject.Parse("{\"version\":\"3\"}"), now));

            ActivityEntry kill = ActivityEntry.FromMod("death", JObject.Parse("{\"victim\":\"1\",\"victimName\":\"Bob\",\"killer\":\"2\",\"killerName\":\"Alice\",\"headshot\":true,\"weapon\":\"ak47u.entity\",\"distance\":42}"), now);
            Assert.Equal("Killed by Alice with ak47u (headshot, 42 m)", kill.Detail);
            Assert.Equal("2", kill.OtherId);

            Assert.Equal("Died: Drowned", ActivityEntry.FromMod("death", JObject.Parse("{\"victim\":\"1\",\"victimName\":\"Bob\",\"cause\":\"Drowned\"}"), now).Detail);
        }

        [Fact]
        public void SearchesEventsAndChatTogether()
        {
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-activity-" + Guid.NewGuid().ToString("N"));
            try
            {
                DateTime now = DateTime.Now;
                ActivityLog.Append("1", ActivityEntry.FromMod("join", JObject.Parse("{\"steamId\":\"1\",\"name\":\"Bob\"}"), now.AddMinutes(-3)));
                ActivityLog.Append("1", ActivityEntry.FromMod("death", JObject.Parse("{\"victim\":\"1\",\"victimName\":\"Bob\",\"killer\":\"2\",\"killerName\":\"Alice\"}"), now.AddMinutes(-1)));
                ChatLog.Append("1", new ChatMessage { Time = now.AddMinutes(-2), Channel = "Global", SteamId = "2", Name = "Alice", Message = "gg" });

                Assert.Equal(new[] { "death", "chat", "join" }, ActivityLog.Search("1", "", "", 7).Select(e => e.Type));
                Assert.Equal(new[] { "death", "chat" }, ActivityLog.Search("1", "2", "", 7).Select(e => e.Type)); // Alice, as killer and in chat
                Assert.Equal("[Global] gg", Assert.Single(ActivityLog.Search("1", "", "chat", 7)).Detail);
                Assert.Equal("join", Assert.Single(ActivityLog.Search("1", "", "join", 7)).Type);
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
