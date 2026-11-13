using System.Drawing;
using System.Drawing.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;
using A = DocumentFormat.OpenXml.Drawing;

namespace Peritum.Protect.Core.Tests;

public class OfficeAndPdfTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void Xlsx_copy_redacts_cells_and_keeps_the_original_hash()
    {
        var dir = TempDir();
        try
        {
            var original = Path.Combine(dir, "Invoice.xlsx");
            WriteXlsx(original, "ada@example.com");
            var before = Hashing.Sha256File(original);
            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.EndsWith("Invoice_protect.xlsx", copy.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ada@example.com", ReadXlsx(original), StringComparison.Ordinal);
            Assert.Contains("[EMAIL]", ReadXlsx(copy.OutputPath!), StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", ReadXlsx(copy.OutputPath!), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Xlsx_copy_redacts_a_referenced_shared_string_only()
    {
        var dir = TempDir();
        try
        {
            var original = Path.Combine(dir, "Shared.xlsx");
            WriteSharedStringXlsx(original, "ada@example.com", "sk-unusedFAKE1234567890");

            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");

            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Contains("[EMAIL]", ReadXlsx(copy.OutputPath!), StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", ReadXlsx(copy.OutputPath!), StringComparison.Ordinal);
            Assert.DoesNotContain("[API_KEY]", ReadXlsx(copy.OutputPath!), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Csv_copy_keeps_commas_and_the_original()
    {
        var dir = TempDir();
        try
        {
            var original = Path.Combine(dir, "people.csv");
            File.WriteAllText(original, "name,email\nAda,ada@example.com\n");
            var before = Hashing.Sha256File(original);
            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(original));
            var text = File.ReadAllText(copy.OutputPath!);
            Assert.Contains("name,email", text, StringComparison.Ordinal);
            Assert.Contains("[EMAIL]", text, StringComparison.Ordinal);
            Assert.Equal("name,email\nAda,ada@example.com\n", File.ReadAllText(original));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pptx_copy_redacts_slide_text()
    {
        var dir = TempDir();
        try
        {
            var original = Path.Combine(dir, "Deck.pptx");
            WritePptx(original, "ada@example.com");
            var before = Hashing.Sha256File(original);
            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.Contains("[EMAIL]", ReadPptx(copy.OutputPath!), StringComparison.Ordinal);
            Assert.Contains("ada@example.com", ReadPptx(original), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pdf_text_copy_is_a_new_file_and_a_txt_sibling()
    {
        var dir = TempDir();
        try
        {
            var original = Path.Combine(dir, "Letter.pdf");
            File.WriteAllBytes(original, SimplePdfWriter.FromText("Contact ada@example.com today."));
            var before = Hashing.Sha256File(original);
            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.EndsWith("Letter_protect.pdf", copy.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(copy.SidecarPath);
            Assert.Contains("[EMAIL]", File.ReadAllText(copy.SidecarPath!), StringComparison.Ordinal);
            Assert.Contains("text-only", copy.Note, StringComparison.OrdinalIgnoreCase);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(copy.OutputPath!);
            var text = string.Join(" ", pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
            Assert.Contains("[EMAIL]", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Image_sidecar_contains_counts_not_detected_values()
    {
        var dir = TempDir();
        try
        {
            var original = Path.Combine(dir, "card.png");
            using (var bitmap = new Bitmap(80, 40))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.White);
                bitmap.Save(original, ImageFormat.Png);
            }
            var before = Hashing.Sha256File(original);
            var ocr = new FakeOcr("ada@example.com", new OcrBox { Start = 0, Length = "ada@example.com".Length, X = 4, Y = 4, Width = 40, Height = 16 });
            var copy = new ProtectService(ocr).MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(original));
            Assert.EndsWith("card_protect.png", copy.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(before, Hashing.Sha256File(copy.OutputPath!));
            Assert.NotNull(copy.SidecarPath);
            var sidecar = File.ReadAllText(copy.SidecarPath!);
            Assert.Contains("Email ×1", sidecar, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", sidecar, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-office-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteXlsx(string path, string cellText)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var sheets = workbook.Workbook.AppendChild(new Sheets());
        var sheetPart = workbook.AddNewPart<WorksheetPart>();
        var row = new Row();
        row.Append(new Cell
        {
            CellReference = "A1",
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(cellText))
        });
        sheetPart.Worksheet = new Worksheet(new SheetData(row));
        sheets.Append(new Sheet
        {
            Id = workbook.GetIdOfPart(sheetPart),
            SheetId = 1,
            Name = "Sheet1"
        });
        workbook.Workbook.Save();
    }

    private static void WriteSharedStringXlsx(string path, string used, string unused)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var strings = workbook.AddNewPart<SharedStringTablePart>();
        strings.SharedStringTable = new SharedStringTable(
            new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(used)),
            new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(unused)));
        var sheetPart = workbook.AddNewPart<WorksheetPart>();
        sheetPart.Worksheet = new Worksheet(new SheetData(
            new Row(new Cell
            {
                CellReference = "A1",
                DataType = CellValues.SharedString,
                CellValue = new CellValue("0")
            })));
        workbook.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbook.GetIdOfPart(sheetPart),
            SheetId = 1,
            Name = "Sheet1"
        }));
        workbook.Workbook.Save();
    }

    private static string ReadXlsx(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        return string.Join(" ", doc.WorkbookPart!.WorksheetParts
            .SelectMany(part => part.Worksheet.Descendants<Cell>())
            .Select(cell => cell.InlineString?.InnerText ?? cell.CellValue?.InnerText ?? ""));
    }

    private static void WritePptx(string path, string slideText)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new Presentation(new SlideIdList());
        var slidePart = presentation.AddNewPart<SlidePart>();
        slidePart.Slide = new Slide(
            new CommonSlideData(
                new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(),
                    new Shape(
                        new NonVisualShapeProperties(
                            new NonVisualDrawingProperties { Id = 2, Name = "Text" },
                            new NonVisualShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new ShapeProperties(),
                        new TextBody(
                            new A.BodyProperties(),
                            new A.ListStyle(),
                            new A.Paragraph(new A.Run(new A.Text(slideText))))))),
            new ColorMapOverride(new A.MasterColorMapping()));
        var slideId = new SlideId { Id = 256, RelationshipId = presentation.GetIdOfPart(slidePart) };
        presentation.Presentation.SlideIdList!.Append(slideId);
        presentation.Presentation.Save();
    }

    private static string ReadPptx(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        return string.Join(" ", doc.PresentationPart!.SlideParts
            .SelectMany(part => part.Slide.Descendants<A.Text>())
            .Select(text => text.Text));
    }

    private sealed class FakeOcr : IOcrEngine
    {
        private readonly OcrDocument _document;

        public FakeOcr(string text, OcrBox box)
        {
            _document = new OcrDocument { Text = text, Words = [box] };
        }

        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public OcrDocument? Recognize(string path) => _document;
    }
}

public class AllowListAndRegexTests
{
    [Fact]
    public void Allow_list_roundtrip_removes_a_value()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-allow-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new AllowListStore(Path.Combine(dir, "allowlist.json"));
            store.Add("ada@example.com");
            var again = new AllowListStore(Path.Combine(dir, "allowlist.json"));
            Assert.True(again.Contains("ada@example.com"));
            again.Remove("ada@example.com");
            var third = new AllowListStore(Path.Combine(dir, "allowlist.json"));
            Assert.False(third.Contains("ada@example.com"));
            Assert.DoesNotContain("sk-", File.ReadAllText(Path.Combine(dir, "allowlist.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Regex_term_hits_a_matter_number()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-regex-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "user.json");
            UserDictionary.Save(path, [new UserTerm { Label = "MatterNumbers", Text = @"MAT-\d{4}", Kind = "regex" }]);
            var loaded = UserDictionary.Load(path);
            Assert.Equal("regex", Assert.Single(loaded).Kind);
            var rules = RulePackLoader.SelectActive([RulePackLoader.LoadFile(path)], [], []);
            var findings = new RuleScanner().Find("File MAT-2048 is open.", rules);
            var hit = Assert.Single(findings);
            Assert.Equal("MAT-2048", hit.Snippet);
            Assert.Equal("[CUSTOM:MatterNumbers]", Redaction.TextRedactor.Apply("File MAT-2048 is open.", findings).Replace("File ", "").Replace(" is open.", ""));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
