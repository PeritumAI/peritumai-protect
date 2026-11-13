using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Tests;

public class CopyDestinationTests
{
    [Fact]
    public void Copies_stay_beside_the_original_unless_a_writable_folder_is_chosen()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-copies-" + Guid.NewGuid().ToString("n"));
        var sourceDir = Path.Combine(root, "source");
        var chosen = Path.Combine(root, "chosen");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(chosen);
        var original = Path.Combine(sourceDir, "note.txt");
        File.WriteAllText(original, "ada@example.com");
        var bytes = System.Text.Encoding.UTF8.GetBytes("[EMAIL]");
        try
        {
            var beside = ProtectedCopyWriter.Write(original, bytes, "{name}_protect{ext}");
            Assert.Equal(sourceDir, Path.GetDirectoryName(beside));
            Assert.Equal("note_protect.txt", Path.GetFileName(beside));

            var parked = ProtectedCopyWriter.Write(original, bytes, "{name}_protect{ext}", chosen);
            Assert.Equal(chosen, Path.GetDirectoryName(parked));
            Assert.True(File.Exists(parked));

            var (folder, note) = CopyDestination.Resolve("folder", chosen);
            Assert.Equal(Path.GetFullPath(chosen), folder);
            Assert.Null(note);

            var (fallback, why) = CopyDestination.Resolve("folder", Path.Combine(root, "missing"));
            Assert.Null(fallback);
            Assert.Equal(CopyDestination.FallbackNote, why);

            var (defaultFolder, defaultNote) = CopyDestination.Resolve("beside", chosen);
            Assert.Null(defaultFolder);
            Assert.Null(defaultNote);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
