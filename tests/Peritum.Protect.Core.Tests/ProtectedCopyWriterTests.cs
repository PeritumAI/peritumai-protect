using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Tests;

public class ProtectedCopyWriterTests
{
    [Fact]
    public void Writes_sibling_and_does_not_change_original_hash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-writer-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "Invoice.txt");
            File.WriteAllText(original, "ada@example.com");
            var before = Hashing.Sha256File(original);

            var first = ProtectedCopyWriter.Write(original, "clean"u8.ToArray(), "{name}_protect{ext}");
            var second = ProtectedCopyWriter.Write(original, "clean"u8.ToArray(), "{name}_protect{ext}");

            Assert.Equal(Path.Combine(dir, "Invoice_protect.txt"), first);
            Assert.Equal(Path.Combine(dir, "Invoice_protect_2.txt"), second);
            Assert.Equal("ada@example.com", File.ReadAllText(original));
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.Equal("clean", File.ReadAllText(first));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Refuses_a_pattern_that_points_at_the_original_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-writer-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "Invoice.txt");
            File.WriteAllText(original, "same");
            var dest = ProtectedCopyWriter.Write(original, "other"u8.ToArray(), "{name}{ext}");
            Assert.NotEqual(Path.GetFullPath(original), Path.GetFullPath(dest));
            Assert.Equal("same", File.ReadAllText(original));
            Assert.True(File.Exists(dest));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
