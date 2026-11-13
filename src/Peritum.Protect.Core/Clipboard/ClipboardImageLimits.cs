namespace Peritum.Protect.Core.Clipboard;

public static class ClipboardImageLimits
{
    public const int MaxBytes = 12 * 1024 * 1024;
    public const int MaxEdge = 8000;
    public const long MaxPixels = 16_000_000;

    public const string NotChecked = "Not checked. This image is above the size limit, so it was not read.";
    public const string OcrOff = "Not checked. OCR is not available on this PC. Image scan is off.";

    public static bool Exceeds(int byteLength, int width, int height) =>
        byteLength > MaxBytes
        || width > MaxEdge
        || height > MaxEdge
        || (width > 0 && height > 0 && (long)width * height > MaxPixels);
}
