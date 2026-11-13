namespace Peritum.Protect.Core.Scanning;

public enum ScanSource
{
    Clipboard,
    File,
    Manual
}

/// <summary>Text or a file path, plus where the scan came from.</summary>
public sealed class ScanRequest
{
    public string? Text { get; init; }
    public string? FilePath { get; init; }
    public ScanSource Source { get; init; }

    public static ScanRequest ForText(string text, ScanSource source) =>
        new() { Text = text, Source = source };

    public static ScanRequest ForFile(string path) =>
        new() { FilePath = path, Source = ScanSource.File };
}
