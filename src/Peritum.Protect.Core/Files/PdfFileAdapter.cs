using System.Diagnostics;
using System.Text;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Util;

namespace Peritum.Protect.Core.Files;

public sealed class PdfFileAdapter : IFileAdapter
{
    public const string TextOnlyNote = "PDF copy is text-only in this version. Layout is not preserved.";
    public const string NoTextNote = "No text layer in this PDF. Image scan is in Settings.";

    public bool CanHandle(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Test hook. When set, used instead of the stopwatch.</summary>
    public Func<TimeSpan>? Elapsed { get; set; }

    private static DocumentRead Incomplete(string reason) => new() { Incomplete = true, Note = reason };

    public DocumentRead Read(string path)
    {
        var clock = Stopwatch.StartNew();
        var sb = new StringBuilder();
        var links = new StringBuilder();
        var segments = new List<TextSegment>();
        using (var document = PdfDocument.Open(path))
        {
            if (document.NumberOfPages > PackageLimits.MaxPdfPages)
                return Incomplete(PackageLimits.PdfTooManyPages);
            var pages = 0;
            foreach (var page in document.GetPages())
            {
                pages++;
                if (pages > PackageLimits.MaxPdfPages)
                    return Incomplete(PackageLimits.PdfTooManyPages);
                if ((Elapsed?.Invoke() ?? clock.Elapsed) > PackageLimits.PdfTimeBudget)
                    return Incomplete(PackageLimits.PdfTooSlow);
                AppendPage(sb, segments, page);
                foreach (var annotation in SafeAnnotations(page))
                {
                    if (!string.IsNullOrWhiteSpace(annotation.Content))
                        Append(sb, segments, annotation.Content, "page " + page.Number + ", annotation");
                    var uri = LinkTarget(annotation);
                    if (string.IsNullOrWhiteSpace(uri))
                        continue;
                    var visible = string.IsNullOrWhiteSpace(annotation.Content) ? page.Text : annotation.Content;
                    if (visible.Length > 200)
                        visible = "";
                    links.Append("<a href=\"").Append(uri).Append("\">").Append(visible.Trim()).AppendLine("</a>");
                }
            }

            AppendInfo(sb, segments, document);
            if (document.TryGetForm(out var form) && form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field is not AcroTextField textField || string.IsNullOrWhiteSpace(textField.Value))
                        continue;
                    var page = textField.PageNumber is int number && number > 0 ? "page " + number + ", " : "";
                    Append(sb, segments, textField.Value, page + "form field");
                }
            }
        }

        var text = sb.ToString();
        if (text.Trim().Length < 2)
            return DocumentRead.Fail(NoTextNote);

        return new DocumentRead
        {
            Text = text,
            Segments = segments,
            Note = TextOnlyNote,
            ExtraText = links.ToString(),
            WriteRedactedTextBeside = true
        };
    }

    private static void Append(StringBuilder sb, List<TextSegment> segments, string value, string location)
    {
        if (sb.Length > 0)
            sb.Append('\n');
        var start = sb.Length;
        sb.Append(value);
        segments.Add(new TextSegment { Start = start, Text = value, Place = SegmentPlace.Fixed(location) });
    }

    private static void AppendPage(StringBuilder sb, List<TextSegment> segments, Page page)
    {
        var groups = new Dictionary<string, List<Letter>>(StringComparer.Ordinal);
        var visible = new List<Letter>();
        var optional = OptionalLetters(page);
        foreach (var letter in page.Letters)
        {
            var reason = optional.Contains(letter) ? "hidden optional content" : Reason(letter, page);
            if (reason is null)
                visible.Add(letter);
            else if (!groups.TryGetValue(reason, out var list))
                groups[reason] = [letter];
            else
                list.Add(letter);
        }

        Append(sb, segments, Words(visible, page), "page " + page.Number);
        foreach (var pair in groups)
            Append(sb, segments, Words(pair.Value, page), "page " + page.Number + ", " + pair.Key);
    }

    private static HashSet<Letter> OptionalLetters(Page page)
    {
        var hidden = new HashSet<Letter>();
        try
        {
            foreach (var pair in page.GetOptionalContents())
            {
                foreach (var group in pair.Value)
                {
                    var marked = group.MarkedContent;
                    if (marked is null)
                        continue;
                    foreach (var letter in marked.Letters)
                        hidden.Add(letter);
                }
            }
        }
        catch
        {
            // Optional content that PdfPig cannot read stays with the other letter checks.
        }

        return hidden;
    }

    private static string? Reason(Letter letter, Page page)
    {
        if (letter.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip)
            return "invisible text";
        if (letter.FontSize > 0 && letter.FontSize < 2)
            return "text under 2 pt";
        if (NearWhite(letter.FillColor))
            return "white text";
        var box = page.CropBox.Bounds.Width > 1 ? page.CropBox.Bounds : page.MediaBox.Bounds;
        var point = letter.Location;
        if (point.X < box.Left - 1 || point.X > box.Right + 1 || point.Y < box.Bottom - 1 || point.Y > box.Top + 1)
            return "text outside the page";
        return null;
    }

    private static bool NearWhite(IColor? color) => color switch
    {
        RGBColor rgb => rgb.R >= 0.9 && rgb.G >= 0.9 && rgb.B >= 0.9,
        GrayColor gray => gray.Gray >= 0.9,
        _ => false
    };

    private static string Words(IReadOnlyList<Letter> letters, Page page)
    {
        if (letters.Count == 0)
            return "";
        if (letters.Count == page.Letters.Count)
        {
            var line = string.Join(" ", page.GetWords().Select(word => word.Text));
            return line.Length == 0 ? page.Text ?? "" : line;
        }

        return string.Join(" ", DefaultWordExtractor.Instance.GetWords(letters).Select(word => word.Text));
    }

    private static void AppendInfo(StringBuilder sb, List<TextSegment> segments, PdfDocument document)
    {
        var info = document.Information;
        var parts = new[] { info.Title, info.Author, info.Subject, info.Keywords, info.Creator }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var text = string.Join(" ", parts);
        if (text.Length > 0)
            Append(sb, segments, text, "document info");
        try
        {
            if (document.TryGetXmpMetadata(out var xmp) && xmp is not null)
            {
                var xml = System.Text.Encoding.UTF8.GetString(xmp.GetXmlBytes());
                var plain = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
                if (plain.Length > 0)
                    Append(sb, segments, plain, "document metadata");
            }
        }
        catch
        {
            // A metadata stream that cannot be read is skipped.
        }
    }

    private static string? LinkTarget(UglyToad.PdfPig.Annotations.Annotation annotation)
    {
        if (annotation.Action is UglyToad.PdfPig.Actions.UriAction uri)
            return uri.Uri;
        return null;
    }

    private static IEnumerable<UglyToad.PdfPig.Annotations.Annotation> SafeAnnotations(Page page)
    {
        try
        {
            return page.GetAnnotations().ToList();
        }
        catch
        {
            return [];
        }
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var mode = redaction ?? CopyRedaction.Permanent;
        var redacted = mode.WithMapLine(mode.Apply(read.Text, findings));
        var bytes = SimplePdfWriter.FromText(redacted, out var fontNote);
        if (!string.IsNullOrEmpty(fontNote))
            read.Note = string.IsNullOrEmpty(read.Note) ? fontNote : read.Note + " " + fontNote;
        return bytes;
    }
}
