namespace Peritum.Protect.Core.Clipboard;

/// <summary>
/// One clipboard snapshot. Tests build this themselves. The watcher is the only reader of the real clipboard.
/// </summary>
public sealed class ClipboardScanInput
{
    public string? Text { get; init; }
    public IReadOnlyList<string> FilePaths { get; init; } = [];
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public byte[]? ImageBytes { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public bool ImageExceedsLimit { get; init; }

    public bool HasRich =>
        !string.IsNullOrWhiteSpace(Html)
        || !string.IsNullOrWhiteSpace(Rtf)
        || ImageBytes is { Length: > 0 }
        || ImageExceedsLimit;
}
