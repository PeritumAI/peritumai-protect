using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class TextSegment
{
    public int Start { get; init; }
    public string Text { get; init; } = "";
    /// <summary>How to describe a hit inside this segment. Set by the adapter that read the file.</summary>
    public SegmentPlace? Place { get; init; }
    public IReadOnlyList<OcrBox>? Boxes { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    /// <summary>Column header for a spreadsheet cell. Empty when this segment has none.</summary>
    public string Header { get; init; } = "";

    public TextSegment Relocated(int start, string prefix)
    {
        var place = Place ?? SegmentPlace.Text();
        if (!string.IsNullOrEmpty(prefix))
            place = place.Prefixed(prefix);
        return new TextSegment
        {
            Start = start,
            Text = Text,
            Place = place,
            Boxes = Boxes,
            ImageWidth = ImageWidth,
            ImageHeight = ImageHeight,
            Header = Header
        };
    }
}

/// <summary>Adapter-owned description of one piece of extracted text.</summary>
public sealed class SegmentPlace
{
    /// <summary>text, csv, clipboard, fixed, or image.</summary>
    public string Kind { get; init; } = "text";
    public string Label { get; init; } = "";
    public string Prefix { get; init; } = "";

    public static SegmentPlace Text(string prefix = "") => new() { Kind = "text", Prefix = prefix };
    public static SegmentPlace Csv() => new() { Kind = "csv" };
    public static SegmentPlace Clipboard() => new() { Kind = "clipboard" };
    public static SegmentPlace Fixed(string label) => new() { Kind = "fixed", Label = label };
    public static SegmentPlace Image() => new() { Kind = "image" };

    public SegmentPlace Prefixed(string prefix) => new()
    {
        Kind = Kind,
        Label = Label,
        Prefix = prefix + Prefix
    };
}

public sealed class OcrBox
{
    public int Start { get; init; }
    public int Length { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}

public sealed class DocumentRead
{
    public string Text { get; init; } = "";
    public IReadOnlyList<TextSegment> Segments { get; init; } = [];
    public IReadOnlyList<OcrBox> Boxes { get; init; } = [];
    public string? Error { get; init; }
    public string? ReasonCode { get; init; }
    public string? Note { get; set; }
    public bool Incomplete { get; init; }
    public string ExtraText { get; init; } = "";
    public string FromHeader { get; init; } = "";
    public string ReplyToHeader { get; init; } = "";
    public int CodePage { get; init; } = 65001;
    /// <summary>Write a .txt sibling of the redacted text. Used for PDF.</summary>
    public bool WriteRedactedTextBeside { get; init; }
    /// <summary>Write a .txt list of the sensitive strings. Used for images.</summary>
    public bool EmitFindingSidecar { get; init; }
    /// <summary>False when the file can be scanned but a protected copy cannot be written.</summary>
    public bool CopySupported { get; init; } = true;
    internal object? AdapterState { get; init; }

    public static DocumentRead Fail(string error, string? reasonCode = null) => new() { Error = error, ReasonCode = reasonCode };

    public static DocumentRead FromSingle(string text) => new()
    {
        Text = text,
        Segments = [new TextSegment { Start = 0, Text = text }]
    };
}

public interface IFileAdapter
{
    bool CanHandle(string extension);
    DocumentRead Read(string path);
    byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null);
}

/// <summary>A package whose read shares the caller's size budget and nesting depth.</summary>
internal interface IPackageFile
{
    DocumentRead Read(string path, PackageLimits.Budget budget, int depth);
}
