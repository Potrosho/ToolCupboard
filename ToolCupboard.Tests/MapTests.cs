using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    public class MapTests
    {
        [Theory]
        [InlineData("76561198000000001", null, 120.456, -300.0, "toolcupboard.teleport 76561198000000001 120.5 -300")]
        [InlineData("76561198000000001", "76561198000000002", null, null, "toolcupboard.teleport 76561198000000001 76561198000000002")]
        public void BuildsTeleportCommands(string steamId, string to, double? x, double? z, string expected)
        {
            Assert.Equal(expected, ServerManager.TeleportCommand(steamId, to, x, z));
        }

        [Theory]
        [InlineData("1; quit", null, 1.0, 1.0)]
        [InlineData("76561198000000001", "2 ; quit", null, null)]
        [InlineData("76561198000000001", null, null, 5.0)]
        [InlineData("76561198000000001", null, double.NaN, 5.0)]
        [InlineData("76561198000000001", null, double.PositiveInfinity, 5.0)]
        [InlineData(null, null, 1.0, 1.0)]
        public void RefusesAnythingElse(string steamId, string to, double? x, double? z)
        {
            Assert.Null(ServerManager.TeleportCommand(steamId, to, x, z));
        }

        [Fact]
        public void OnlyTakesAvatarsFromSteam()
        {
            Assert.Equal("https://avatars.steamstatic.com/abc_medium.jpg", SteamAvatars.ParseAvatarUrl("<profile><avatarMedium><![CDATA[https://avatars.steamstatic.com/abc_medium.jpg]]></avatarMedium></profile>"));
            Assert.Null(SteamAvatars.ParseAvatarUrl("<avatarMedium><![CDATA[https://evil.example/a.jpg]]></avatarMedium>"));
            Assert.Null(SteamAvatars.ParseAvatarUrl("<avatarMedium><![CDATA[http://avatars.steamstatic.com/a.jpg]]></avatarMedium>"));
            Assert.Null(SteamAvatars.ParseAvatarUrl("<error>The specified profile could not be found.</error>"));
        }
    }
}
