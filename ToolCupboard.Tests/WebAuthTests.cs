using ToolCupboard.Web;
using Xunit;

namespace ToolCupboard.Tests
{
    public class WebAuthTests
    {
        [Theory]
        [InlineData("127.0.0.1:27080", true)]
        [InlineData("localhost:27080", true)]
        [InlineData("evil.example:27080", false)]
        [InlineData("127.0.0.1.evil.example:27080", false)]
        [InlineData("127.0.0.1:80", false)]
        [InlineData("", false)]
        public void OnlyLocalHostNamesAreAccepted(string host, bool allowed)
        {
            Assert.Equal(allowed, WebAuth.IsLocalHost(host, 27080));
        }

        [Theory]
        [InlineData("right", "right", true)]
        [InlineData("wrong", "right", false)]
        [InlineData("", "right", false)]
        [InlineData(null, "right", false)]
        [InlineData("", "", false)]
        public void TokenMustMatch(string token, string expected, bool valid)
        {
            Assert.Equal(valid, WebAuth.IsValidToken(token, expected));
        }

        [Theory]
        [InlineData("Potrosho's Backyard", "Potrosho's Backyard", true)]
        [InlineData("potrosho's backyard", "Potrosho's Backyard", false)]
        [InlineData("Potrosho's Backyard ", "Potrosho's Backyard", false)]
        [InlineData("", "Potrosho's Backyard", false)]
        [InlineData(null, "Potrosho's Backyard", false)]
        [InlineData("", "", false)]
        public void DangerousActionsNeedTheExactServerName(string typed, string name, bool confirmed)
        {
            Assert.Equal(confirmed, WebAuth.IsConfirmed(typed, name));
        }

        [Theory]
        [InlineData("https://discord.com/api/webhooks/1/abc", true)]
        [InlineData("https://discordapp.com/api/webhooks/1/abc", true)]
        [InlineData("http://discord.com/api/webhooks/1/abc", false)]
        [InlineData("https://discord.com.evil.example/api/webhooks/1/abc", false)]
        [InlineData("https://evil.example/api/webhooks/1/abc", false)]
        [InlineData("https://discord.com/users/1", false)]
        [InlineData("not a url", false)]
        public void AlertsOnlyGoToDiscordWebhooks(string url, bool valid)
        {
            Assert.Equal(valid, WebAuth.IsDiscordWebhook(url));
        }

        [Theory]
        [InlineData("348921660361146380", "0", true)]
        [InlineData("348921660361146380", "1,2", true)]
        [InlineData("not-an-id", "0", false)]
        [InlineData("348921660361146380", "", false)]
        [InlineData("348921660361146380", "1,", false)]
        [InlineData("348921660361146380\n999", "0", false)]
        public void DiscordAdminsMustBeWellFormed(string id, string servers, bool valid)
        {
            Assert.Equal(valid, WebAuth.IsValidDiscordAdmin(id, servers));
        }

        // index.html reads these names, and the API sends web-style JSON
        [Fact]
        public void PlayersSerializeWithTheNamesThePageReads()
        {
            var player = new ToolCupboard.Functions.RustPlayer { SteamID = "765", DisplayName = "Potrosho", Ping = 12, ConnectedSeconds = 65 };
            string json = System.Text.Json.JsonSerializer.Serialize(player, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            Assert.Contains("\"steamID\":\"765\"", json);
            Assert.Contains("\"displayName\":\"Potrosho\"", json);
            Assert.Contains("\"ping\":12", json);
            Assert.Contains("\"connected\":\"0:01:05\"", json);
        }

        [Fact]
        public void TokensAreLongAndUnique()
        {
            Assert.Equal(64, WebAuth.CreateToken().Length);
            Assert.NotEqual(WebAuth.CreateToken(), WebAuth.CreateToken());
        }
    }
}
