using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    public class GroupCommandTests
    {
        [Theory]
        [InlineData("teams", "12", "kick", "76561198000000001", null, "toolcupboard.team kick 12 76561198000000001")]
        [InlineData("teams", "12", "disband", null, null, "toolcupboard.team disband 12")]
        [InlineData("clans", "5", "role", "76561198000000001", "2", "toolcupboard.clan role 5 76561198000000001 2")]
        [InlineData("clans", "5", "disband", null, null, "toolcupboard.clan disband 5 0 0")]
        public void BuildsTheModCommand(string kind, string group, string action, string steamId, string roleId, string expected)
        {
            Assert.Equal(expected, ServerManager.GroupCommand(kind, group, action, steamId, roleId, out string error));
            Assert.Null(error);
        }

        // Anything from the page goes into an RCON command, so only plain numbers and known actions get through
        [Theory]
        [InlineData("teams", "12; quit", "kick", "1", null)]
        [InlineData("teams", "12", "kick", "1 ; server.stop", null)]
        [InlineData("teams", "12", "quit", "1", null)]
        [InlineData("clans", "5", "role", "1", "2; quit")]
        [InlineData("clans", "5", "kick", null, null)]
        [InlineData("players", "5", "kick", "1", null)]
        [InlineData("teams", "-1", "disband", null, null)]
        public void RefusesAnythingElse(string kind, string group, string action, string steamId, string roleId)
        {
            Assert.Null(ServerManager.GroupCommand(kind, group, action, steamId, roleId, out string error));
            Assert.NotNull(error);
        }
    }
}
