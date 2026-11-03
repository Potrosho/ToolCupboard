using System.IO;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    public class ServerFilesTests
    {
        const string Root = @"C:\tc\servers\3\serverfiles";

        [Theory]
        [InlineData("", Root)]
        [InlineData(null, Root)]
        [InlineData("server.cfg", Root + @"\server.cfg")]
        [InlineData("oxide/config/Kits.json", Root + @"\oxide\config\Kits.json")]
        [InlineData("/oxide/plugins", Root + @"\oxide\plugins")]
        [InlineData("oxide/../server.cfg", Root + @"\server.cfg")]
        public void PathsInsideTheServerFolderResolve(string path, string expected)
        {
            Assert.Equal(expected, ServerFiles.Resolve(Root, path));
        }

        [Theory]
        [InlineData("..")]
        [InlineData("../../../configs/webtoken.txt")]
        [InlineData(@"..\serverfiles2\x")]
        [InlineData(@"C:\Windows\win.ini")]
        [InlineData("C:/Windows/win.ini")]
        [InlineData(@"C:\tc\servers\3\serverfiles2\x")]
        public void PathsOutsideTheServerFolderAreRefused(string path)
        {
            Assert.Null(ServerFiles.Resolve(Root, path));
        }

        [Fact]
        public void ListsFoldersBeforeFiles()
        {
            string folder = Path.Combine(Path.GetTempPath(), "tc-files-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "b-folder"));
                File.WriteAllText(Path.Combine(folder, "a.txt"), "hi");
                ServerFileEntry[] entries = ServerFiles.List(folder);
                Assert.Equal(new[] { "b-folder", "a.txt" }, System.Array.ConvertAll(entries, e => e.Name));
                Assert.True(entries[0].Folder);
                Assert.Equal(2, entries[1].Size);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
