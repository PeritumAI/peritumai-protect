using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap8cLinkCountTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void A_word_file_with_two_hyperlinks_counts_two()
    {
        using var dir = Temp();
        var path = Path.Combine(dir.Root, "note.docx");
        WriteDocx(path,
            ("https://shop.example/one", "https://shop.example/one"),
            ("https://shop.example/two", "Details"));
        var session = new ProtectService().Scan(ScanRequest.ForFile(path), Rules);
        Assert.True(string.IsNullOrEmpty(session.Result.Error), session.Result.Error);
        Assert.Equal(2, session.Result.LinkCount);
    }

    [Fact]
    public void An_email_with_two_links_and_an_image_counts_two()
    {
        var raw = """
            From: News <news@shop.example>
            To: Ada <ada@example.com>
            Subject: Hello
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <html><head><link rel="stylesheet" href="https://cdn.example/mail.css"></head>
            <body>
            <a href="https://shop.example/one">https://shop.example/one</a>
            <a href="https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fshop.example%2Ftwo">Details</a>
            <img src="https://cdn.example/pixel.png" alt="">
            </body></html>
            """;
        using var dir = Temp();
        var path = dir.Write("mail.eml", raw);
        var session = new ProtectService().Scan(ScanRequest.ForFile(path), Rules);
        Assert.True(string.IsNullOrEmpty(session.Result.Error), session.Result.Error);
        Assert.Equal(2, session.Result.LinkCount);
    }

    private static void WriteDocx(string path, params (string Href, string Visible)[] links)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        foreach (var (href, visible) in links)
        {
            var rel = main.AddHyperlinkRelationship(new Uri(href, UriKind.Absolute), true);
            main.Document.Body!.AppendChild(new Paragraph(new Hyperlink(new Run(new Text(visible))) { Id = rel.Id }));
        }

        main.Document.Save();
    }

    private static Work Temp() => new();

    private sealed class Work : IDisposable
    {
        public Work()
        {
            Root = Path.Combine(Path.GetTempPath(), "peritum-gap8c-count-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string name, string raw)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
