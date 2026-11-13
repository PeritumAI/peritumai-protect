using System.Text;
using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Tests;

public class PdfHiddenTests
{
    [Fact]
    public void Invisible_render_mode_is_named_and_removed() =>
        AssertHidden("invisible secret", "invisible text", "BT /F1 12 Tf 3 Tr 20 80 Td (invisible secret) Tj 0 Tr 20 40 Td (visible word) Tj ET");

    [Fact]
    public void White_text_is_named_and_removed() =>
        AssertHidden("white secret", "white text", "BT /F1 12 Tf 1 1 1 rg 20 80 Td (white secret) Tj 0 0 0 rg 20 40 Td (visible word) Tj ET");

    [Fact]
    public void Tiny_text_is_named_and_removed() =>
        AssertHidden("tiny secret", "text under 2 pt", "BT /F1 1 Tf 20 80 Td (tiny secret) Tj /F1 12 Tf 20 40 Td (visible word) Tj ET");

    [Fact]
    public void Text_outside_the_page_is_named_and_removed() =>
        AssertHidden("outside secret", "text outside the page", "BT /F1 12 Tf 400 400 Td (outside secret) Tj ET BT /F1 12 Tf 20 40 Td (visible word) Tj ET");

    [Fact]
    public void Annotation_text_is_named_and_removed()
    {
        var pdf = Build(new PdfSpec
        {
            Content = "BT /F1 12 Tf 20 40 Td (visible word) Tj ET",
            PageExtra = "/Annots [6 0 R]",
            Objects = ["<< /Type /Annot /Subtype /Text /Contents (annot secret) /Rect [10 10 30 30] >>"]
        });
        AssertHiddenPdf(pdf, "annot secret", "annotation", "visible word");
    }

    [Fact]
    public void Form_field_text_is_named_and_removed()
    {
        var pdf = Build(new PdfSpec
        {
            Content = "BT /F1 12 Tf 20 40 Td (visible word) Tj ET",
            CatalogExtra = "/AcroForm << /Fields [6 0 R] >>",
            PageExtra = "/Annots [6 0 R]",
            Objects = ["<< /FT /Tx /T (Name) /V (form secret) /Subtype /Widget /Rect [0 0 40 16] >>"]
        });
        AssertHiddenPdf(pdf, "form secret", "form field", "visible word");
    }

    [Fact]
    public void Document_info_text_is_named_and_removed()
    {
        var pdf = Build(new PdfSpec
        {
            Content = "BT /F1 12 Tf 20 40 Td (visible word) Tj ET",
            Info = "<< /Author (meta secret) /Title (Secret Title) >>"
        });
        AssertHiddenPdf(pdf, "meta secret", "document info", "visible word");
    }

    [Fact]
    public void Xmp_metadata_text_is_named_and_removed()
    {
        var xmp = """<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?><title>xmp secret</title><?xpacket end="w"?>""";
        var pdf = Build(new PdfSpec
        {
            Content = "BT /F1 12 Tf 20 40 Td (visible word) Tj ET",
            CatalogExtra = "/Metadata 6 0 R",
            Streams = [( "<< /Type /Metadata /Subtype /XML", xmp )]
        });
        AssertHiddenPdf(pdf, "xmp secret", "document metadata", "visible word");
    }

    [Fact]
    public void Hidden_optional_content_is_named_and_removed()
    {
        var pdf = Build(new PdfSpec
        {
            Content = "/OC /OC1 BDC BT /F1 12 Tf 20 80 Td (oc secret) Tj ET EMC BT /F1 12 Tf 20 40 Td (visible word) Tj ET",
            CatalogExtra = "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            ResourcesExtra = "/Properties << /OC1 6 0 R >>",
            Objects = ["<< /Type /OCG /Name (HiddenLayer) >>"]
        });
        AssertHiddenPdf(pdf, "oc secret", "hidden optional content", "visible word");
    }

    private static void AssertHidden(string secret, string reason, string content)
    {
        var pdf = Build(new PdfSpec { Content = content });
        AssertHiddenPdf(pdf, secret, reason, "visible word");
    }

    private static void AssertHiddenPdf(byte[] pdf, string secret, string reason, string visible)
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-pdf-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "page.pdf");
            File.WriteAllBytes(path, pdf);
            var read = new PdfFileAdapter().Read(path);
            Assert.Null(read.Error);
            Assert.Contains(read.Segments, segment =>
                segment.Text.Contains(secret, StringComparison.Ordinal)
                && (segment.Place?.Label ?? "").Contains(reason, StringComparison.Ordinal));
            var cleaned = ShareCleaner.Clean(path);
            Assert.Null(cleaned.Error);
            var copy = Path.Combine(dir, "page_clean.pdf");
            File.WriteAllBytes(copy, cleaned.Bytes);
            var again = new PdfFileAdapter().Read(copy);
            Assert.DoesNotContain(secret, again.Text, StringComparison.Ordinal);
            Assert.Contains(visible, again.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class PdfSpec
    {
        public string Content { get; init; } = "";
        public string CatalogExtra { get; init; } = "";
        public string PageExtra { get; init; } = "";
        public string ResourcesExtra { get; init; } = "";
        public string Info { get; init; } = "";
        public List<string> Objects { get; init; } = [];
        public List<(string Header, string Body)> Streams { get; init; } = [];
    }

    private static byte[] Build(PdfSpec spec)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R " + spec.CatalogExtra + " >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> " + spec.ResourcesExtra + " >> " + spec.PageExtra + " >>",
            "",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        objects.AddRange(spec.Objects);
        var streamObjects = new List<(int Index, string Header, string Body)>();
        foreach (var stream in spec.Streams)
        {
            objects.Add("");
            streamObjects.Add((objects.Count, stream.Header, stream.Body));
        }

        if (spec.Info.Length > 0)
            objects.Add(spec.Info);

        var contentIndex = 4;
        var ascii = Encoding.ASCII;
        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ascii.GetByteCount(builder.ToString()));
            var number = i + 1;
            if (number == contentIndex)
            {
                builder.Append(number + " 0 obj\n<< /Length " + spec.Content.Length + " >>\nstream\n" + spec.Content + "\nendstream\nendobj\n");
                continue;
            }

            var stream = streamObjects.FirstOrDefault(item => item.Index == number);
            if (stream.Index == number)
            {
                builder.Append(number + " 0 obj\n" + stream.Header + " /Length " + stream.Body.Length + " >>\nstream\n" + stream.Body + "\nendstream\nendobj\n");
                continue;
            }

            builder.Append(number + " 0 obj\n" + objects[i] + "\nendobj\n");
        }

        var info = spec.Info.Length > 0 ? " /Info " + objects.Count + " 0 R" : "";
        var xref = ascii.GetByteCount(builder.ToString());
        builder.Append("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            builder.Append(offsets[i].ToString("D10") + " 00000 n \n");
        builder.Append("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R" + info + " >>\nstartxref\n" + xref + "\n%%EOF\n");
        return ascii.GetBytes(builder.ToString());
    }
}
