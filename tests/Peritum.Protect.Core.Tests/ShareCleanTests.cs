using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Tests;

public class ShareCleanTests
{
    [Fact]
    public void Document_properties_are_removed()
    {
        var cleaned = Clean(Zip(("docProps/core.xml", """
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:creator>Secret Author</dc:creator>
            </cp:coreProperties>
            """)));
        Assert.Contains("Document properties", cleaned.Removed);
        Assert.DoesNotContain("Secret Author", Entry(cleaned, "docProps/core.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_properties_are_removed()
    {
        var cleaned = Clean(Zip(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties">
              <property name="Client"><vt:lpwstr xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">Hidden Name</vt:lpwstr></property>
            </Properties>
            """)));
        Assert.Contains("Custom properties", cleaned.Removed);
        Assert.DoesNotContain("Hidden Name", Entry(cleaned, "docProps/custom.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Thumbnail_is_removed()
    {
        var cleaned = Clean(Zip(("docProps/thumbnail.jpeg", "thumbnail bytes"), ("word/document.xml", "<w:document/>")));
        Assert.Contains("Thumbnail", cleaned.Removed);
        Assert.Null(Find(cleaned, "docProps/thumbnail.jpeg"));
    }

    [Fact]
    public void Comments_are_removed()
    {
        var cleaned = Clean(Zip(("word/comments.xml", "<w:comments>secret comment</w:comments>")));
        Assert.Contains("Comments", cleaned.Removed);
        Assert.Null(Find(cleaned, "word/comments.xml"));
    }

    [Fact]
    public void Tracked_changes_can_be_accepted_or_rejected()
    {
        var xml = """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:ins w:author="A"><w:r><w:t>kept insert</w:t></w:r></w:ins>
                <w:del w:author="A"><w:r><w:delText>gone delete</w:delText></w:r></w:del>
              </w:p></w:body>
            </w:document>
            """;
        var accepted = Clean(Zip(("word/document.xml", xml)), new CleanOptions { AcceptTrackedChanges = true });
        Assert.Contains("Tracked changes (accepted)", accepted.Removed);
        Assert.Contains("kept insert", Entry(accepted, "word/document.xml"), StringComparison.Ordinal);
        Assert.DoesNotContain("gone delete", Entry(accepted, "word/document.xml"), StringComparison.Ordinal);

        var rejected = Clean(Zip(("word/document.xml", xml)), new CleanOptions { AcceptTrackedChanges = false });
        Assert.Contains("Tracked changes (rejected)", rejected.Removed);
        Assert.DoesNotContain("kept insert", Entry(rejected, "word/document.xml"), StringComparison.Ordinal);
        Assert.Contains("gone delete", Entry(rejected, "word/document.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_text_is_removed()
    {
        var cleaned = Clean(Zip(("word/document.xml", """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p>
                <w:r><w:rPr><w:vanish/></w:rPr><w:t>vanish secret</w:t></w:r>
                <w:r><w:t>visible word</w:t></w:r>
              </w:p></w:body>
            </w:document>
            """)));
        var text = Entry(cleaned, "word/document.xml");
        Assert.Contains("Hidden text", cleaned.Removed);
        Assert.DoesNotContain("vanish secret", text, StringComparison.Ordinal);
        Assert.Contains("visible word", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_sheet_is_removed()
    {
        var cleaned = Clean(Zip(
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Visible" sheetId="1" r:id="rId1"/>
                    <sheet name="Secret" sheetId="2" state="hidden" r:id="rId2"/>
                  </sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://sheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://sheet" Target="worksheets/sheet2.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml", "<worksheet>keep</worksheet>"),
            ("xl/worksheets/sheet2.xml", "<worksheet>hidden sheet secret</worksheet>")));
        Assert.Contains("Hidden sheet", cleaned.Removed);
        Assert.Null(Find(cleaned, "xl/worksheets/sheet2.xml"));
        Assert.DoesNotContain("hidden", Entry(cleaned, "xl/workbook.xml"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("keep", Entry(cleaned, "xl/worksheets/sheet1.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Hidden_rows_and_columns_are_removed()
    {
        var cleaned = Clean(Zip(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cols><col min="2" max="2" hidden="1"/></cols>
              <sheetData>
                <row r="1"><c r="A1"><v>keep</v></c><c r="B1"><v>column secret</v></c></row>
                <row r="2" hidden="1"><c r="A2"><v>row secret</v></c></row>
              </sheetData>
            </worksheet>
            """)));
        var text = Entry(cleaned, "xl/worksheets/sheet1.xml");
        Assert.Contains("Hidden rows", cleaned.Removed);
        Assert.Contains("Hidden columns", cleaned.Removed);
        Assert.DoesNotContain("row secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("column secret", text, StringComparison.Ordinal);
        Assert.Contains("keep", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Off_slide_shapes_are_removed()
    {
        var cleaned = Clean(Zip(
            ("ppt/presentation.xml", """<p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:sldSz cx="9144000" cy="5143500"/></p:presentation>"""),
            ("ppt/slides/slide1.xml", """
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:sp><p:spPr><a:xfrm><a:off x="-2000000" y="0"/><a:ext cx="100" cy="100"/></a:xfrm></p:spPr><p:txBody><a:p><a:r><a:t>off slide secret</a:t></a:r></a:p></p:txBody></p:sp>
                  <p:sp><p:spPr><a:xfrm><a:off x="100" y="100"/><a:ext cx="100" cy="100"/></a:xfrm></p:spPr><p:txBody><a:p><a:r><a:t>on slide</a:t></a:r></a:p></p:txBody></p:sp>
                </p:sld>
                """)));
        var text = Entry(cleaned, "ppt/slides/slide1.xml");
        Assert.Contains("Off-slide shapes", cleaned.Removed);
        Assert.DoesNotContain("off slide secret", text, StringComparison.Ordinal);
        Assert.Contains("on slide", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Speaker_notes_are_optional()
    {
        var entries = new[]
        {
            ("ppt/notesSlides/notesSlide1.xml", "<p:notes>speaker secret</p:notes>"),
            ("ppt/slides/slide1.xml", "<p:sld>slide</p:sld>")
        };
        var removed = Clean(Zip(entries), new CleanOptions { RemoveSpeakerNotes = true });
        Assert.Contains("Speaker notes", removed.Removed);
        Assert.Null(Find(removed, "ppt/notesSlides/notesSlide1.xml"));

        var kept = Clean(Zip(entries), new CleanOptions { RemoveSpeakerNotes = false });
        Assert.DoesNotContain("Speaker notes", kept.Removed);
        Assert.Contains("speaker secret", Entry(kept, "ppt/notesSlides/notesSlide1.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Macros_become_a_macro_free_extension()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "Budget.docm");
            File.WriteAllBytes(path, Zip(
                ("word/vbaProject.bin", "macro bytes"),
                ("[Content_Types].xml", """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                      <Override PartName="/word/vbaProject.bin" ContentType="application/vnd.ms-office.vbaProject"/>
                      <Override PartName="/word/document.xml" ContentType="application/vnd.ms-word.document.macroEnabled.main+xml"/>
                    </Types>
                    """),
                ("word/document.xml", "<w:document>body</w:document>")));
            var before = Hashing.Sha256File(path);
            var copy = new ProtectService().MakeCleanCopy(path);
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(path));
            Assert.EndsWith("Budget_clean.docx", copy.OutputPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Macros", copy.Note, StringComparison.Ordinal);
            using var zip = ZipFile.OpenRead(copy.OutputPath!);
            Assert.Null(zip.GetEntry("word/vbaProject.bin"));
            var types = Read(zip, "[Content_Types].xml");
            Assert.DoesNotContain("macroEnabled", types, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void External_links_are_removed()
    {
        var cleaned = Clean(Zip(("xl/externalLinks/externalLink1.xml", "http://secret.example/data")));
        Assert.Contains("External links", cleaned.Removed);
        Assert.Null(Find(cleaned, "xl/externalLinks/externalLink1.xml"));
    }

    [Fact]
    public void Data_connections_are_removed()
    {
        var cleaned = Clean(Zip(("xl/connections.xml", "<connections>connection secret</connections>")));
        Assert.Contains("Data connections", cleaned.Removed);
        Assert.Null(Find(cleaned, "xl/connections.xml"));
    }

    [Fact]
    public void Embedded_objects_are_removed()
    {
        var cleaned = Clean(Zip(("word/embeddings/oleObject1.bin", "ole secret")));
        Assert.Contains("Embedded objects", cleaned.Removed);
        Assert.Null(Find(cleaned, "word/embeddings/oleObject1.bin"));
    }

    [Fact]
    public void Document_template_path_is_removed()
    {
        var cleaned = Clean(Zip(("word/settings.xml", """
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:attachedTemplate r:id="rId1"/>
            </w:settings>
            """)));
        Assert.Contains("Document template", cleaned.Removed);
        Assert.DoesNotContain("attachedTemplate", Entry(cleaned, "word/settings.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_info_is_removed_and_the_copy_is_text_only()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "note.pdf");
            File.WriteAllBytes(path, Pdf("Hello page", "Secret Author"));
            var cleaned = ShareCleaner.Clean(path);
            Assert.Contains("PDF document info", cleaned.Removed);
            Assert.DoesNotContain("Secret Author", Encoding.Latin1.GetString(cleaned.Bytes), StringComparison.Ordinal);
            var copy = Path.Combine(dir, "note_clean.pdf");
            File.WriteAllBytes(copy, cleaned.Bytes);
            var read = new PdfFileAdapter().Read(copy);
            Assert.Contains("Hello page", read.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Image_metadata_and_gps_are_removed()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "snap.jpg");
            using (var bitmap = new Bitmap(8, 8))
            {
                bitmap.SetPixel(0, 0, Color.White);
                bitmap.SetPropertyItem(Property(0x010F, Encoding.ASCII.GetBytes("SecretCam\0")));
                bitmap.SetPropertyItem(Property(0x8825, BitConverter.GetBytes(1)));
                bitmap.Save(path, ImageFormat.Jpeg);
            }

            var jpeg = File.ReadAllBytes(path);
            var marker = Encoding.ASCII.GetBytes("GPSLatitude");
            var com = new byte[4 + marker.Length];
            com[0] = 0xFF;
            com[1] = 0xFE;
            com[2] = 0;
            com[3] = (byte)(2 + marker.Length);
            marker.CopyTo(com, 4);
            var withGps = new byte[jpeg.Length + com.Length];
            withGps[0] = jpeg[0];
            withGps[1] = jpeg[1];
            com.CopyTo(withGps, 2);
            jpeg.AsSpan(2).CopyTo(withGps.AsSpan(2 + com.Length));
            File.WriteAllBytes(path, withGps);

            var before = Hashing.Sha256File(path);
            var copy = new ProtectService().MakeCleanCopy(path);
            Assert.True(copy.OriginalUnchanged, copy.Error);
            Assert.Equal(before, Hashing.Sha256File(path));
            Assert.Contains("Image metadata", copy.Note, StringComparison.Ordinal);
            Assert.Contains("GPS location", copy.Note, StringComparison.Ordinal);
            var output = File.ReadAllBytes(copy.OutputPath!);
            Assert.DoesNotContain("SecretCam", Encoding.Latin1.GetString(output), StringComparison.Ordinal);
            Assert.DoesNotContain("GPSLatitude", Encoding.Latin1.GetString(output), StringComparison.Ordinal);
            using var cleaned = new Bitmap(new MemoryStream(output));
            Assert.DoesNotContain(cleaned.PropertyItems, item => item.Id == 0x010F || item.Id == 0x8825);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Html_comments_metadata_and_hidden_text_are_removed()
    {
        var dir = Work();
        try
        {
            var path = Path.Combine(dir, "page.html");
            File.WriteAllText(path, """
                <html><head><meta name="author" content="Secret Author"></head>
                <body><p>Visible sentence</p><!-- secret comment --><span style="display:none">hidden sentence</span></body></html>
                """);
            var copy = new ProtectService().MakeCleanCopy(path);
            Assert.EndsWith("page_clean.html", copy.OutputPath, StringComparison.OrdinalIgnoreCase);
            var text = File.ReadAllText(copy.OutputPath!);
            Assert.Contains("Visible sentence", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret comment", text, StringComparison.Ordinal);
            Assert.DoesNotContain("hidden sentence", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret Author", text, StringComparison.Ordinal);
            Assert.Contains("HTML comments", copy.Note, StringComparison.Ordinal);
            Assert.Contains("Hidden HTML", copy.Note, StringComparison.Ordinal);
            Assert.Contains("HTML metadata", copy.Note, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static CleanPackage Clean(byte[] zip, CleanOptions? options = null)
    {
        var dir = Work();
        var path = Path.Combine(dir, "file.docx");
        File.WriteAllBytes(path, zip);
        var cleaned = ShareCleaner.Clean(path, options);
        Directory.Delete(dir, true);
        Assert.Null(cleaned.Error);
        return cleaned;
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static string Entry(CleanPackage package, string name)
    {
        var text = Find(package, name);
        Assert.NotNull(text);
        return text;
    }

    private static string? Find(CleanPackage package, string name)
    {
        using var stream = new MemoryStream(package.Bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry(name);
        if (entry is null)
            return null;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string Read(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static PropertyItem Property(int id, byte[] value)
    {
        var item = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
        item.Id = id;
        item.Type = 2;
        item.Value = value;
        item.Len = value.Length;
        return item;
    }

    private static byte[] Pdf(string pageText, string author)
    {
        var ascii = Encoding.ASCII;
        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        void Add(string body)
        {
            offsets.Add(ascii.GetByteCount(builder.ToString()));
            builder.Append(body);
        }

        var stream = "BT /F1 12 Tf 20 40 Td (" + pageText + ") Tj ET";
        Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Add("2 0 obj\n<< /Type /Pages /Count 1 /Kids [3 0 R] >>\nendobj\n");
        Add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");
        Add("4 0 obj\n<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream\nendobj\n");
        Add("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
        Add("6 0 obj\n<< /Author (" + author + ") /Title (Secret Title) >>\nendobj\n");
        var xref = ascii.GetByteCount(builder.ToString());
        builder.Append("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            builder.Append(offsets[i].ToString("D10") + " 00000 n \n");
        builder.Append("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R /Info 6 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        return ascii.GetBytes(builder.ToString());
    }

    private static string Work()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-clean-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
