using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;
using X = DocumentFormat.OpenXml.Spreadsheet;
using P = DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace Peritum.Protect.Core.Tests;

public class AiBundleRoundTripTests
{
    [Fact]
    public void Docx_xlsx_pptx_and_pdf_round_trip_through_a_bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rules = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).ToList();
            var maps = new MapStore(Path.Combine(root, "maps"));
            foreach (var (name, write) in new (string Name, Action<string> Write)[]
            {
                ("note.docx", path => WriteDocx(path, "Contact ada@example.com today.")),
                ("sheet.xlsx", path => WriteXlsx(path, "ada@example.com")),
                ("deck.pptx", path => WritePptx(path, "ada@example.com")),
                ("page.pdf", path => File.WriteAllBytes(path, SimplePdfWriter.FromText("ada@example.com")))
            })
            {
                var source = Path.Combine(root, name);
                write(source);
                var exported = AiBundle.ExportFile(source, rules, _ => false, maps);
                Assert.True(exported.Succeeded, name + " " + exported.Message);
                var bundle = File.ReadAllText(exported.OutputPath!);
                Assert.StartsWith(AiBundle.Header, bundle, StringComparison.Ordinal);
                Assert.Contains("[[EMAIL_1]]", bundle, StringComparison.Ordinal);
                Assert.DoesNotContain("ada@example.com", bundle, StringComparison.Ordinal);
                if (name.EndsWith(".pdf", StringComparison.Ordinal))
                    Assert.Contains("## Page 1", bundle, StringComparison.Ordinal);

                var answer = bundle.Replace("[[EMAIL_1]]", "[[email_1]]", StringComparison.Ordinal) + "\nThe assistant added this sentence.";
                var docx = Path.Combine(root, Path.GetFileNameWithoutExtension(name) + "-answer.docx");
                var imported = AiBundle.ImportAnswer(answer, maps, TimeSpan.FromDays(30), docx);
                Assert.True(imported.Succeeded, name + " " + imported.Message);
                Assert.Contains("new document from the AI's answer", imported.Message, StringComparison.OrdinalIgnoreCase);
                using var doc = WordprocessingDocument.Open(docx, false);
                var text = doc.MainDocumentPart!.Document.Body!.InnerText;
                Assert.Contains("ada@example.com", text, StringComparison.Ordinal);
                Assert.Contains("assistant added", text, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Repeated_value_in_one_paragraph_table_cell_or_heading_uses_one_token()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-bundle-repeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rules = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).ToList();
            var maps = new MapStore(Path.Combine(root, "maps"));
            var line = "write to ada@example.com or reply to ada@example.com";
            var email = "ada@example.com";
            foreach (var (name, write) in new (string Name, Action<string> Write)[]
            {
                ("paragraph.docx", path => WriteBody(path, ParagraphOf(line))),
                ("cell.docx", path => WriteBody(path, TableCellOf(email, email))),
                ("heading.docx", path => WriteBody(path, HeadingOf(email, email)))
            })
            {
                var source = Path.Combine(root, name);
                write(source);
                var exported = AiBundle.ExportFile(source, rules, _ => false, maps);
                Assert.True(exported.Succeeded, name + " " + exported.Message);
                var bundle = File.ReadAllText(exported.OutputPath!);
                Assert.Contains("[[EMAIL_1]]", bundle, StringComparison.Ordinal);
                Assert.DoesNotContain("[[EMAIL_2]]", bundle, StringComparison.Ordinal);
                Assert.Equal(2, Count(bundle, "[[EMAIL_1]]"));
                Assert.DoesNotContain(email, bundle, StringComparison.Ordinal);
                Assert.DoesNotContain("ada@", bundle, StringComparison.Ordinal);
                Assert.DoesNotContain("example.com", bundle, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Tokens_are_numbered_by_first_appearance_and_old_maps_still_restore()
    {
        var text = "bob@example.com then ada@example.com";
        var findings = new RuleScanner().Find(text, BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).ToList());
        var (output, map) = ReversibleRedactor.Apply(text, findings, reversible: true);
        Assert.Equal("[[EMAIL_1]] then [[EMAIL_2]]", output);
        Assert.Equal("bob@example.com", map.Tokens["[[EMAIL_1]]"]);
        Assert.Equal("ada@example.com", map.Tokens["[[EMAIL_2]]"]);

        var old = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["[[EMAIL_1]]"] = "ada@example.com",
            ["[[EMAIL_2]]"] = "bob@example.com"
        };
        var restored = TokenRestorer.Restore("[[EMAIL_1]] then [[EMAIL_2]]", old);
        Assert.Equal("ada@example.com then bob@example.com", restored.Text);

        var root = Path.Combine(Path.GetTempPath(), "peritum-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "notes.txt");
            File.WriteAllText(source, text);
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var copy = new ProtectService().MakeProtectedCopy(source, rules, "{name}_reversible{ext}", reversible: true, mapsDirectory: Path.Combine(root, "maps"));
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Contains("[[EMAIL_1]] then [[EMAIL_2]]", File.ReadAllText(copy.OutputPath!), StringComparison.Ordinal);

            var session = RichClipboard.Inspect(new ClipboardScanInput { Text = text }, rules, ocr: null);
            var write = RichClipboard.Redact(session, session.Result.Findings, reversible: true);
            Assert.Null(write.Error);
            Assert.StartsWith("[[EMAIL_1]] then [[EMAIL_2]]", write.PlainText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static void WriteBody(string path, OpenXmlElement block)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(block));
        main.Document.Save();
    }

    private static Paragraph ParagraphOf(string text) =>
        new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph HeadingOf(string first, string second) =>
        new(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
            new Run(
                new Text(first) { Space = SpaceProcessingModeValues.Preserve },
                new Break(),
                new Text(second) { Space = SpaceProcessingModeValues.Preserve }));

    private static Table TableCellOf(string first, string second) =>
        new(
            new TableRow(
                new TableCell(
                    new Paragraph(new Run(new Text(first) { Space = SpaceProcessingModeValues.Preserve })),
                    new Paragraph(new Run(new Text(second) { Space = SpaceProcessingModeValues.Preserve })))));

    private static void WriteDocx(string path, string text)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text("Hello") { Space = SpaceProcessingModeValues.Preserve })),
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
        sheets.Append(new X.Sheet { Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = "Clients" });
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
