using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Peritum.Protect.Core.Files;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Peritum.Protect.Core.Tests;

public class Gap3bCleanTests
{
    private const string Secret = "HIDDENSHEETDATA";

    [Fact]
    public void Hidden_sheet_text_is_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "book.xlsx");
            WriteHiddenWorkbook(path, S.SheetStateValues.Hidden, Secret);
            AssertClean(path, ".xlsx", Secret, "Hidden sheet");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Very_hidden_sheet_text_is_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "book.xlsx");
            WriteHiddenWorkbook(path, S.SheetStateValues.VeryHidden, Secret);
            AssertClean(path, ".xlsx", Secret, "Hidden sheet");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_defined_name_and_chart_do_not_keep_the_hidden_sheet()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "book.xlsx");
            WriteHiddenWorkbook(path, S.SheetStateValues.Hidden, Secret, withNameAndChart: true);
            AssertClean(path, ".xlsx", Secret, "Hidden sheet");
            var copy = LastCopy(dir);
            var workbook = Entry(copy, "xl/workbook.xml");
            Assert.DoesNotContain("Secret!", workbook, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("localSheetId=\"1\"", workbook, StringComparison.OrdinalIgnoreCase);
            foreach (var (name, text) in Entries(copy))
            {
                if (name.Contains("chart", StringComparison.OrdinalIgnoreCase) || name.Contains("pivot", StringComparison.OrdinalIgnoreCase))
                    Assert.Fail(name + " still holds hidden-sheet content: " + text);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Comment_text_is_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "note.docx");
            WriteComments(path, "COMMENTSECRET");
            AssertClean(path, ".docx", "COMMENTSECRET", "Comments");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Vba_bytes_are_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "Budget.docm");
            WriteVba(path, "VBASECRET");
            AssertClean(path, ".docx", "VBASECRET", "Macros");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Custom_xml_text_is_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "note.docx");
            WriteCustomXml(path, "CUSTOMXMLSECRET");
            AssertClean(path, ".docx", "CUSTOMXMLSECRET", "Custom XML");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Hidden_slide_text_is_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "deck.pptx");
            WriteHiddenSlide(path, "HIDDENSILDESECRET");
            AssertClean(path, ".pptx", "HIDDENSILDESECRET", "Hidden slide");
            var copy = LastCopy(dir);
            Assert.Contains("keep visible", Entry(copy, "ppt/slides/slide1.xml"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Thumbnail_bytes_are_absent_from_every_part_and_the_file_validates()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "note.docx");
            WriteThumbnail(path, "THUMBNAILSECRET");
            AssertClean(path, ".docx", "THUMBNAILSECRET", "Thumbnail");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void AssertClean(string path, string extension, string secret, string removed)
    {
        var before = Hashing.Sha256File(path);
        var copy = new ProtectService().MakeCleanCopy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        Assert.Equal(before, Hashing.Sha256File(path));
        Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
        Assert.Contains(removed, copy.Note ?? "", StringComparison.Ordinal);
        Assert.True(File.Exists(copy.OutputPath));
        foreach (var (name, text) in Entries(copy.OutputPath!))
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        var output = File.ReadAllBytes(copy.OutputPath!);
        Assert.Null(OfficePackageValidator.Check(output, extension, File.ReadAllBytes(path)));
        using var stream = new MemoryStream(output);
        using var document = extension switch
        {
            ".xlsx" => (OpenXmlPackage)SpreadsheetDocument.Open(stream, false),
            ".docx" => WordprocessingDocument.Open(stream, false),
            ".pptx" => PresentationDocument.Open(stream, false),
            _ => throw new InvalidOperationException(extension)
        };
        Assert.NotNull(document);
    }

    private static void WriteHiddenWorkbook(string path, S.SheetStateValues state, string secret, bool withNameAndChart = false)
    {
        using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook();
            var strings = workbook.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new S.SharedStringTable(
                new S.SharedStringItem(new S.Text(secret)));
            strings.SharedStringTable.Save();

            var sheets = workbook.Workbook.AppendChild(new S.Sheets());
            var visible = workbook.AddNewPart<WorksheetPart>();
            visible.Worksheet = new S.Worksheet(new S.SheetData(InlineRow("A1", "keep visible")));
            visible.Worksheet.Save();
            sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(visible), SheetId = 1, Name = "Visible" });

            var hidden = workbook.AddNewPart<WorksheetPart>();
            hidden.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    new S.Cell
                    {
                        CellReference = "A1",
                        DataType = S.CellValues.SharedString,
                        CellValue = new S.CellValue("0")
                    },
                    new S.Cell
                    {
                        CellReference = "B1",
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text(secret))
                    })));
            hidden.Worksheet.Save();
            sheets.Append(new S.Sheet
            {
                Id = workbook.GetIdOfPart(hidden),
                SheetId = 2,
                Name = "Secret",
                State = state
            });

            if (withNameAndChart)
            {
                workbook.Workbook.Append(new S.DefinedNames(
                    new S.DefinedName { Name = "SecretCell", Text = "Secret!$A$1" },
                    new S.DefinedName { Name = "Scoped", Text = "$A$1", LocalSheetId = 1U }));
            }

            workbook.Workbook.Save();
        }

        RewriteSheetTarget(path);
        if (withNameAndChart)
        {
            AddEntry(path, "xl/charts/chart1.xml", ChartXml(secret));
            AddEntry(path, "xl/pivotCache/pivotCacheDefinition1.xml", """
                <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <cacheSource type="worksheet"><worksheetSource ref="A1" sheet="Secret"/></cacheSource>
                </pivotCacheDefinition>
                """);
            AddEntry(path, "xl/pivotCache/pivotCacheRecords1.xml",
                "<pivotCacheRecords xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><r><s v=\"" + secret + "\"/></r></pivotCacheRecords>");
        }
    }

    private static void RewriteSheetTarget(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        Assert.NotNull(rels);
        string text;
        using (var reader = new StreamReader(rels.Open()))
            text = reader.ReadToEnd();
        text = text.Replace("Target=\"worksheets/", "Target=\"xl/worksheets/", StringComparison.Ordinal);
        rels.Delete();
        var created = zip.CreateEntry("xl/_rels/workbook.xml.rels");
        using var writer = new StreamWriter(created.Open());
        writer.Write(text);
    }

    private static void WriteComments(string path, string secret)
    {
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Visible sentence")))));
            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new W.Comments(new W.Comment(
                new W.Paragraph(new W.Run(new W.Text(secret))))
            {
                Author = "A",
                Id = "1",
                Date = DateTime.UtcNow
            });
            comments.Comments.Save();
            main.Document.Save();
        }

        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.CreateEntry("word/threadedComments/threadedComment1.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("<comment>" + secret + "</comment>");
    }

    private static void WriteVba(string path, string secret)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.MacroEnabledDocument);
        var main = doc.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Visible sentence")))));
        var vba = main.AddNewPart<VbaProjectPart>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(secret));
        vba.FeedData(stream);
        main.Document.Save();
    }

    private static void WriteCustomXml(string path, string secret)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Visible sentence")))));
        var custom = main.AddCustomXmlPart(CustomXmlPartType.CustomXml);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<data>" + secret + "</data>"));
        custom.FeedData(stream);
        main.Document.Save();
    }

    private static void WriteHiddenSlide(string path, string secret)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new Presentation(new SlideIdList());
        var visible = presentation.AddNewPart<SlidePart>();
        visible.Slide = SlideWithText("keep visible");
        visible.Slide.Save();
        var hidden = presentation.AddNewPart<SlidePart>();
        hidden.Slide = SlideWithText(secret);
        hidden.Slide.Save();
        presentation.Presentation.SlideIdList!.Append(new SlideId { Id = 256, RelationshipId = presentation.GetIdOfPart(visible) });
        var hiddenId = new SlideId { Id = 257, RelationshipId = presentation.GetIdOfPart(hidden) };
        hiddenId.SetAttribute(new OpenXmlAttribute("show", "", "0"));
        presentation.Presentation.SlideIdList.Append(hiddenId);
        presentation.Presentation.Save();
    }

    private static Slide SlideWithText(string text) => new(
        new CommonSlideData(new ShapeTree(
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
                new TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(text))))))),
        new ColorMapOverride(new A.MasterColorMapping()));

    private static void WriteThumbnail(string path, string secret)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Visible sentence")))));
        var thumb = doc.AddThumbnailPart(ThumbnailPartType.Jpeg);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(secret));
        thumb.FeedData(stream);
        main.Document.Save();
    }

    private static S.Row InlineRow(string reference, string text) => new(new S.Cell
    {
        CellReference = reference,
        DataType = S.CellValues.InlineString,
        InlineString = new S.InlineString(new S.Text(text))
    });

    private static string ChartXml(string secret) => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart>
            <c:title><c:tx><c:rich><a:p><a:r><a:t>TITLE</a:t></a:r></a:p></c:rich></c:tx></c:title>
            <c:plotArea>
              <c:barChart>
                <c:barDir val="col"/>
                <c:ser>
                  <c:idx val="0"/>
                  <c:order val="0"/>
                  <c:tx><c:v>TITLE</c:v></c:tx>
                  <c:val>
                    <c:numRef>
                      <c:f>Secret!$A$1</c:f>
                      <c:numCache><c:ptCount val="1"/><c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache>
                    </c:numRef>
                  </c:val>
                </c:ser>
                <c:axId val="1"/>
                <c:axId val="2"/>
              </c:barChart>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        </c:chartSpace>
        """.Replace("TITLE", secret, StringComparison.Ordinal);

    private static void AddEntry(string path, string name, string text)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static string LastCopy(string dir) =>
        Directory.GetFiles(dir, "*_clean.*").Single();

    private static string Entry(string path, string name)
    {
        var found = Entries(path).SingleOrDefault(pair => pair.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrEmpty(found.Name), name);
        return found.Text;
    }

    private static List<(string Name, string Text)> Entries(string path)
    {
        var list = new List<(string, string)>();
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            using var reader = entry.Open();
            using var copy = new MemoryStream();
            reader.CopyTo(copy);
            list.Add((entry.FullName.Replace('\\', '/'), Encoding.UTF8.GetString(copy.ToArray())));
        }

        return list;
    }

    private static string Work()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-gap3b-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
