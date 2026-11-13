using Peritum.Protect.Core.Files;

namespace Peritum.Protect.Core.Scanning;

/// <summary>
/// In-memory scan. <see cref="Text"/> is kept so the app can redact.
/// The event log stores the hash and hit counts only.
/// </summary>
public sealed class ScanSession
{
    public required ScanRequest Request { get; init; }
    public required ScanResult Result { get; init; }
    public string Text { get; init; } = "";
    public IReadOnlyList<OcrBox> Boxes { get; init; } = [];
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    /// <summary>Length of the text a paste would show. Hidden HTML is appended after this. -1 means the whole text.</summary>
    public int VisibleLength { get; init; } = -1;
    public byte[]? ClipboardImage { get; init; }
    public string? ClipboardHtmlFragment { get; init; }
    /// <summary>Where the OCR text starts inside <see cref="Text"/>. -1 when there is no image.</summary>
    public int ImageTextStart { get; init; } = -1;
    public int ImageTextLength { get; init; }
}
