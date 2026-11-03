using System;
using System.IO;
using ToolCupboard.Functions;
using Xunit;

namespace ToolCupboard.Tests
{
    [Collection("ServerPath")]
    public class NotesTests
    {
        [Theory]
        [InlineData("Rules", true)]
        [InlineData("Wipe day checklist", true)]
        [InlineData("admin_notes-2", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("../configs/webtoken", false)]
        [InlineData("a/b", false)]
        [InlineData("trailing ", false)]
        [InlineData(" leading", false)]
        [InlineData("con.txt", false)]
        public void NoteNamesAreSafeFileNames(string name, bool valid)
        {
            Assert.Equal(valid, Notes.IsValidName(name));
        }

        [Fact]
        public void NotesAreSavedListedAndDeleted()
        {
            string oldRoot = AppInfo.RootPath;
            AppInfo.RootPath = Path.Combine(Path.GetTempPath(), "tc-notes-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.Empty(Notes.List());
                Notes.Save("Rules", "# Rules\nNo racism.");
                Assert.Equal("Rules", Assert.Single(Notes.List()).Name);
                Assert.Equal("# Rules\nNo racism.", Notes.Read("Rules"));
                Assert.Null(Notes.Read("Missing"));
                Assert.True(Notes.Delete("Rules"));
                Assert.False(Notes.Delete("Rules"));
                Assert.Empty(Notes.List());
            }
            finally
            {
                if (Directory.Exists(AppInfo.RootPath)) { Directory.Delete(AppInfo.RootPath, true); }
                AppInfo.RootPath = oldRoot;
            }
        }
    }
}
