using System.Text;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class HtmlFileAdapter : IFileAdapter
{
    public bool CanHandle(string extension) =>
        extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);

    public DocumentRead Read(string path)
    {
        var read = HtmlClipboard.Read(File.ReadAllText(path));
        var text = new StringBuilder();
        var segments = new List<TextSegment>();
        Add(text, segments, read.VisibleText, "HTML");
        foreach (var hit in read.Hidden)
            Add(text, segments, hit.Text, "HTML, " + hit.Reason);
        return new DocumentRead { Text = text.ToString(), Segments = segments };
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var html = HtmlClipboard.Read(File.ReadAllText(originalPath));
        var visible = html.VisibleText;
        var mode = redaction ?? CopyRedaction.Permanent;
        var pairs = new List<(string Value, string Token)>();
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > visible.Length)
                continue;
            var value = visible.Substring(finding.Start, finding.Length);
            pairs.Add((value, mode.Apply(value, [finding.Shift(-finding.Start)])));
        }

        return Encoding.UTF8.GetBytes(HtmlClipboard.Sanitize(html.Fragment, pairs));
    }

    private static void Add(StringBuilder text, List<TextSegment> segments, string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (text.Length > 0)
            text.Append('\n');
        var start = text.Length;
        text.Append(value);
        segments.Add(new TextSegment { Start = start, Text = value, Place = SegmentPlace.Fixed(label) });
    }
}
