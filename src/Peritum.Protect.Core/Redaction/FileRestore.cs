using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Redaction;

public sealed class FileRestoreResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Details { get; init; } = [];
    public string? OutputPath { get; init; }
    public RestoreReport? Report { get; init; }
}

public static class FileRestore
{
    public const string Expired = "That restore map has expired.";
    public const string Missing = "No map with that id is stored on this PC.";
    public const string NoMapId = "No map id was found in the file.";
    public const string Unsupported = "Restore accepts a .txt, .md, or .docx file.";

    private static readonly Regex MapLine = new(
        @"Peritum map:\s*([A-Za-z0-9-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);
    }

    public static string ReadText(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            var read = new DocxFileAdapter().Read(path);
            if (read.Error is not null)
                throw new InvalidDataException(read.Error);
            return read.Text;
        }

        return File.ReadAllText(path);
    }

    public static FileRestoreResult RestoreFile(string path, MapStore store, TimeSpan maxAge)
    {
        if (!File.Exists(path))
            return Fail("That file could not be read.");
        if (!IsSupported(path))
            return Fail(Unsupported);

        string text;
        try
        {
            text = ReadText(path);
        }
        catch (Exception)
        {
            return Fail("That file could not be read.");
        }

        var restored = RestoreText(text, store, maxAge);
        if (restored.Report is null)
            return restored;

        try
        {
            var bytes = Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)
                ? DocxBytes(restored.Report.Text)
                : System.Text.Encoding.UTF8.GetBytes(restored.Report.Text);
            var output = ProtectedCopyWriter.Write(path, bytes, "{name}_restored{ext}");
            return new FileRestoreResult
            {
                Succeeded = true,
                Message = restored.Message,
                Details = restored.Details,
                OutputPath = output,
                Report = restored.Report
            };
        }
        catch (Exception)
        {
            return Fail("The restored file could not be written. The file you opened was not changed.");
        }
    }

    public static FileRestoreResult RestoreText(string text, MapStore store, TimeSpan maxAge)
    {
        var match = MapLine.Match(text ?? "");
        if (!match.Success)
            return Fail(NoMapId);

        var loaded = store.TryLoad(match.Groups[1].Value, maxAge);
        if (loaded.Status == MapStatus.Expired)
            return Fail(Expired);
        if (loaded.Status != MapStatus.Found || loaded.Map is null)
            return Fail(Missing);

        var report = TokenRestorer.Restore(text ?? "", loaded.Map.Tokens);
        return new FileRestoreResult
        {
            Succeeded = true,
            Message = Describe(report),
            Details = DetailLines(report),
            Report = report
        };
    }

    public static string Describe(RestoreReport report)
    {
        var parts = new List<string>();
        if (report.Restored.Count == 0)
            parts.Add("No tokens found");
        else if (report.Restored.Count == 1)
            parts.Add("1 value restored");
        else
            parts.Add(report.Restored.Count + " values restored");
        parts.Add(CountClause(report.Missing.Count, "not in the answer"));
        parts.Add(CountClause(report.Invented.Count, "not in the map"));
        return string.Join(". ", parts.Where(part => part.Length > 0)) + ".";
    }

    public static IReadOnlyList<string> DetailLines(RestoreReport report)
    {
        var lines = new List<string>();
        if (report.Missing.Count > 0)
            lines.Add("Not in the answer: " + string.Join(", ", report.Missing));
        if (report.Invented.Count > 0)
            lines.Add("Not in the map: " + string.Join(", ", report.Invented));
        return lines;
    }

    private static string CountClause(int count, string where)
    {
        if (count <= 0)
            return "";
        if (count == 1)
            return "1 token was " + where;
        return count + " tokens were " + where;
    }

    private static byte[] DocxBytes(string text)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            var lines = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            foreach (var line in lines)
            {
                body.AppendChild(new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
            }

            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static FileRestoreResult Fail(string message) => new()
    {
        Succeeded = false,
        Message = message
    };
}
