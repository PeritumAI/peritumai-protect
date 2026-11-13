namespace Peritum.Protect.Core.Files;

public interface IOcrEngine
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    OcrDocument? Recognize(string path);

    OcrDocument? RecognizeBytes(ReadOnlyMemory<byte> image)
    {
        var path = Path.Combine(Path.GetTempPath(), "peritum-ocr-" + Guid.NewGuid().ToString("n") + ".png");
        try
        {
            File.WriteAllBytes(path, image.ToArray());
            return Recognize(path);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // The temp picture is only for OCR.
            }
        }
    }
}

public sealed class OcrDocument
{
    public string Text { get; init; } = "";
    public IReadOnlyList<OcrBox> Words { get; init; } = [];
}
