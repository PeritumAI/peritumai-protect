using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Links;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Peritum.Protect.Core.Redaction;

public static class BundleMarkdown
{
    public static string FromFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase))
            return Docx(path);
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
            return Xlsx(path);
        if (extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pptm", StringComparison.OrdinalIgnoreCase))
            return Pptx(path);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return Pdf(path);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return Csv(path);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            return HtmlLinks.ToMarkdown(File.ReadAllText(path));
        if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            return Eml(path);
        return File.ReadAllText(path);
    }

    private static string Eml(string path)
    {
        var root = MimeParser.Parse(File.ReadAllBytes(path));
        if (root is null)
            return File.ReadAllText(path);
        var plain = new StringBuilder();
        var html = new StringBuilder();
        Collect(root, plain, html, headers: true);
        var body = new StringBuilder();
        if (plain.Length > 0)
            body.Append(plain);
        if (html.Length > 0)
        {
            if (body.Length > 0 && body[^1] != '\n')
                body.AppendLine();
            body.Append(HtmlLinks.ToMarkdown(html.ToString()).Trim());
        }

        root.Headers.TryGetValue("From", out var from);
        var note = LinkCopyNote.ForEmail(plain + "\n" + html, from);
        var text = body.ToString().Trim();
        if (note is null)
            return text + Environment.NewLine;
        return note + Environment.NewLine + Environment.NewLine + text + Environment.NewLine;
    }

    private static void Collect(MimeParser.Part part, StringBuilder plain, StringBuilder html, bool headers)
    {
        if (headers)
        {
            foreach (var name in new[] { "From", "To", "Cc", "Subject" })
            {
                if (part.Headers.TryGetValue(name, out var value) && value.Length > 0)
                    plain.Append(name).Append(": ").AppendLine(value);
            }
        }

        if (part.Children.Count > 0)
        {
            var inner = part.MediaType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase);
            foreach (var child in part.Children)
                Collect(child, plain, html, inner);
            return;
        }

        if (!string.IsNullOrEmpty(part.Html))
            html.AppendLine(part.Html);
        else if (part.Text is not null && !part.Attachment)
            plain.AppendLine(part.Text);
    }

    private static string Docx(string path)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null)
            return "";
        AppendContainer(sb, body);
        return sb.ToString().Trim() + Environment.NewLine;
    }

    private static void AppendContainer(StringBuilder sb, OpenXmlElement node)
    {
        foreach (var child in node.ChildElements)
        {
            if (child is W.Paragraph paragraph)
                AppendParagraph(sb, paragraph);
            else if (child is W.Table table)
                AppendTable(sb, table);
            else if (child is AlternateContent alternate)
            {
                var branch = (OpenXmlElement?)alternate.GetFirstChild<AlternateContentChoice>()
                    ?? alternate.GetFirstChild<AlternateContentFallback>();
                if (branch is not null)
                    AppendContainer(sb, branch);
            }
            else
                AppendContainer(sb, child);
        }
    }

    private static void AppendParagraph(StringBuilder sb, W.Paragraph paragraph)
    {
        var text = ParagraphText(paragraph);
        if (text.Trim().Length > 0)
            WriteParagraph(sb, paragraph, text);
        foreach (var nested in paragraph.Descendants<W.Paragraph>())
        {
            if (ReferenceEquals(nested.Ancestors<W.Paragraph>().FirstOrDefault(), paragraph))
                AppendParagraph(sb, nested);
        }
    }

    private static void WriteParagraph(StringBuilder sb, W.Paragraph paragraph, string text)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            var level = 1;
            if (style.Length > 0 && char.IsDigit(style[^1]))
                level = Math.Clamp(style[^1] - '0', 1, 6);
            var marks = new string('#', level);
            foreach (var line in text.Split('\n'))
            {
                if (line.Trim().Length == 0)
                    continue;
                sb.Append(marks).Append(' ').AppendLine(line);
            }
            sb.AppendLine();
            return;
        }
        if (paragraph.ParagraphProperties?.NumberingProperties is not null)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.Trim().Length == 0)
                    continue;
                sb.Append("- ").AppendLine(line);
            }
            return;
        }
        sb.AppendLine(text).AppendLine();
    }

    private static string ParagraphText(W.Paragraph paragraph)
    {
        var sb = new StringBuilder();
        AppendInline(paragraph, sb);
        return sb.ToString();
    }

    private static void AppendInline(OpenXmlElement node, StringBuilder sb)
    {
        foreach (var child in node.ChildElements)
        {
            if (child is W.Paragraph)
                continue;
            if (child is AlternateContent alternate)
            {
                var branch = (OpenXmlElement?)alternate.GetFirstChild<AlternateContentChoice>()
                    ?? alternate.GetFirstChild<AlternateContentFallback>();
                if (branch is not null)
                    AppendInline(branch, sb);
                continue;
            }
            if (child is W.Break or W.CarriageReturn)
            {
                sb.Append('\n');
                continue;
            }
            if (child is W.TabChar)
            {
                sb.Append('\t');
                continue;
            }
            if (child is W.Text text)
                sb.Append(text.Text);
            else
                AppendInline(child, sb);
        }
    }

    private static void AppendTable(StringBuilder sb, W.Table table)
    {
        var rows = new List<List<string>>();
        foreach (var row in table.Elements<W.TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<W.TableCell>())
                cells.Add(CellText(cell, table));
            if (cells.Count > 0)
                rows.Add(cells);
        }
        if (rows.Count == 0)
            return;
        WriteTable(sb, rows);
    }

    private static string CellText(W.TableCell cell, W.Table table)
    {
        var parts = new List<string>();
        foreach (var paragraph in cell.Descendants<W.Paragraph>())
        {
            if (!ReferenceEquals(paragraph.Ancestors<W.Table>().FirstOrDefault(), table))
                continue;
            var text = ParagraphText(paragraph).Replace('\n', ' ').Replace("|", "\\|", StringComparison.Ordinal).Trim();
            if (text.Length > 0)
                parts.Add(text);
        }
        return string.Join(" ", parts);
    }

    private static string Xlsx(string path)
    {
        var sb = new StringBuilder();
        using var doc = SpreadsheetDocument.Open(path, false);
        var workbook = doc.WorkbookPart;
        if (workbook?.Workbook.Sheets is null)
            return "";
        var shared = workbook.SharedStringTablePart?.SharedStringTable;
        foreach (var sheet in workbook.Workbook.Sheets.Elements<Sheet>())
        {
            sb.Append("## ").AppendLine(sheet.Name?.Value ?? "Sheet");
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
            var rows = new List<List<string>>();
            foreach (var row in part.Worksheet.Descendants<Row>())
            {
                var cells = row.Elements<Cell>().Select(cell => CellText(cell, shared)).ToList();
                if (cells.Any(cell => cell.Length > 0))
                    rows.Add(cells);
            }
            if (rows.Count > 0)
                WriteTable(sb, rows);
            sb.AppendLine();
        }
        return sb.ToString().Trim() + Environment.NewLine;
    }

    private static string CellText(Cell cell, SharedStringTable? shared)
    {
        if (cell.InlineString is not null)
            return cell.InlineString.InnerText;
        var value = cell.CellValue?.Text ?? "";
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(value, out var index) && shared is not null)
            return shared.ElementAt(index).InnerText;
        return value;
    }

    private static string Pptx(string path)
    {
        var sb = new StringBuilder();
        using var doc = PresentationDocument.Open(path, false);
        var presentation = doc.PresentationPart;
        if (presentation?.Presentation.SlideIdList is null)
            return "";
        var number = 1;
        foreach (var id in presentation.Presentation.SlideIdList.Elements<SlideId>())
        {
            var slide = (SlidePart)presentation.GetPartById(id.RelationshipId!);
            sb.Append("## Slide ").AppendLine(number.ToString());
            foreach (var paragraph in slide.Slide.Descendants<A.Paragraph>())
            {
                var text = paragraph.InnerText;
                if (text.Length > 0)
                    sb.AppendLine(text);
            }
            if (slide.NotesSlidePart is not null)
            {
                sb.AppendLine();
                sb.AppendLine("Notes");
                foreach (var paragraph in slide.NotesSlidePart.NotesSlide.Descendants<A.Paragraph>())
                {
                    var text = paragraph.InnerText;
                    if (text.Length > 0)
                        sb.AppendLine(text);
                }
            }
            sb.AppendLine();
            number++;
        }
        return sb.ToString().Trim() + Environment.NewLine;
    }

    private static string Pdf(string path)
    {
        var sb = new StringBuilder();
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            sb.Append("## Page ").AppendLine(page.Number.ToString());
            sb.AppendLine(page.Text ?? "");
            sb.AppendLine();
        }
        return sb.ToString().Trim() + Environment.NewLine;
    }

    private static string Csv(string path)
    {
        var rows = File.ReadAllLines(path).Where(line => line.Length > 0).Select(line => line.Split(',').ToList()).ToList();
        var sb = new StringBuilder();
        if (rows.Count > 0)
            WriteTable(sb, rows);
        return sb.ToString();
    }

    private static void WriteTable(StringBuilder sb, List<List<string>> rows)
    {
        var width = rows.Max(row => row.Count);
        string Line(List<string> row)
        {
            var cells = new string[width];
            for (var i = 0; i < width; i++)
                cells[i] = i < row.Count ? row[i] : "";
            return "| " + string.Join(" | ", cells) + " |";
        }
        sb.AppendLine(Line(rows[0]));
        sb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", width)) + " |");
        foreach (var row in rows.Skip(1))
            sb.AppendLine(Line(row));
        sb.AppendLine();
    }
}
