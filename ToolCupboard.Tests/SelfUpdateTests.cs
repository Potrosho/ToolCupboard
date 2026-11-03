using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    public class SelfUpdateTests
    {
        const string Release = @"{
            ""tag_name"": ""v0.2.0"", ""html_url"": ""https://github.com/Potrosho/ToolCupboard/releases/tag/v0.2.0"", ""body"": ""New stuff"",
            ""assets"": [
                { ""name"": ""ToolCupboard.exe"", ""digest"": ""sha256:aa11"", ""browser_download_url"": ""https://github.com/Potrosho/ToolCupboard/releases/download/v0.2.0/ToolCupboard.exe"" },
                { ""name"": ""ToolCupboardWeb.exe"", ""digest"": ""sha256:bb22"", ""browser_download_url"": ""https://github.com/Potrosho/ToolCupboard/releases/download/v0.2.0/ToolCupboardWeb.exe"" },
                { ""name"": ""Evil.exe"", ""digest"": ""sha256:cc33"", ""browser_download_url"": ""https://evil.example/ToolCupboard.exe"" },
                { ""name"": ""NoDigest.exe"", ""browser_download_url"": ""https://github.com/Potrosho/ToolCupboard/releases/download/v0.2.0/NoDigest.exe"" }
            ]
        }";

        [Fact]
        public void PicksTheAssetForThisEdition()
        {
            AppRelease web = SelfUpdate.Parse(Release, "ToolCupboardWeb.exe", "v0.1.2");
            Assert.True(web.Newer);
            Assert.True(web.CanInstall);
            Assert.Equal("bb22", web.Sha256);
            Assert.EndsWith("/ToolCupboardWeb.exe", web.DownloadUrl);
        }

        [Fact]
        public void RefusesDownloadsWithoutADigestOrFromElsewhere()
        {
            Assert.False(SelfUpdate.Parse(Release, "NoDigest.exe", "v0.1.2").CanInstall);
            Assert.False(SelfUpdate.Parse(Release.Replace("https://github.com/Potrosho/ToolCupboard/releases/download/v0.2.0/ToolCupboard.exe", "https://evil.example/ToolCupboard.exe"), "ToolCupboard.exe", "v0.1.2").CanInstall);
            Assert.False(SelfUpdate.Parse(Release, "Renamed.exe", "v0.1.2").CanInstall);
        }

        [Theory]
        [InlineData("v0.2.0", "v0.1.2", true)]
        [InlineData("v0.1.10", "v0.1.9", true)]
        [InlineData("v1.0.0", "v0.9.9", true)]
        [InlineData("v0.1.2", "v0.1.2", false)]
        [InlineData("v0.1.1", "v0.1.2", false)]
        [InlineData("nightly", "v0.1.2", false)]
        public void OnlyNewerVersionsAreUpdates(string latest, string current, bool newer)
        {
            Assert.Equal(newer, SelfUpdate.IsNewer(latest, current));
        }
    }
}
