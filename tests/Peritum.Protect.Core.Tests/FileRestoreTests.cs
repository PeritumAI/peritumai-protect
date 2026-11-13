using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Tests;

public class FileRestoreTests
{
    private const string Email = "ada@example.com";
    private const string Secret = "sk-testFAKEkey1234567890";

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".docx")]
    public void Round_trip_edits_the_text_and_writes_a_new_file(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-restore-" + Guid.NewGuid().ToString("n"));
        var maps = Path.Combine(dir, "maps");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "notes" + extension);
            Write(source, "See " + Email + " and " + Email.ToUpperInvariant() + " key " + Secret + ".");
            var before = Hashing.Sha256File(source);
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var copy = new ProtectService().MakeProtectedCopy(
                source, rules, "{name}_reversible{ext}", reversible: true, mapsDirectory: maps);
            Assert.True(copy.OriginalUnchanged, copy.Error);

            var tokenText = FileRestore.ReadText(copy.OutputPath!);
            Assert.Contains("[[EMAIL_1]]", tokenText, StringComparison.Ordinal);
            var editedText = tokenText.Replace("[[EMAIL_1]]", "\\[\\[email\\_1\\]\\]", StringComparison.Ordinal)
                + "\nSee [EMAIL_9] too.";
            var edited = Path.Combine(dir, "edited" + extension);
            Write(edited, editedText);
            var editedBefore = Hashing.Sha256File(edited);

            var store = new MapStore(maps);
            var mangled = TokenRestorer.Restore("[[ EMAIL_1 ]] and [email_1]", store.Load(copy.MapId!)!.Tokens);
            Assert.Contains(Email, mangled.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[[", mangled.Text, StringComparison.Ordinal);

            var restored = FileRestore.RestoreFile(edited, store, TimeSpan.FromDays(30));
            Assert.True(restored.Succeeded, restored.Message);
            Assert.NotEqual(Path.GetFullPath(edited), Path.GetFullPath(restored.OutputPath!));
            Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(restored.OutputPath!));
            Assert.Equal(editedBefore, Hashing.Sha256File(edited));
            Assert.Equal(before, Hashing.Sha256File(source));
            var text = FileRestore.ReadText(restored.OutputPath!);
            Assert.Contains(Email, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
            Assert.Contains(ReversibleRedactor.RemovedSecret, text, StringComparison.Ordinal);
            Assert.Contains("1 token was not in the map", restored.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("..", restored.Message, StringComparison.Ordinal);
            Assert.Contains("[[EMAIL_9]]", string.Join(" ", restored.Details), StringComparison.Ordinal);

            LocalData.DeleteMaps(dir);
            Assert.Empty(Directory.GetFiles(maps));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Message_uses_singular_and_plural_for_zero_one_and_two()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-restore-words-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new MapStore(dir);
            var map = new RedactionMap { Id = "ABCD-EFGH" };
            map.Tokens["[[EMAIL_1]]"] = Email;
            map.Tokens["[[EMAIL_2]]"] = "bob@example.com";
            store.Save(map);
            var age = TimeSpan.FromDays(30);

            var missingTwo = FileRestore.RestoreText("Peritum map: ABCD-EFGH", store, age);
            Assert.Equal("No tokens found. 2 tokens were not in the answer.", missingTwo.Message);
            Assert.Equal("Not in the answer: [[EMAIL_1]], [[EMAIL_2]]", missingTwo.Details[0]);

            var missingOne = FileRestore.RestoreText("[[EMAIL_1]]\nPeritum map: ABCD-EFGH", store, age);
            Assert.Equal("1 value restored. 1 token was not in the answer.", missingOne.Message);
            Assert.Equal("Not in the answer: [[EMAIL_2]]", missingOne.Details[0]);

            var none = FileRestore.RestoreText("[[EMAIL_1]] [[EMAIL_2]]\nPeritum map: ABCD-EFGH", store, age);
            Assert.Equal("2 values restored.", none.Message);
            Assert.Empty(none.Details);

            var inventedOne = FileRestore.RestoreText("[[EMAIL_1]] [[PHONE_4]]\nPeritum map: ABCD-EFGH", store, age);
            Assert.Equal("1 value restored. 1 token was not in the answer. 1 token was not in the map.", inventedOne.Message);
            Assert.Contains("[[PHONE_4]]", string.Join(" ", inventedOne.Details), StringComparison.Ordinal);
            Assert.DoesNotContain("..", inventedOne.Message, StringComparison.Ordinal);

            var inventedTwo = FileRestore.RestoreText("[[EMAIL_1]] [[EMAIL_2]] [[PHONE_4]] [[IBAN_8]]\nPeritum map: ABCD-EFGH", store, age);
            Assert.Equal("2 values restored. 2 tokens were not in the map.", inventedTwo.Message);
            Assert.Equal("Not in the map: [[PHONE_4]], [[IBAN_8]]", inventedTwo.Details[0]);
            Assert.DoesNotContain("..", inventedTwo.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Expired_and_missing_maps_do_not_restore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-restore-age-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new MapStore(dir);
            var map = new RedactionMap { Id = "ABCD-EFGH" };
            map.Tokens["[[EMAIL_1]]"] = Email;
            store.Save(map, DateTimeOffset.UtcNow.AddDays(-40));
            var expired = FileRestore.RestoreText("Hello [[EMAIL_1]]\nPeritum map: ABCD-EFGH", store, TimeSpan.FromDays(30));
            Assert.False(expired.Succeeded);
            Assert.Equal(FileRestore.Expired, expired.Message);
            Assert.Null(expired.OutputPath);

            var missing = FileRestore.RestoreText("Hello [[EMAIL_1]]\nPeritum map: ZZZZ-9999", store, TimeSpan.FromDays(30));
            Assert.False(missing.Succeeded);
            Assert.Equal(FileRestore.Missing, missing.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Write(string path, string text)
    {
        if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, text);
            return;
        }

        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            body.AppendChild(new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
        main.Document = new Document(body);
        main.Document.Save();
    }
}
