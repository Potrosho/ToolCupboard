using System.Security.Cryptography;
using System.Text;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class UmodCatalogTests
    {
        [Fact]
        public void WritePlugin_WorksOnAFreshServerAndReplacesADisabledCopy()
        {
            ServerPath.Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rustgsm-tests-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                RustMods.WritePlugin("1", RustModFramework.Oxide, "Kits", Encoding.UTF8.GetBytes("first"));

                string plugins = RustMods.GetPluginsPath("1", RustModFramework.Oxide);
                RustMods.SetEnabled("1", RustModFramework.Oxide, RustMods.GetPlugins("1", RustModFramework.Oxide)[0], false);
                RustMods.WritePlugin("1", RustModFramework.Oxide, "Kits", Encoding.UTF8.GetBytes("second"));

                Assert.Equal("second", System.IO.File.ReadAllText(System.IO.Path.Combine(plugins, "Kits.cs")));
                Assert.False(System.IO.File.Exists(System.IO.Path.Combine(plugins, "disabled", "Kits.cs")));
            }
            finally
            {
                System.IO.Directory.Delete(ServerPath.Root, true);
            }
        }

        [Fact]
        public void ParsesSearchResults()
        {
            UmodPage page = UmodCatalog.ParsePage(@"{""current_page"":2,""last_page"":12,""data"":[{""slug"":""custom-auto-kits"",""name"":""CustomAutoKits"",""title"":""Custom Auto Kits"",""description"":""Automatic kits"",""latest_release_version"":""1.2.6"",""downloads_shortened"":""32.0K"",""url"":""https://umod.org/plugins/custom-auto-kits"",""tags_all"":""rust,kits,unmaintained""}]}");

            Assert.Equal(2, page.Page);
            Assert.Equal(12, page.LastPage);
            Assert.Equal("custom-auto-kits", page.Items[0].Slug);
            Assert.Equal("1.2.6", page.Items[0].Version);
            Assert.True(page.Items[0].Unmaintained);
        }

        [Fact]
        public void ParsesAnEmptyPage()
        {
            Assert.Empty(UmodCatalog.ParsePage("{}").Items);
        }

        [Theory]
        [InlineData("custom-auto-kits", true)]
        [InlineData("kits", true)]
        [InlineData("../secrets", false)]
        [InlineData("kits.json", false)]
        [InlineData("kits\n", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyPlainSlugsAreAccepted(string slug, bool valid)
        {
            Assert.Equal(valid, UmodCatalog.IsValidSlug(slug));
        }

        [Theory]
        [InlineData("CustomAutoKits", true)]
        [InlineData("Kits_2", true)]
        [InlineData("..\\Evil", false)]
        [InlineData("../Evil", false)]
        [InlineData("a.b", false)]
        [InlineData("Kits\n", false)]
        public void PluginNamesCannotLeaveThePluginsFolder(string name, bool valid)
        {
            Assert.Equal(valid, UmodCatalog.IsValidPluginName(name));
        }

        [Theory]
        [InlineData("https://umod.org/plugins/Kits.cs", true)]
        [InlineData("http://umod.org/plugins/Kits.cs", false)]
        [InlineData("https://umod.org.evil.example/Kits.cs", false)]
        [InlineData("https://evil.example/umod.org/Kits.cs", false)]
        [InlineData("not a url", false)]
        public void DownloadsMustComeFromUmod(string url, bool trusted)
        {
            Assert.Equal(trusted, UmodCatalog.IsTrustedUrl(url));
        }

        [Fact]
        public void ChecksumMustMatchTheContent()
        {
            byte[] content = Encoding.UTF8.GetBytes("plugin");
            string checksum = System.Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();

            Assert.True(UmodCatalog.MatchesChecksum(content, checksum));
            Assert.False(UmodCatalog.MatchesChecksum(Encoding.UTF8.GetBytes("changed"), checksum));
        }
    }
}
