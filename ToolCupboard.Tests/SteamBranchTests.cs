using System;
using ToolCupboard.Installer;
using Xunit;

namespace ToolCupboard.Tests
{
    public class SteamBranchTests
    {
        const string AppInfo = "\"258550\"\r\n{\r\n\t\"depots\"\r\n\t{\r\n\t\t\"258552\"\r\n\t\t{\r\n\t\t\t\"manifests\"\r\n\t\t\t{\r\n\t\t\t\t\"public\"\r\n\t\t\t\t{\r\n\t\t\t\t\t\"gid\"\t\t\"111\"\r\n\t\t\t\t\t\"size\"\t\t\"222\"\r\n\t\t\t\t}\r\n\t\t\t}\r\n\t\t}\r\n"
            + "\t\t\"branches\"\r\n\t\t{\r\n\t\t\t\"public\"\r\n\t\t\t{\r\n\t\t\t\t\"buildid\"\t\t\"20645632\"\r\n\t\t\t\t\"timeupdated\"\t\t\"1762444800\"\r\n\t\t\t}\r\n"
            + "\t\t\t\"staging\"\r\n\t\t\t{\r\n\t\t\t\t\"buildid\"\t\t\"20651234\"\r\n\t\t\t\t\"description\"\t\t\"Staging\"\r\n\t\t\t\t\"timeupdated\"\t\t\"1762531200\"\r\n\t\t\t}\r\n\t\t}\r\n\t}\r\n}";

        [Fact]
        public void ReadsTheBuildOfEachBranch()
        {
            Assert.Equal("20645632", SteamCMD.ParseBuild(AppInfo, "public", out DateTimeOffset? publicTime));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762444800), publicTime);

            Assert.Equal("20651234", SteamCMD.ParseBuild(AppInfo, "staging", out DateTimeOffset? stagingTime));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762531200), stagingTime);
        }

        [Fact]
        public void AMissingBranchHasNoBuild()
        {
            Assert.Equal(string.Empty, SteamCMD.ParseBuild(AppInfo, "aux01", out DateTimeOffset? time));
            Assert.Null(time);
            Assert.Equal(string.Empty, SteamCMD.ParseBuild("Steam is down", "public", out _));
        }
    }
}
