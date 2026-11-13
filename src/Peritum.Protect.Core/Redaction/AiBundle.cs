using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Redaction;

public sealed class BundleExport
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
    public string? OutputPath { get; init; }
    public string? MapId { get; init; }
}

public static class AiBundle
{
    public const string Header = "Peritum markers: keep every [[TOKEN]] exactly. Do not guess hidden values. Do not merge or renumber tokens.";
    public const string NewDocument = "A new document from the AI's answer. The original layout is not kept.";

    public static string Export(string text, string mapId)
    {
        return Header + Environment.NewLine + "Peritum map: " + mapId + Environment.NewLine + Environment.NewLine + text;
    }

    public static BundleExport ExportFile(
        string sourcePath,
        IReadOnlyList<Rule> rules,
        Func<string, bool> allow,
        MapStore maps,
        IReadOnlyCollection<string>? excludeValues = null)
    {
        string markdown;
        try
        {
            markdown = BundleMarkdown.FromFile(sourcePath);
        }
        catch (Exception)
        {
            return Fail("This file could not be read, so no bundle was written.");
        }

        bool Allowed(string value) =>
            allow(value) || (excludeValues?.Contains(value, StringComparer.Ordinal) ?? false);
        var session = new ProtectService().Scan(ScanRequest.ForText(markdown, ScanSource.File), rules, Allowed);
        if (session.Result.Error is not null)
            return Fail(session.Result.Error);
        var (redacted, map) = ReversibleRedactor.Apply(markdown, session.Result.Findings, true);
        var body = Export(redacted, map.Id);
        var values = Values(markdown, session.Result.Findings);
        if (values.Any(value => value.Length > 0 && body.Contains(value, StringComparison.Ordinal)))
            return Fail("The bundle still contained a matched value, so it was not written.");
        maps.Save(map);
        var output = ProtectedCopyWriter.Write(sourcePath, Encoding.UTF8.GetBytes(body), "{name}_bundle.md");
        return new BundleExport
        {
            Succeeded = true,
            Message = "AI bundle saved. Paste that file into a chat. The map stays on this PC.",
            OutputPath = output,
            MapId = map.Id
        };
    }

    public static FileRestoreResult ImportAnswer(string answer, MapStore maps, TimeSpan maxAge, string outputPath)
    {
        var restored = FileRestore.RestoreText(answer, maps, maxAge);
        if (!restored.Succeeded || restored.Report is null)
            return restored;
        var extension = Path.GetExtension(outputPath);
        byte[] bytes;
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            bytes = MarkdownDocx(restored.Report.Text);
        else if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            bytes = Encoding.UTF8.GetBytes(restored.Report.Text);
        else
            return new FileRestoreResult { Succeeded = false, Message = "Save the answer as .txt, .md, or .docx." };
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(outputPath, bytes);
        return new FileRestoreResult
        {
            Succeeded = true,
            Message = NewDocument + " " + restored.Message,
            Details = restored.Details,
            OutputPath = outputPath,
            Report = restored.Report
        };
    }

    private static BundleExport Fail(string message) => new() { Succeeded = false, Message = message };

    private static List<string> Values(string text, IReadOnlyList<Finding> findings)
    {
        var values = new List<string>();
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > text.Length)
                continue;
            values.Add(text.Substring(finding.Start, finding.Length));
        }
        return values;
    }

    public static byte[] MarkdownDocx(string markdown)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("| ", StringComparison.Ordinal) && line.EndsWith('|'))
                {
                    if (line.Contains("---", StringComparison.Ordinal))
                        continue;
                    var cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
                    var row = new TableRow();
                    foreach (var cell in cells)
                        row.Append(new TableCell(new Paragraph(new Run(new Text(cell) { Space = SpaceProcessingModeValues.Preserve }))));
                    var table = body.Elements<Table>().LastOrDefault();
                    if (table is null || body.LastChild is not Table)
                    {
                        table = new Table();
                        body.Append(table);
                    }
                    table.Append(row);
                    continue;
                }
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    var level = 0;
                    while (level < line.Length && line[level] == '#')
                        level++;
                    var text = line[level..].Trim();
                    body.Append(new Paragraph(
                        new ParagraphProperties(new ParagraphStyleId { Val = "Heading" + Math.Clamp(level, 1, 6) }),
                        new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
                    continue;
                }
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    body.Append(RunParagraph("• " + line[2..]));
                    continue;
                }
                if (line.Length == 0)
                    continue;
                body.Append(RunParagraph(line));
            }
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static Paragraph RunParagraph(string line)
    {
        var paragraph = new Paragraph();
        var rest = line;
        while (rest.Length > 0)
        {
            var bold = rest.IndexOf("**", StringComparison.Ordinal);
            var italic = rest.IndexOf('*');
            if (bold == 0)
            {
                var end = rest.IndexOf("**", 2, StringComparison.Ordinal);
                if (end > 2)
                {
                    paragraph.Append(Styled(rest[2..end], bold: true, italic: false));
                    rest = rest[(end + 2)..];
                    continue;
                }
            }
            if (italic == 0 && (rest.Length < 2 || rest[1] != '*'))
            {
                var end = rest.IndexOf('*', 1);
                if (end > 1)
                {
                    paragraph.Append(Styled(rest[1..end], bold: false, italic: true));
                    rest = rest[(end + 1)..];
                    continue;
                }
            }
            var next = NextMark(rest);
            var plain = next < 0 ? rest : rest[..next];
            if (plain.Length > 0)
                paragraph.Append(Styled(plain, false, false));
            rest = next < 0 ? "" : rest[next..];
        }
        return paragraph;
    }

    private static int NextMark(string text)
    {
        var bold = text.IndexOf("**", StringComparison.Ordinal);
        var italic = text.IndexOf('*');
        if (bold < 0)
            return italic;
        if (italic < 0)
            return bold;
        return Math.Min(bold, italic);
    }

    private static Run Styled(string text, bool bold, bool italic)
    {
        var run = new Run();
        if (bold || italic)
        {
            var props = new RunProperties();
            if (bold)
                props.Append(new Bold());
            if (italic)
                props.Append(new Italic());
            run.Append(props);
        }
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }
}
