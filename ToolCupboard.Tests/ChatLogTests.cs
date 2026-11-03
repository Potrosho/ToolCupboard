using System;
using System.IO;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class ChatLogTests
    {
        [Fact]
        public void RconChatIsSavedAndSearchable()
        {
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-chat-" + Guid.NewGuid().ToString("N"));
            try
            {
                long now = DateTimeOffset.Now.ToUnixTimeSeconds();
                ChatLog.Append("1", ChatMessage.FromRcon($"{{\"Channel\":0,\"Message\":\"anyone selling sulfur?\",\"UserId\":\"76561198000000001\",\"Username\":\"Alice\",\"Time\":{now}}}"));
                ChatLog.Append("1", ChatMessage.FromRcon($"{{\"Channel\":1,\"Message\":\"raid at B4\",\"UserId\":\"76561198000000002\",\"Username\":\"Bob\",\"Time\":{now + 1}}}"));

                var all = ChatLog.Search("1", "", "", 7);
                Assert.Equal(2, all.Count);
                Assert.Equal("Bob", all[0].Name); // newest first
                Assert.Equal("Team", all[0].Channel);

                Assert.Single(ChatLog.Search("1", "SULFUR", "", 7));
                Assert.Single(ChatLog.Search("1", "76561198000000002", "", 7));
                Assert.Single(ChatLog.Search("1", "", "Global", 7));
                Assert.Empty(ChatLog.Search("1", "nothing like this", "", 7));
                Assert.Null(ChatMessage.FromRcon("not json"));
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
