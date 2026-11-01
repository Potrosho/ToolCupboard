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
