using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Peritum.Protect.Core.Files;

/// <summary>
/// A new PDF that contains only the supplied text. It is not a visual copy of another PDF.
/// Text is drawn with a subset of a Windows TrueType font (Identity-H via PdfPig).
/// </summary>
public static class SimplePdfWriter
{
    public const string MissingFontNote = "Some characters had no font on this PC.";

    public static byte[] FromText(string text) => FromText(text, out _);

    public static byte[] FromText(string text, out string? note)
    {
        text ??= "";
        var fonts = SystemFonts.Load();
        var missing = new HashSet<int>();
        var prepared = Prepare(text, fonts, missing);
        note = missing.Count == 0 ? null : MissingFontNote;
        if (missing.Count > 0)
            prepared += "\n" + MissingFontNote;

        var builder = new PdfDocumentBuilder();
        var loaded = new Dictionary<string, PdfDocumentBuilder.AddedFont>(StringComparer.OrdinalIgnoreCase);
        PdfDocumentBuilder.AddedFont FontFor(SystemFonts.Face face)
        {
            if (!loaded.TryGetValue(face.Path, out var added))
            {
                added = builder.AddTrueTypeFont(face.Bytes);
                loaded[face.Path] = added;
            }
            return added;
        }

        var lines = Wrap(prepared, 80);
        if (lines.Count == 0)
            lines.Add(" ");
        PdfPageBuilder? page = null;
        var y = 760d;
        foreach (var line in lines)
        {
            if (page is null || y < 50)
            {
                page = builder.AddPage(595, 842);
                y = 760;
            }
            DrawLine(page, line, fonts, FontFor, 50, y);
            y -= 14;
        }
        if (page is null)
            builder.AddPage(595, 842);
        return builder.Build();
    }

    private static void DrawLine(
        PdfPageBuilder page,
        string line,
        IReadOnlyList<SystemFonts.Face> fonts,
        Func<SystemFonts.Face, PdfDocumentBuilder.AddedFont> fontFor,
        double x,
        double y)
    {
        var i = 0;
        while (i < line.Length)
        {
            var face = SystemFonts.FaceFor(fonts, line[i]);
            var j = i + 1;
            if (face is not null)
            {
                while (j < line.Length && SystemFonts.FaceFor(fonts, line[j]) == face)
                    j++;
                var run = line[i..j];
                page.AddText(run, 11, new PdfPoint(x, y), fontFor(face));
                x += run.Length * (run.Any(c => c > 0x2E7F) ? 11 : 6.2);
            }
            i = j;
        }
    }

    private static string Prepare(string text, IReadOnlyList<SystemFonts.Face> fonts, HashSet<int> missing)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\r' or '\n' or '\t' || c == ' ')
            {
                sb.Append(c == '\t' ? ' ' : c);
                continue;
            }
            if (SystemFonts.FaceFor(fonts, c) is null)
            {
                missing.Add(c);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length == 0)
            {
                lines.Add(" ");
                continue;
            }
            var rest = raw;
            while (rest.Length > width)
            {
                lines.Add(rest[..width]);
                rest = rest[width..];
            }
            lines.Add(rest);
        }
        return lines;
    }
}
