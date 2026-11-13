using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap8FileLinkTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void An_email_reports_a_mismatched_link_a_lookalike_sender_and_a_wrapped_trick()
    {
        var raw = """
            From: PayPal Support <billing@secure-login.example>
            Reply-To: other@elsewhere.example
            To: Ada <ada@example.com>
            Subject: Invoice
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>Please <a href="https://secure-login.example/signin">www.paypal.com</a> review this.</p>
            <p><a href="https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpaypa1.example%2Fsignin">Open</a></p>
            """;
        var session = Scan(raw, "mail.eml");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.text_mismatch");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.sender_mismatch");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.lookalike");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.reply_to_differs");
        Assert.DoesNotContain(session.Result.Findings, finding => finding.Category == "links");
    }

    [Fact]
    public void A_normal_newsletter_has_no_link_warnings()
    {
        var raw = """
            From: News <news@example.com>
            To: Ada <ada@example.com>
            Subject: Hello
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>Read <a href="https://www.example.com/story">www.example.com</a> today.</p>
            """;
        var session = Scan(raw, "news.eml");
        Assert.Empty(session.Result.LinkFindings);
        Assert.True(session.Result.LinkCount >= 1);
    }

    [Fact]
    public void A_word_file_and_a_pdf_report_one_mismatched_link_each()
    {
        using var dir = NewTemp();
        var docx = Path.Combine(dir.Root, "note.docx");
        WriteDocx(docx, "https://secure-login.example/signin", "www.paypal.com");
        var pdf = Path.Combine(dir.Root, "note.pdf");
        File.WriteAllBytes(pdf, PdfWithLink("https://secure-login.example/signin", "www.paypal.com"));

        var word = new ProtectService().Scan(ScanRequest.ForFile(docx), Rules);
        var document = new ProtectService().Scan(ScanRequest.ForFile(pdf), Rules);
        Assert.Contains(word.Result.LinkFindings, finding => finding.RuleId == "link.text_mismatch");
        Assert.Contains(document.Result.LinkFindings, finding => finding.RuleId == "link.text_mismatch");
    }

    [Fact]
    public void Ordinary_links_in_an_html_email_do_not_warn()
    {
        var body = new StringBuilder();
        body.Append("<html><body>");
        foreach (var url in OrdinaryLinks().Take(300))
            body.Append("<a href=\"").Append(url).Append("\">read</a> ");
        body.Append("</body></html>");
        var raw = "From: News <news@example.com>\r\nTo: Ada <ada@example.com>\r\nSubject: Digest\r\nMIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\n\r\n" + body;
        var session = Scan(raw, "digest.eml");
        Assert.Empty(session.Result.LinkFindings);
    }

    [Fact]
    public void A_protected_copy_keeps_the_link()
    {
        using var dir = NewTemp();
        var path = Path.Combine(dir.Root, "page.html");
        File.WriteAllText(path, "<a href=\"https://secure-login.example/signin\">www.paypal.com</a>");
        var copy = new ProtectService().MakeProtectedCopy(path, Rules, "{name}_protect{ext}");
        Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.Contains("https://secure-login.example/signin", text, StringComparison.Ordinal);
        Assert.Contains("www.paypal.com", text, StringComparison.Ordinal);
    }

    private static ScanSession Scan(string raw, string name)
    {
        using var dir = NewTemp();
        var path = Path.Combine(dir.Root, name);
        File.WriteAllText(path, raw.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        var session = new ProtectService().Scan(ScanRequest.ForFile(path), Rules);
        Assert.True(string.IsNullOrEmpty(session.Result.Error), session.Result.Error);
        return session;
    }

    private static void WriteDocx(string path, string href, string visible)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text(visible)))));
        var rel = main.AddHyperlinkRelationship(new Uri(href, UriKind.Absolute), true);
        var hyperlink = new Hyperlink(new Run(new Text(visible))) { Id = rel.Id };
        main.Document.Body!.AppendChild(new Paragraph(hyperlink));
        main.Document.Save();
    }

    private static byte[] PdfWithLink(string href, string visible)
    {
        var text = "BT /F1 12 Tf 72 720 Td (" + visible + ") Tj ET";
        var annot = "<< /Type /Annot /Subtype /Link /Rect [72 700 240 730] /A << /Type /Action /S /URI /URI (" + href + ") >> >>";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Annots [5 0 R] /Resources << /Font << /F1 6 0 R >> >> >>",
            "<< /Length " + text.Length + " >>\nstream\n" + text + "\nendstream",
            annot,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var body in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append(offset.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer << /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static IEnumerable<string> OrdinaryLinks()
    {
        yield return "https://login.microsoftonline.com/common/oauth2/authorize";
        yield return "https://accounts.google.com/ServiceLogin";
        yield return "https://docs.google.com/document/d/abc/edit";
        yield return "https://www.paypal.com/signin";
        yield return "https://www.amazon.co.uk/";
        yield return "https://github.com/dotnet/runtime";
        yield return "https://en.wikipedia.org/wiki/PayPal";
        yield return "https://www.bbc.co.uk/news/technology-paypal-outage";
        var n = 0;
        while (n < 320)
        {
            n++;
            yield return "https://news.example/story/" + n + "?utm_source=newsletter";
        }
    }

    private static Temp NewTemp() => new();

    private sealed class Temp : IDisposable
    {
        public Temp()
        {
            Root = Path.Combine(Path.GetTempPath(), "peritum-gap8-links-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
