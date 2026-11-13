using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class DocxFileAdapter : IFileAdapter, IPackageFile
{
    public bool CanHandle(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase);

    public DocumentRead Read(string path) => Read(path, new PackageLimits.Budget(), 1);

    public DocumentRead Read(string path, PackageLimits.Budget budget, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            return new DocumentRead { Incomplete = true, Note = PackageLimits.NestedTooDeep };
        var blocked = PackageLimits.Blocked(path, budget, depth);
        if (blocked is not null)
            return blocked;
        // Read into memory so a bad package cannot keep the original file locked.
        using var ms = new MemoryStream(File.ReadAllBytes(path), writable: false);
        using var doc = WordprocessingDocument.Open(ms, false);
        return ReadOpen(doc);
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "peritum-protect-" + Guid.NewGuid().ToString("n") + ".docx");
        try
        {
            File.Copy(originalPath, temp, overwrite: false);
            var mode = redaction ?? CopyRedaction.Permanent;
            using (var doc = WordprocessingDocument.Open(temp, true))
            {
                var paragraphs = EnumerateParagraphs(doc).ToList();
                if (paragraphs.Count != read.Segments.Count)
                    throw new InvalidDataException("Could not read this file. Original was not changed.");

                foreach (var deleted in doc.MainDocumentPart?.Document?.Descendants<DeletedRun>().ToList() ?? [])
                    deleted.Remove();
                paragraphs = EnumerateParagraphs(doc).ToList();
                for (var i = 0; i < paragraphs.Count; i++)
                {
                    var segment = read.Segments[i];
                    var local = new List<Finding>();
                    foreach (var finding in findings)
                    {
                        if (finding.Start >= segment.Start && finding.Start + finding.Length <= segment.Start + segment.Text.Length)
                            local.Add(finding.Shift(-segment.Start));
                    }
                    if (local.Count == 0)
                        continue;
                    SetParagraphText(paragraphs[i], mode.Apply(segment.Text, local));
                }

                if (mode.Reversible && doc.MainDocumentPart?.Document?.Body is Body body)
                {
                    body.AppendChild(new Paragraph(new Run(new Text(mode.Session!.MapLine)
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    })));
                }

                doc.Save();
            }

            return File.ReadAllBytes(temp);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // Temp cleanup is best-effort. The original was not opened for write.
            }
        }
    }

    private static DocumentRead ReadOpen(WordprocessingDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        var segments = new List<TextSegment>();
        var places = new WordPlaces();
        foreach (var paragraph in EnumerateParagraphs(doc))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            var start = sb.Length;
            var text = GetParagraphText(paragraph);
            sb.Append(text);
            segments.Add(new TextSegment { Start = start, Text = text, Place = SegmentPlace.Fixed(places.Describe(paragraph, text)) });
        }

        return new DocumentRead
        {
            Text = sb.ToString(),
            Segments = segments,
            ExtraText = Extra(doc)
        };
    }

    internal static IEnumerable<Paragraph> EnumerateParagraphs(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart;
        if (main?.Document?.Body is not null)
        {
            foreach (var paragraph in main.Document.Body.Descendants<Paragraph>())
                yield return paragraph;
        }

        if (main is null)
            yield break;

        foreach (var header in main.HeaderParts)
        {
            foreach (var paragraph in header.Header.Descendants<Paragraph>())
                yield return paragraph;
        }

        foreach (var footer in main.FooterParts)
        {
            foreach (var paragraph in footer.Footer.Descendants<Paragraph>())
                yield return paragraph;
        }

        if (main.WordprocessingCommentsPart?.Comments is not null)
        {
            foreach (var paragraph in main.WordprocessingCommentsPart.Comments.Descendants<Paragraph>())
                yield return paragraph;
        }
        if (main.FootnotesPart?.Footnotes is not null)
        {
            foreach (var paragraph in main.FootnotesPart.Footnotes.Descendants<Paragraph>())
                yield return paragraph;
        }
        if (main.EndnotesPart?.Endnotes is not null)
        {
            foreach (var paragraph in main.EndnotesPart.Endnotes.Descendants<Paragraph>())
                yield return paragraph;
        }
        if (main.GlossaryDocumentPart?.GlossaryDocument is not null)
        {
            foreach (var paragraph in main.GlossaryDocumentPart.GlossaryDocument.Descendants<Paragraph>())
                yield return paragraph;
        }
    }

    private static string Extra(WordprocessingDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        var main = doc.MainDocumentPart;
        if (main is null)
            return "";
        foreach (var field in main.Document?.Descendants<FieldCode>() ?? [])
            sb.AppendLine(field.Text);
        foreach (var rel in main.HyperlinkRelationships)
            sb.AppendLine(rel.Uri.ToString());
        if (main.Document is not null)
        {
            foreach (var link in main.Document.Descendants<Hyperlink>())
            {
                var id = link.Id?.Value;
                if (string.IsNullOrEmpty(id))
                    continue;
                var relationship = main.HyperlinkRelationships.FirstOrDefault(item => item.Id == id);
                if (relationship is null)
                    continue;
                sb.Append("<a href=\"").Append(relationship.Uri).Append("\">").Append(link.InnerText).AppendLine("</a>");
            }
        }

        return sb.ToString();
    }

    private static string GetParagraphText(Paragraph paragraph)
    {
        var sb = new System.Text.StringBuilder();
        AppendParagraph(paragraph, sb);
        return sb.ToString();
    }

    private static void AppendParagraph(OpenXmlElement node, System.Text.StringBuilder sb)
    {
        foreach (var child in node.ChildElements)
        {
            if (child is Paragraph)
                continue;
            if (child is Text text)
                sb.Append(text.Text);
            else if (child is DeletedText deleted)
                sb.Append(deleted.Text);
            else if (child is FieldCode field)
                sb.Append(field.Text);
            else
                AppendParagraph(child, sb);
        }
    }

    private static void AppendRun(System.Text.StringBuilder sb, Run run)
    {
        foreach (var element in run.ChildElements)
        {
            if (element is Text text)
                sb.Append(text.Text);
            else if (element is TabChar)
                sb.Append('\t');
        }
    }

    private sealed class WordPlaces
    {
        private readonly Dictionary<Table, int> _tables = new(ReferenceEqualityComparer.Instance);
        private string _heading = "";
        private int _body;
        private int _header;
        private int _footer;
        private int _comment;
        private int _footnote;
        private int _endnote;
        private int _textBox;
        private int _tracked;

        public string Describe(Paragraph paragraph, string text)
        {
            if (paragraph.Ancestors<Comments>().Any())
                return $"comment, paragraph {++_comment}";
            if (paragraph.Ancestors<Footnotes>().Any())
                return $"footnote, paragraph {++_footnote}";
            if (paragraph.Ancestors<Endnotes>().Any())
                return $"endnote, paragraph {++_endnote}";

            var region = "";
            if (paragraph.Ancestors<Header>().Any())
                region = "header";
            else if (paragraph.Ancestors<Footer>().Any())
                region = "footer";

            string label;
            var table = paragraph.Ancestors<Table>().FirstOrDefault();
            var cell = paragraph.Ancestors<TableCell>().FirstOrDefault();
            if (table is not null && cell is not null)
                label = TableLabel(table, cell);
            else if (paragraph.Ancestors<TextBoxContent>().Any())
                label = $"text box, paragraph {++_textBox}";
            else if (paragraph.Descendants<DeletedRun>().Any() || paragraph.Descendants<InsertedRun>().Any())
                label = $"tracked change, paragraph {++_tracked}";
            else if (region == "header")
                label = $"header, paragraph {++_header}";
            else if (region == "footer")
                label = $"footer, paragraph {++_footer}";
            else
                label = $"body, paragraph {++_body}";

            if (region.Length == 0 && !string.IsNullOrWhiteSpace(_heading) && !IsHeading(paragraph))
            {
                var rest = label.StartsWith("body, ", StringComparison.Ordinal) ? label["body, ".Length..] : label;
                label = $"Section '{Clean(_heading)}', {rest}";
            }
            else if (region.Length > 0 && !label.StartsWith(region, StringComparison.Ordinal))
            {
                label = region + ", " + label;
            }

            if (region.Length == 0 && IsHeading(paragraph))
                _heading = text.Trim();
            return label;
        }

        private string TableLabel(Table table, TableCell cell)
        {
            if (!_tables.TryGetValue(table, out var number))
            {
                number = _tables.Count + 1;
                _tables[table] = number;
            }

            var row = cell.Ancestors<TableRow>().FirstOrDefault();
            var rowNumber = 1;
            if (row is not null)
            {
                var seen = 1;
                foreach (var item in table.Elements<TableRow>())
                {
                    if (ReferenceEquals(item, row))
                    {
                        rowNumber = seen;
                        break;
                    }

                    seen++;
                }
            }

            var column = 1;
            if (row is not null)
            {
                foreach (var item in row.Elements<TableCell>())
                {
                    if (ReferenceEquals(item, cell))
                        break;
                    var span = item.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                    column += span;
                }
            }

            return $"table {number}, row {rowNumber}, column {column}";
        }

        private static bool IsHeading(Paragraph paragraph)
        {
            var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (string.IsNullOrEmpty(style))
                return false;
            return style.Equals("Title", StringComparison.OrdinalIgnoreCase)
                || style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase);
        }

        private static string Clean(string text)
        {
            var sb = new System.Text.StringBuilder();
            var space = false;
            foreach (var c in text.Trim())
            {
                if (c is '\'' or '\r' or '\n' or '\t' || char.IsWhiteSpace(c))
                {
                    space = sb.Length > 0;
                    continue;
                }

                if (space)
                    sb.Append(' ');
                space = false;
                sb.Append(c);
                if (sb.Length >= 80)
                    break;
            }

            return sb.ToString();
        }
    }

    private static void SetParagraphText(Paragraph paragraph, string value)
    {
        var texts = paragraph.Descendants<Text>().ToList();

        if (texts.Count == 0)
        {
            paragraph.AppendChild(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
            return;
        }

        texts[0].Text = value;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = "";
    }
}
