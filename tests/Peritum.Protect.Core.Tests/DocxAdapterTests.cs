using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Tests;

public class DocxAdapterTests
{
    [Fact]
    public void Protected_docx_is_a_sibling_and_original_hash_matches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-docx-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "Invoice.docx");
            WriteDocx(original, "Email ada@example.com and key sk-testFAKEkey1234567890.");
            var before = Hashing.Sha256File(original);
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

            var copy = new ProtectService().MakeProtectedCopy(original, rules, "{name}_protect{ext}");

            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, copy.OriginalHashBefore);
            Assert.Equal(before, copy.OriginalHashAfter);
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.EndsWith("Invoice_protect.docx", copy.OutputPath, StringComparison.OrdinalIgnoreCase);

            var originalText = ReadDocx(original);
            var protectedText = ReadDocx(copy.OutputPath!);
            Assert.Contains("ada@example.com", originalText, StringComparison.Ordinal);
            Assert.Contains("sk-testFAKEkey1234567890", originalText, StringComparison.Ordinal);
            Assert.Contains("[EMAIL]", protectedText, StringComparison.Ordinal);
            Assert.Contains("[API_KEY]", protectedText, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", protectedText, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-testFAKEkey1234567890", protectedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Txt_sibling_redacts_and_leaves_original_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-txt-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "notes.txt");
            File.WriteAllText(original, "See ada@example.com soon.");
            var before = Hashing.Sha256File(original);
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var copy = new ProtectService().MakeProtectedCopy(original, rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.Equal("See [EMAIL] soon.", File.ReadAllText(copy.OutputPath!).Trim());
            Assert.Equal("See ada@example.com soon.", File.ReadAllText(original));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_docx_returns_an_error_and_does_not_write_a_sibling()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-bad-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "broken.docx");
            File.WriteAllText(original, "this is not a zip");
            var before = Hashing.Sha256File(original);
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var copy = new ProtectService().MakeProtectedCopy(original, rules, "{name}_protect{ext}");
            Assert.False(string.IsNullOrEmpty(copy.Error));
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.False(File.Exists(Path.Combine(dir, "broken_protect.docx")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    internal static void WriteDocx(string path, string paragraph)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text(paragraph)))));
        main.Document.Save();
    }

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return string.Concat(doc.MainDocumentPart!.Document.Body!.Descendants<Text>().Select(t => t.Text));
    }
}
