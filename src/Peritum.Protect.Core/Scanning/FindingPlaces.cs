using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Scanning;

/// <summary>
/// Turns adapter places into the location line a person can read, and a short masked context.
/// Neither string is written by this type.
/// </summary>
public static class FindingPlaces
{
    public const int ContextRadius = 40;

    public static IReadOnlyList<Finding> Apply(string text, IReadOnlyList<TextSegment> segments, IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
            return findings;
        var placed = new List<Finding>(findings.Count);
        foreach (var finding in findings)
        {
            var segment = SegmentFor(segments, finding);
            var local = segment is null ? finding.Start : finding.Start - segment.Start;
            var source = segment?.Text ?? text;
            var place = segment?.Place ?? SegmentPlace.Text();
            var location = Describe(place, source, local, finding.Length, segment?.Boxes, segment?.ImageWidth ?? 0, segment?.ImageHeight ?? 0);
            var context = ScrubKnown(Context(text, finding.Start, finding.Length, finding.RuleId), text, findings);
            placed.Add(finding.Located(location, context));
        }

        return placed;
    }

    public static string Describe(SegmentPlace place, string segmentText, int localOffset, int length, IReadOnlyList<OcrBox>? boxes, int imageWidth, int imageHeight)
    {
        var inner = place.Kind switch
        {
            "fixed" => place.Label,
            "csv" => Csv(segmentText, localOffset),
            "clipboard" => TextCatalog.Format("place.lineOnly", ("line", LineColumn(segmentText, localOffset).Line)),
            "image" => ImageArea(localOffset, length, boxes, imageWidth, imageHeight),
            _ => LineColumnText(segmentText, localOffset)
        };
        if (string.IsNullOrEmpty(place.Prefix))
            return inner;
        return string.IsNullOrEmpty(inner) ? place.Prefix.Trim() : place.Prefix + inner;
    }

    public static string Context(string text, int start, int length, string ruleId)
    {
        if (string.IsNullOrEmpty(text) || start < 0 || length <= 0 || start + length > text.Length)
            return "";
        var value = text.Substring(start, length);
        var from = Math.Max(0, start - ContextRadius);
        var to = Math.Min(text.Length, start + length + ContextRadius);
        var before = Collapse(text[from..start]);
        var after = Collapse(text[(start + length)..to]);
        var masked = Mask(value, ruleId);
        before = Scrub(before, value, masked);
        after = Scrub(after, value, masked);
        var body = before + masked + after;
        if (from > 0)
            body = "…" + body;
        if (to < text.Length)
            body += "…";
        return body;
    }

    public static string LineColumnText(string text, int offset)
    {
        var (line, column) = LineColumn(text, offset);
        return TextCatalog.Format("place.line", ("line", line), ("column", column));
    }

    public static (int Line, int Column) LineColumn(string text, int offset)
    {
        var line = 1;
        var column = 1;
        var limit = Math.Clamp(offset, 0, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else if (text[i] != '\r')
            {
                column++;
            }
        }

        return (line, column);
    }

