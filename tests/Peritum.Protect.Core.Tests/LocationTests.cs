using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;
using A = DocumentFormat.OpenXml.Drawing;

namespace Peritum.Protect.Core.Tests;

public class LocationTests
{
    private const string Email = "ada@example.com";
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Theory]
    [InlineData("note.txt")]
    [InlineData("note.md")]
    [InlineData("note.json")]
    [InlineData("note.xml")]
    [InlineData("note.log")]
    public void Text_location_is_line_and_column_and_context_masks_the_value(string name)
    {
        var body = "hello\nplease send the invoice to ada@example.com before Friday\n";
        using var dir = new WorkDir();
        var path = dir.Write(name, body);
        var finding = EmailFinding(Scan(path));
        Assert.Equal("line 2, column 28", finding.Location);
        Assert.Contains("a***@example.com", finding.Context, StringComparison.Ordinal);
        Assert.Contains("invoice", finding.Context, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Csv_location_names_the_row_and_the_column_header()
    {
        using var dir = new WorkDir();
        var path = dir.Write("people.csv", "Name,Email\nAda,ada@example.com\n");
        var finding = EmailFinding(Scan(path));
        Assert.Equal("row 2, column Email", finding.Location);
        Assert.DoesNotContain(Email, finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Clipboard_location_is_the_line_in_the_copied_text()
    {
        var session = new ProtectService().Scan(ScanRequest.ForText("hello\nada@example.com", ScanSource.Clipboard), Rules);
        var finding = EmailFinding(session);
        Assert.Equal("line 2", finding.Location);
        Assert.DoesNotContain(Email, finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Word_locations_name_the_section_table_header_comment_note_and_text_box()
    {
        using var dir = new WorkDir();
        var path = Path.Combine(dir.Path, "terms.docx");
        WriteWord(path);
        var findings = Scan(path).Result.Findings;
        Assert.Equal("Section 'Payment terms', paragraph 2", LocationOf(findings, Email));
        Assert.Equal("Section 'Payment terms', table 1, row 1, column 2", LocationOf(findings, "bob@example.com"));
        Assert.Equal("header, paragraph 1", LocationOf(findings, "cara@example.com"));
        Assert.Equal("comment, paragraph 1", LocationOf(findings, "dan@example.com"));
        Assert.Equal("footnote, paragraph 1", LocationOf(findings, "eve@example.com"));
        Assert.Equal("endnote, paragraph 1", LocationOf(findings, "frank@example.com"));
        Assert.Equal("Section 'Payment terms', tracked change, paragraph 1", LocationOf(findings, "gina@example.com"));
        Assert.Equal("Section 'Payment terms', text box, paragraph 1", LocationOf(findings, "hal@example.com"));
        foreach (var finding in findings)
            Assert.DoesNotContain(SnippetValue(finding), finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Excel_locations_name_the_cell_header_comment_and_hidden_sheet()
    {
        using var dir = new WorkDir();
        var path = Path.Combine(dir.Path, "Customers.xlsx");
        WriteExcel(path);
        var findings = Scan(path).Result.Findings;
        Assert.Equal("Customers!C14 (Email)", LocationOf(findings, Email));
        Assert.Equal("Customers!A1, comment", LocationOf(findings, "cara@example.com"));
        Assert.Equal("Customers, header", LocationOf(findings, "dan@example.com"));
        Assert.Equal("Customers, footer", LocationOf(findings, "eve@example.com"));
        Assert.Equal("Secret!B3, hidden sheet", LocationOf(findings, "bob@example.com"));
        foreach (var finding in findings)
            Assert.DoesNotContain(SnippetValue(finding), finding.Context, StringComparison.Ordinal);

        var copy = new ProtectService().MakeProtectedCopy(path, Rules, "{name}_protect{ext}");
        Assert.True(copy.OriginalUnchanged, copy.Error);
        Assert.DoesNotContain(Email, ReadCells(copy.OutputPath!), StringComparison.Ordinal);
    }

    [Fact]
    public void PowerPoint_locations_name_the_slide_part()
    {
        using var dir = new WorkDir();
        var path = Path.Combine(dir.Path, "deck.pptx");
        WriteDeck(path);
        var findings = Scan(path).Result.Findings;
        Assert.Equal("Slide 1, title", LocationOf(findings, Email));
        Assert.Equal("Slide 1, body", LocationOf(findings, "bob@example.com"));
        Assert.Equal("Slide 1, table", LocationOf(findings, "cara@example.com"));
        Assert.Equal("Slide 1, notes", LocationOf(findings, "dan@example.com"));
    }

    [Fact]
    public void Pdf_location_is_the_page()
    {
        using var dir = new WorkDir();
        var path = Path.Combine(dir.Path, "letter.pdf");
        File.WriteAllBytes(path, SimplePdfWriter.FromText("Contact ada@example.com today."));
        var finding = EmailFinding(Scan(path));
        Assert.Equal("page 1", finding.Location);
        Assert.DoesNotContain(Email, finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_location_names_the_area()
    {
        using var dir = new WorkDir();
        var path = Path.Combine(dir.Path, "card.png");
        using (var bitmap = new System.Drawing.Bitmap(80, 40))
        {
            bitmap.SetPixel(0, 0, System.Drawing.Color.White);
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        var ocr = new FakeOcr(Email, new OcrBox { Start = 0, Length = Email.Length, X = 4, Y = 4, Width = 40, Height = 16 });
        var session = new ProtectService(ocr).Scan(ScanRequest.ForFile(path), Rules);
        var finding = EmailFinding(session);
        Assert.Equal("text in image, top left", finding.Location);
        Assert.DoesNotContain(Email, finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_location_names_the_header_and_the_attachment()
    {
        using var dir = new WorkDir();
        var sheet = Path.Combine(dir.Path, "Q3.xlsx");
        WriteExcel(sheet);
        var raw = Normalize("""
            From: Ada Lovelace <ada@example.com>
            To: Desk
            Subject: Q3
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="BOUND"

            --BOUND
            Content-Type: text/plain; charset=utf-8

            Please review the sheet.
            --BOUND
            Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
            Content-Disposition: attachment; filename="Q3.xlsx"
            Content-Transfer-Encoding: base64

            """) + "\r\n" + Convert.ToBase64String(File.ReadAllBytes(sheet), Base64FormattingOptions.InsertLineBreaks) + "\r\n--BOUND--\r\n";
        var path = dir.Write("mail.eml", raw);
        var session = Scan(path);
        Assert.True(session.Result.Error is null, session.Result.Error + " " + session.Result.Note);
        var findings = session.Result.Findings;
        Assert.Contains(findings, finding => finding.Location == "From" && finding.Snippet.Contains(Email, StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Location == "Attachment 'Q3.xlsx' → Customers!C14 (Email)");
        foreach (var finding in findings)
            Assert.DoesNotContain(finding.Snippet.TrimEnd('…'), finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Zip_location_names_the_entry_and_the_inner_place()
    {
        using var dir = new WorkDir();
        var sheet = Path.Combine(dir.Path, "inside.xlsx");
        WriteExcel(sheet);
        var path = Path.Combine(dir.Path, "pack.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(sheet, "Q3.xlsx");
            var note = archive.CreateEntry("notes/q.txt");
            using var writer = new StreamWriter(note.Open(), new UTF8Encoding(false));
            writer.Write("zoe@example.com");
        }

        var findings = Scan(path).Result.Findings;
        Assert.Contains(findings, finding => finding.Location == "Q3.xlsx → Customers!C14 (Email)");
        Assert.Contains(findings, finding => finding.Location == "Q3.xlsx → Secret!B3, hidden sheet");
        Assert.Equal("notes/q.txt → line 1, column 1", LocationOf(findings, "zoe@example.com"));
        foreach (var finding in findings)
            Assert.DoesNotContain(finding.Snippet.TrimEnd('…'), finding.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Location_and_context_are_not_written_to_the_data_folder()
    {
        using var dir = new WorkDir();
        var path = dir.Write("people.csv", "Name,Email\nAda,ada@example.com\n");
        var session = Scan(path);
        var finding = EmailFinding(session);
        Assert.False(string.IsNullOrEmpty(finding.Location));
        Assert.False(string.IsNullOrEmpty(finding.Context));

        var data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        var log = new EventLog(Path.Combine(data, "events.jsonl"));
        log.Append(log.FromScan(session.Result, EventActions.Warn, null, path));
        new SettingsStore(Path.Combine(data, "settings.json")).Save(AppSettings.CreateDefault());

        var blob = new StringBuilder();
        foreach (var file in Directory.GetFiles(data, "*", SearchOption.AllDirectories))
            blob.Append(Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        var written = blob.ToString();
        Assert.DoesNotContain(Email, written, StringComparison.Ordinal);
        Assert.DoesNotContain(finding.Location, written, StringComparison.Ordinal);
        Assert.DoesNotContain(finding.Context, written, StringComparison.Ordinal);
    }

    private static string Normalize(string raw)
    {
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Split('\n');
        return string.Join("\r\n", lines.Select(line => line.Trim())) + "\r\n";
    }

    private static ScanSession Scan(string path) =>
        new ProtectService().Scan(ScanRequest.ForFile(path), Rules);

    private static Finding EmailFinding(ScanSession session)
    {
        Assert.Null(session.Result.Error);
        return Assert.Single(session.Result.Findings, finding => finding.RuleId == "pii.email" && finding.Snippet.Contains(Email, StringComparison.Ordinal));
    }

    private static string LocationOf(IReadOnlyList<Finding> findings, string value)
    {
        var matches = findings.Where(item => item.Snippet.Contains(value, StringComparison.Ordinal)).ToList();
        Assert.True(matches.Count == 1, "looking for " + value + " in " + string.Join(" | ", findings.Select(item => item.Snippet + " => " + item.Location)));
        var finding = matches[0];
        Assert.DoesNotContain(value, finding.Context, StringComparison.Ordinal);
        return finding.Location;
    }

    private static string SnippetValue(Finding finding) => finding.Snippet;

    private static string ReadCells(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        return doc.WorkbookPart!.WorksheetParts
            .Select(part => part.Worksheet.InnerText + (part.WorksheetCommentsPart?.Comments?.InnerText ?? "") + part.Worksheet.InnerXml)
            .Aggregate("", (left, right) => left + "\n" + right);
    }

    private static void WriteWord(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new W.Body();
        body.Append(Heading("Payment terms"));
        body.Append(Paragraph(Email));
        body.Append(new W.Table(
            new W.TableRow(
                new W.TableCell(Paragraph("Ada")),
                new W.TableCell(Paragraph("bob@example.com")))));
        body.Append(new W.Paragraph(new W.DeletedRun(new W.Run(new W.DeletedText("gina@example.com")))));

        var headerPart = main.AddNewPart<HeaderPart>();
        headerPart.Header = new W.Header(Paragraph("cara@example.com"));
        headerPart.Header.Save();
        body.Append(new W.SectionProperties(new W.HeaderReference
        {
            Type = W.HeaderFooterValues.Default,
            Id = main.GetIdOfPart(headerPart)
        }));
        main.Document = new W.Document(body);
        main.Document.Save();
        var box = "<w:p><w:r><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\"><v:textbox><w:txbxContent><w:p><w:r><w:t>hal@example.com</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></w:r></w:p>";
        main.Document.Body!.InnerXml += box;
        main.Document.Save();

        var comments = main.AddNewPart<WordprocessingCommentsPart>();
        comments.Comments = new W.Comments(new W.Comment(Paragraph("dan@example.com"))
        {
            Author = "A",
            Id = "1",
            Date = DateTime.UtcNow
        });
        comments.Comments.Save();

        var footnotes = main.AddNewPart<FootnotesPart>();
        footnotes.Footnotes = new W.Footnotes(new W.Footnote(Paragraph("eve@example.com")) { Id = 1 });
        footnotes.Footnotes.Save();

        var endnotes = main.AddNewPart<EndnotesPart>();
        endnotes.Endnotes = new W.Endnotes(new W.Endnote(Paragraph("frank@example.com")) { Id = 1 });
        endnotes.Endnotes.Save();
    }

    private static W.Paragraph Heading(string text) => new(
        new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
        new W.Run(new W.Text(text)));

    private static W.Paragraph Paragraph(string text) =>
        new(new W.Run(new W.Text(text)));

    private static void WriteExcel(string path)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new S.Workbook();
        var sheets = workbook.Workbook.AppendChild(new S.Sheets());
        var customers = workbook.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        data.Append(Row(1, ("A1", "Name"), ("C1", "Email")));
        data.Append(Row(14, ("C14", Email)));
        var worksheet = new S.Worksheet(data);
        worksheet.Append(new S.HeaderFooter
        {
            OddHeader = new S.OddHeader { Text = "dan@example.com" },
            OddFooter = new S.OddFooter { Text = "eve@example.com" }
        });
        customers.Worksheet = worksheet;
        var comments = customers.AddNewPart<WorksheetCommentsPart>();
        comments.Comments = new S.Comments(
            new S.Authors(new S.Author("Peritum")),
            new S.CommentList(new S.Comment
            {
                Reference = "A1",
                AuthorId = 0,
                CommentText = new S.CommentText(new S.Run(
                    new S.Text("cara@example.com")))
            }));
        comments.Comments.Save();
        customers.Worksheet.Save();
        sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(customers), SheetId = 1, Name = "Customers" });

        var secret = workbook.AddNewPart<WorksheetPart>();
        secret.Worksheet = new S.Worksheet(new S.SheetData(Row(3, ("B3", "bob@example.com"))));
        secret.Worksheet.Save();
        sheets.Append(new S.Sheet
        {
            Id = workbook.GetIdOfPart(secret),
            SheetId = 2,
            Name = "Secret",
            State = S.SheetStateValues.Hidden
        });
        workbook.Workbook.Save();
    }

    private static S.Row Row(uint index, params (string Reference, string Text)[] cells)
    {
        var row = new S.Row { RowIndex = index };
        foreach (var (reference, text) in cells)
        {
            row.Append(new S.Cell
            {
                CellReference = reference,
                DataType = S.CellValues.InlineString,
                InlineString = new S.InlineString(new S.Text(text))
            });
        }

        return row;
    }

    private static void WriteDeck(string path)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new Presentation(new SlideIdList());
        var slidePart = presentation.AddNewPart<SlidePart>();
        slidePart.Slide = new Slide(new CommonSlideData(new ShapeTree(
            GroupProps(),
            Shape(2, "Title", Email, PlaceholderValues.Title),
            Shape(3, "Body", "bob@example.com", null),
            TableShape("cara@example.com"))), new ColorMapOverride(new A.MasterColorMapping()));
        slidePart.Slide.Save();
        var notes = slidePart.AddNewPart<NotesSlidePart>();
        notes.NotesSlide = new NotesSlide(new CommonSlideData(new ShapeTree(
            GroupProps(),
            Shape(2, "Notes", "dan@example.com", null))), new ColorMapOverride(new A.MasterColorMapping()));
        notes.NotesSlide.Save();
        presentation.Presentation.SlideIdList!.Append(new SlideId
        {
            Id = 256,
            RelationshipId = presentation.GetIdOfPart(slidePart)
        });
        presentation.Presentation.Save();
    }

    private static NonVisualGroupShapeProperties GroupProps() => new(
        new NonVisualDrawingProperties { Id = 1, Name = "" },
        new NonVisualGroupShapeDrawingProperties(),
        new ApplicationNonVisualDrawingProperties());

    private static Shape Shape(uint id, string name, string text, PlaceholderValues? placeholder)
    {
        var props = new ApplicationNonVisualDrawingProperties();
        if (placeholder is PlaceholderValues kind)
            props.Append(new PlaceholderShape { Type = kind });
        return new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = name },
                new NonVisualShapeDrawingProperties(),
                props),
            new ShapeProperties(),
            new TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(text)))));
    }

    private static GraphicFrame TableShape(string text) => new(
        new NonVisualGraphicFrameProperties(
            new NonVisualDrawingProperties { Id = 4, Name = "Table" },
            new NonVisualGraphicFrameDrawingProperties(),
            new ApplicationNonVisualDrawingProperties()),
        new Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 }),
        new A.Graphic(new A.GraphicData(
            new A.Table(
                new A.TableProperties(),
                new A.TableGrid(new A.GridColumn { Width = 100 }),
                new A.TableRow(
                    new A.TableCell(
                        new A.TextBody(
                            new A.BodyProperties(),
                            new A.Paragraph(new A.Run(new A.Text(text))))),
                    new A.ExtensionList())
                { Height = 100 }))
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" }));

    private sealed class FakeOcr(string text, OcrBox box) : IOcrEngine
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public OcrDocument? Recognize(string path) => new() { Text = text, Words = [box] };
    }

    private sealed class WorkDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "peritum-loc-" + Guid.NewGuid().ToString("n"));

        public WorkDir() => Directory.CreateDirectory(Path);

        public string Write(string name, string text)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
