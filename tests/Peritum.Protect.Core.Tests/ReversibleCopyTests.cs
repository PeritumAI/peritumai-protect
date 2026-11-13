using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using X = DocumentFormat.OpenXml.Spreadsheet;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace Peritum.Protect.Core.Tests;

public class ReversibleCopyTests
{
    private const string Email = "ada@example.com";
    private const string Secret = "sk-testFAKEkey1234567890";

    [Fact]
    public void Reversible_copies_number_tokens_and_do_not_store_secrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-rev-" + Guid.NewGuid().ToString("n"));
        var maps = Path.Combine(dir, "maps");
        Directory.CreateDirectory(dir);
        try
        {
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var service = new ProtectService();
            foreach (var (name, write) in Samples())
            {
                var original = Path.Combine(dir, name);
                write(original, Email + " and " + Email.ToUpperInvariant() + " " + Secret);
                var before = Hashing.Sha256File(original);
                var copy = service.MakeProtectedCopy(
                    original,
                    rules,
                    "{name}_reversible{ext}",
                    reversible: true,
                    mapsDirectory: maps);

                Assert.True(copy.OriginalUnchanged, name + " " + copy.Error);
                Assert.Equal(before, Hashing.Sha256File(original));
                Assert.True(copy.Reversible);
                Assert.False(string.IsNullOrEmpty(copy.MapId));
                var text = ReadCopy(copy.OutputPath!);
                Assert.Contains("[[EMAIL_1]]", text, StringComparison.Ordinal);
                Assert.DoesNotContain("[[EMAIL_2]]", text, StringComparison.Ordinal);
                Assert.Contains(ReversibleRedactor.RemovedSecret, text, StringComparison.Ordinal);
                Assert.Contains("Peritum map: " + copy.MapId, text, StringComparison.Ordinal);
                Assert.DoesNotContain(Email, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);

                var rawMap = File.ReadAllBytes(Path.Combine(maps, copy.MapId + ".map"));
                Assert.False(Contains(rawMap, Email));
                Assert.False(Contains(rawMap, Secret));
                var loaded = new MapStore(maps).Load(copy.MapId!);
                Assert.Contains(loaded!.Tokens.Values, value => value.Equals(Email, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(loaded.Tokens.Values, value => value.Contains(Secret, StringComparison.Ordinal));
                Assert.Empty(Directory.GetFiles(dir, "*.map"));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Permanent_copy_still_uses_bracket_placeholders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-perm-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "notes.txt");
            File.WriteAllText(original, "See " + Email + " soon.");
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var copy = new ProtectService().MakeProtectedCopy(original, rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.False(copy.Reversible);
            Assert.Equal("See [EMAIL] soon.", File.ReadAllText(copy.OutputPath!).Trim());
            Assert.DoesNotContain("Peritum map:", File.ReadAllText(copy.OutputPath!), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static IEnumerable<(string Name, Action<string, string> Write)> Samples()
    {
        yield return ("notes.txt", (path, text) => File.WriteAllText(path, text));
        yield return ("notes.md", (path, text) => File.WriteAllText(path, text));
        yield return ("notes.docx", WriteDocx);
        yield return ("notes.xlsx", WriteXlsx);
        yield return ("notes.pptx", WritePptx);
    }

    private static string ReadCopy(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllText(path);
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            if (entry.Length > 2_000_000)
                continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            sb.Append(reader.ReadToEnd());
        }
        return sb.ToString();
    }

    private static bool Contains(byte[] haystack, string needle)
    {
        var bytes = Encoding.UTF8.GetBytes(needle);
        for (var i = 0; i <= haystack.Length - bytes.Length; i++)
        {
            var same = true;
            for (var j = 0; j < bytes.Length; j++)
            {
                if (haystack[i + j] != bytes[j])
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return true;
        }
        return false;
    }

    private static void WriteDocx(string path, string text)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
        main.Document.Save();
    }

    private static void WriteXlsx(string path, string text)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new X.Workbook();
        var sheets = workbook.Workbook.AppendChild(new X.Sheets());
        var sheetPart = workbook.AddNewPart<WorksheetPart>();
        var row = new X.Row { RowIndex = 1 };
        row.Append(new X.Cell
        {
            CellReference = "A1",
            DataType = X.CellValues.InlineString,
            InlineString = new X.InlineString(new X.Text(text))
        });
        sheetPart.Worksheet = new X.Worksheet(new X.SheetData(row));
        sheets.Append(new X.Sheet
        {
            Id = workbook.GetIdOfPart(sheetPart),
            SheetId = 1,
            Name = "Sheet1"
        });
        workbook.Workbook.Save();
    }

    private static void WritePptx(string path, string text)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new P.Presentation(new P.SlideIdList());
        var slidePart = presentation.AddNewPart<SlidePart>("rId1");
        slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()),
            new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 2, Name = "Title" },
                    new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(),
                new P.TextBody(
                    new A.BodyProperties(),
                    new A.ListStyle(),
                    new A.Paragraph(new A.Run(new A.Text(text))))))));
        presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = 256, RelationshipId = "rId1" });
        presentation.Presentation.Save();
    }
}
