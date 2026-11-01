using System;
using System.IO;
using System.Threading.Tasks;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    public sealed class FolderSwapTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rustgsm-swap-" + Guid.NewGuid().ToString("N"));
        private string Target => Path.Combine(_root, "1");
        private string Replacement => Path.Combine(_root, "1.restoring");

        public FolderSwapTests()
        {
            Directory.CreateDirectory(Target);
            Directory.CreateDirectory(Replacement);
            File.WriteAllText(Path.Combine(Target, "world.sav"), "current");
            File.WriteAllText(Path.Combine(Replacement, "world.sav"), "backup");
        }

        public void Dispose()
        {
            Directory.Delete(_root, true);
        }

        [Fact]
        public void Replace_SwapsTheFolderAndCleansUp()
        {
            FolderSwap.Replace(Target, Replacement);

            Assert.Equal("backup", File.ReadAllText(Path.Combine(Target, "world.sav")));
            Assert.False(Directory.Exists(Target + ".old"));
            Assert.False(Directory.Exists(Replacement));
        }

        [Fact]
        public void Replace_PutsTheOriginalBackIfTheNewFolderStaysLocked()
        {
            using (File.Open(Path.Combine(Replacement, "world.sav"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAny<Exception>(() => FolderSwap.Replace(Target, Replacement, attempts: 2, retryDelayMs: 10));
            }

            Assert.Equal("current", File.ReadAllText(Path.Combine(Target, "world.sav")));
            Assert.False(Directory.Exists(Target + ".old"));
        }

        [Fact]
        public async Task Replace_WaitsOutAShortLock()
        {
            var locked = File.Open(Path.Combine(Replacement, "world.sav"), FileMode.Open, FileAccess.Read, FileShare.None);
            Task release = Task.Delay(200).ContinueWith(_ => locked.Dispose());

            FolderSwap.Replace(Target, Replacement, attempts: 20, retryDelayMs: 50);
            await release;

            Assert.Equal("backup", File.ReadAllText(Path.Combine(Target, "world.sav")));
        }
    }
}