    public static string Csv(string text, int offset)
    {
        var rows = ParseCsv(text);
        var rowNumber = 1;
        var columnNumber = 1;
        var headers = rows.Count > 0 ? rows[0] : [];
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                if (offset >= cell.Start && offset < cell.End)
                {
                    rowNumber = r + 1;
                    columnNumber = c + 1;
                    var header = r > 0 && c < headers.Count ? headers[c].Value.Trim() : "";
                    if (header.Length > 40)
                        header = header[..40];
                    header = header.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    return header.Length > 0 && r > 0
                        ? TextCatalog.Format("place.rowHeader", ("row", rowNumber), ("header", header))
                        : TextCatalog.Format("place.row", ("row", rowNumber), ("column", columnNumber));
                }
            }
        }

        var (line, column) = LineColumn(text, offset);
        return TextCatalog.Format("place.row", ("row", line), ("column", column));
    }

    public static string ImageArea(int localOffset, int length, IReadOnlyList<OcrBox>? boxes, int imageWidth, int imageHeight)
    {
        if (boxes is null || boxes.Count == 0 || imageWidth <= 0 || imageHeight <= 0)
            return TextCatalog.Get("place.image");
        var end = localOffset + Math.Max(length, 1);
        var hits = new List<OcrBox>();
        foreach (var box in boxes)
        {
            if (box.Start < end && localOffset < box.Start + box.Length)
                hits.Add(box);
        }

        if (hits.Count == 0)
            return TextCatalog.Get("place.image");
        var left = hits.Min(box => box.X);
        var top = hits.Min(box => box.Y);
        var right = hits.Max(box => box.X + box.Width);
        var bottom = hits.Max(box => box.Y + box.Height);
        var cx = (left + right) / 2f / imageWidth;
        var cy = (top + bottom) / 2f / imageHeight;
        return TextCatalog.Format("place.imageAt", ("area", Region(cx, cy)));
    }

    private static string Region(float x, float y)
    {
        var column = x < 1f / 3f ? TextCatalog.Get("place.left") : x > 2f / 3f ? TextCatalog.Get("place.right") : "";
        var row = y < 1f / 3f ? TextCatalog.Get("place.top") : y > 2f / 3f ? TextCatalog.Get("place.bottom") : "";
        if (column.Length == 0 && row.Length == 0)
            return TextCatalog.Get("place.center");
        if (row.Length == 0)
            return column;
        if (column.Length == 0)
            return row;
        return row + " " + column;
    }

    private static string ScrubKnown(string context, string text, IReadOnlyList<Finding> findings)
    {
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length < 4 || finding.Start + finding.Length > text.Length)
                continue;
            var value = text.Substring(finding.Start, finding.Length);
            context = Scrub(context, value, Mask(value, finding.RuleId));
        }

        return context;
    }

    private static string Mask(string value, string ruleId)
    {
        var masked = ValueMask.Mask(value, ruleId);
        if (masked.Contains(value, StringComparison.Ordinal))
            return new string('*', Math.Max(4, Math.Min(value.Length, 12)));
        return masked;
    }

    private static string Scrub(string window, string value, string masked)
    {
        if (value.Length == 0 || !window.Contains(value, StringComparison.Ordinal))
            return window;
        return window.Replace(value, masked, StringComparison.Ordinal);
    }

    private static string Collapse(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c is '\r' or '\n' or '\t' ? ' ' : c);
        return sb.ToString();
    }

    private static TextSegment? SegmentFor(IReadOnlyList<TextSegment> segments, Finding finding)
    {
        TextSegment? found = null;
        foreach (var segment in segments)
        {
            var end = segment.Start + segment.Text.Length;
            if (finding.Start >= segment.Start && finding.Start < end)
                found = segment;
        }

        return found;
    }

    private readonly record struct CsvCell(int Start, int End, string Value);

    private static List<List<CsvCell>> ParseCsv(string text)
    {
        var rows = new List<List<CsvCell>>();
        var row = new List<CsvCell>();
        var i = 0;
        while (i <= text.Length)
        {
            if (i == text.Length)
            {
                rows.Add(row);
                break;
            }

            var start = i;
            var sb = new System.Text.StringBuilder();
            if (text[i] == '"')
            {
                start = i + 1;
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    sb.Append(text[i]);
                    i++;
                }

                var end = i;
                if (i < text.Length && text[i] == '"')
                    i++;
                row.Add(new CsvCell(start, Math.Max(start + 1, end), sb.ToString()));
            }
            else
            {
                while (i < text.Length && text[i] != ',' && text[i] != '\n' && text[i] != '\r')
                {
                    sb.Append(text[i]);
                    i++;
                }

                var end = Math.Max(start + 1, i);
                row.Add(new CsvCell(start, end, sb.ToString()));
            }

            if (i >= text.Length)
            {
                rows.Add(row);
                break;
            }

            if (text[i] == ',')
            {
                i++;
                continue;
            }

            if (text[i] == '\r')
                i++;
            if (i < text.Length && text[i] == '\n')
                i++;
            rows.Add(row);
            row = [];
        }

        return rows;
    }
}
