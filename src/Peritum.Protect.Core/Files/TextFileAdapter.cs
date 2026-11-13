using System.Text;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class TextFileAdapter : IFileAdapter
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".log"
    };

    public bool CanHandle(string extension) => Extensions.Contains(extension);

    public DocumentRead Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = Detect(bytes);
        var text = encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        var csv = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
        return new DocumentRead
        {
            Text = text,
            Segments = [new TextSegment { Start = 0, Text = text, Place = csv ? SegmentPlace.Csv() : SegmentPlace.Text() }],
            CodePage = encoding.CodePage
        };
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var mode = redaction ?? CopyRedaction.Permanent;
        var redacted = mode.WithMapLine(mode.Apply(read.Text, findings));
        var encoding = Encoding.GetEncoding(read.CodePage);
        return encoding.GetBytes(redacted);
    }

    private static Encoding Detect(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).Length >= 0
                ? new UTF8Encoding(false)
                : Encoding.GetEncoding(1250);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1250);
        }
    }
}
