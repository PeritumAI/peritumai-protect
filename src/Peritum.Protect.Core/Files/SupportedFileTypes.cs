namespace Peritum.Protect.Core.Files;

public static class SupportedFileTypes
{
    public static IReadOnlyList<string> Extensions { get; } =
    [
        ".txt", ".md", ".csv", ".json", ".xml", ".log",
        ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm", ".pdf", ".html", ".htm", ".eml", ".zip",
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif"
    ];

    public const string DisplaySummary = "Text, Office, PDF, images, email, zip, and HTML";

    public static string DialogFilter =>
        "Supported files|" + string.Join(";", Extensions.Select(extension => "*" + extension)) +
        "|All files|*.*";

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
