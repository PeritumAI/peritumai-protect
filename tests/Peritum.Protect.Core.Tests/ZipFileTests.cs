using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class ZipFileTests
{
    private const string Email = "ada@example.com";
    private const string Key = "AKIAIOSFODNN7EXAMPLE";

    [Fact]
    public void Mixed_zip_protects_supported_entries_and_leaves_the_rest_out()
    {
        using var dir = new WorkDir();
        var docx = dir.File("inside.docx");
        WriteDocx(docx, "Mail " + Email + " today");
        var path = dir.File("bundle.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(archive, "notes.txt", "Write to " + Email + " today");
            Write(archive, "key.txt", "token " + Key);
            Write(archive, "old.odt", "secret " + Email);
            Write(archive, "broken.docx", "this is not a document");
            var entry = archive.CreateEntry("inside.docx", CompressionLevel.NoCompression);
            using var writer = entry.Open();
            writer.Write(File.ReadAllBytes(docx));
        }

        var before = File.ReadAllBytes(path);
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.False(session.Result.Incomplete);
        Assert.Contains("old.odt", session.Result.Note ?? "", StringComparison.Ordinal);
        Assert.Contains("broken.docx", session.Result.Note ?? "", StringComparison.Ordinal);

        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        var text = ReadAll(copy.OutputPath!);
        Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        var names = Names(copy.OutputPath!);
        Assert.Contains("inside.docx", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("notes.txt", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("key.txt", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("old.odt", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("broken.docx", names, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Nested_zip_is_scanned_and_too_deep_is_incomplete()
    {
        using var dir = new WorkDir();
        var path = dir.File("outer.zip");
        File.WriteAllBytes(path, Nest(2, "See " + Email));
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.Contains(Email, session.Text, StringComparison.Ordinal);
        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        Assert.DoesNotContain(Email, ReadAll(copy.OutputPath!), StringComparison.Ordinal);
        using var outer = ZipFile.OpenRead(copy.OutputPath!);
        var inner = outer.GetEntry("inner.zip");
        Assert.NotNull(inner);
        using var innerStream = inner!.Open();
        using var nested = new ZipArchive(innerStream, ZipArchiveMode.Read);
        Assert.NotNull(nested.GetEntry("note.txt"));

        var deep = dir.File("deep.zip");
        File.WriteAllBytes(deep, Nest(5, Email));
        var blocked = Scan(deep);
        Assert.True(blocked.Result.Incomplete);
        Assert.Contains("3 deep", blocked.Result.Note ?? "", StringComparison.Ordinal);
        Assert.Null(new ProtectService().MakeProtectedCopy(deep, Rules(), "{name}_protect{ext}").OutputPath);
    }

    [Fact]
    public void Encrypted_entry_is_not_checked_and_is_left_out()
    {
        using var dir = new WorkDir();
        var path = dir.File("vault.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(archive, "plain.txt", "Write to " + Email + " today");
            Write(archive, "secret.txt", "token " + Key);
        }

        MarkEncrypted(path, "secret.txt");
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.Contains(Email, session.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, session.Text, StringComparison.Ordinal);
        Assert.Contains("Not checked", session.Result.Note ?? "", StringComparison.Ordinal);
        Assert.Contains("secret.txt", session.Result.Note ?? "", StringComparison.Ordinal);
        Assert.Contains("encrypted", session.Result.Note ?? "", StringComparison.OrdinalIgnoreCase);

        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        var names = Names(copy.OutputPath!);
        Assert.Contains("plain.txt", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.txt", names, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(Email, ReadAll(copy.OutputPath!), StringComparison.Ordinal);
        Assert.DoesNotContain(Key, ReadAll(copy.OutputPath!), StringComparison.Ordinal);
    }

    [Fact]
    public void Zip_bomb_is_still_refused()
    {
        using var dir = new WorkDir();
        var path = dir.File("bomb.zip");
        using (var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            using var writer = entry.Open();
            var chunk = Encoding.ASCII.GetBytes(new string('A', 8192));
            for (var i = 0; i < 40; i++)
                writer.Write(chunk);
        }

        var before = File.ReadAllBytes(path);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ScanSession session;
        try
        {
            session = Scan(path);
        }
        catch (Exception ex)
        {
            Assert.Fail("Exception escaped: " + ex);
            return;
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Took " + clock.Elapsed);
        Assert.True(session.Result.Incomplete);
        Assert.Contains("100 to 1", session.Result.Note ?? "", StringComparison.Ordinal);
        var copy = new ProtectService().MakeProtectedCopy(path, Rules(), "{name}_protect{ext}");
        Assert.Null(copy.OutputPath);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Malformed_zip_is_not_checked()
    {
        using var dir = new WorkDir();
        var path = dir.File("broken.zip");
        File.WriteAllText(path, "legacy");
        var session = Scan(path);
        Assert.Equal(UncheckedFiles.Corrupt, session.Result.Error);
        Assert.DoesNotContain("Nothing found", session.Result.Error ?? "", StringComparison.Ordinal);
        Assert.Null(new ProtectService().MakeProtectedCopy(path, Rules(), "{name}_protect{ext}").OutputPath);
    }

    private static ScanSession Scan(string path) =>
        new ProtectService().Scan(ScanRequest.ForFile(path), Rules());

    private static CopyResult Copy(string path) =>
        new ProtectService().MakeProtectedCopy(path, Rules(), "{name}_protect{ext}");

    private static IReadOnlyList<Rule> Rules() =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    private static void WriteDocx(string path, string text)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
        main.Document.Save();
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static byte[] Nest(int depth, string leaf)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            if (depth <= 1)
            {
                var entry = archive.CreateEntry("note.txt", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(leaf);
            }
            else
            {
                var entry = archive.CreateEntry("inner.zip", CompressionLevel.NoCompression);
                using var writer = entry.Open();
                var nested = Nest(depth - 1, leaf);
                writer.Write(nested);
            }
        }

        return ms.ToArray();
    }

    private static void MarkEncrypted(string path, string entryName)
    {
        var bytes = File.ReadAllBytes(path);
        Mark(bytes, [0x50, 0x4B, 0x03, 0x04], 6, 26, 30, entryName);
        Mark(bytes, [0x50, 0x4B, 0x01, 0x02], 8, 28, 46, entryName);
        File.WriteAllBytes(path, bytes);
    }

    private static void Mark(byte[] bytes, byte[] signature, int flagOffset, int nameLenOffset, int nameOffset, string entryName)
    {
        for (var i = 0; i <= bytes.Length - nameOffset; i++)
        {
            var match = true;
            for (var j = 0; j < signature.Length; j++)
            {
                if (bytes[i + j] != signature[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match || i + nameLenOffset + 2 > bytes.Length)
                continue;
            var nameLength = BitConverter.ToUInt16(bytes, i + nameLenOffset);
            if (i + nameOffset + nameLength > bytes.Length)
                continue;
            var name = Encoding.UTF8.GetString(bytes, i + nameOffset, nameLength);
            if (!name.Equals(entryName, StringComparison.Ordinal))
                continue;
            bytes[i + flagOffset] |= 1;
        }
    }

    private static string ReadAll(string path)
    {
        var sb = new StringBuilder();
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            sb.Append(entry.FullName);
            if (entry.FullName.EndsWith('/'))
                continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var body = reader.ReadToEnd();
            if (entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var nested = new MemoryStream();
                using var raw = entry.Open();
                raw.CopyTo(nested);
                var temp = Path.Combine(Path.GetTempPath(), "peritum-zip-read-" + Guid.NewGuid().ToString("n") + ".zip");
                File.WriteAllBytes(temp, nested.ToArray());
                try
                {
                    sb.Append(ReadAll(temp));
                }
                finally
                {
                    File.Delete(temp);
                }
            }
            else
            {
                sb.Append(body);
            }
        }

        return sb.ToString();
    }

    private static List<string> Names(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.Select(entry => entry.FullName).ToList();
    }

    private sealed class WorkDir : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "peritum-zip-" + Guid.NewGuid().ToString("n"));

        public WorkDir() => Directory.CreateDirectory(_path);

        public string File(string name) => System.IO.Path.Combine(_path, name);

        public void Dispose() => Directory.Delete(_path, true);
    }
}
