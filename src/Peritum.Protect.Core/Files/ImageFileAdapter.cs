using System.Drawing;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class ImageFileAdapter : IFileAdapter
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    private readonly IOcrEngine? _ocr;

    public ImageFileAdapter(IOcrEngine? ocr) => _ocr = ocr;

    public bool CanHandle(string extension) => Extensions.Contains(extension);

    public DocumentRead Read(string path)
    {
        if (_ocr is null || !_ocr.IsAvailable)
        {
            return DocumentRead.Fail(_ocr?.UnavailableReason
                ?? "OCR is not available on this PC. Image scan is off.");
        }

        OcrDocument? recognized;
        try
        {
            recognized = _ocr.Recognize(path);
        }
        catch
        {
            return DocumentRead.Fail("OCR is not available on this PC. Image scan is off.");
        }

        if (recognized is null)
            return DocumentRead.Fail("OCR is not available on this PC. Image scan is off.");

        var width = 0;
        var height = 0;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var bitmap = new Bitmap(stream);
            width = bitmap.Width;
            height = bitmap.Height;
        }
        catch
        {
            // The area stays "text in image" when the picture cannot be measured.
        }

        return new DocumentRead
        {
            Text = recognized.Text,
            Segments =
            [
                new TextSegment
                {
                    Start = 0,
                    Text = recognized.Text,
                    Place = SegmentPlace.Image(),
                    Boxes = recognized.Words,
                    ImageWidth = width,
                    ImageHeight = height
                }
            ],
            Boxes = recognized.Words,
            EmitFindingSidecar = true,
            Note = recognized.Text.Length == 0
                ? "No text was read from this image."
                : null
        };
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        _ = redaction;
        var refs = findings.Select(finding => new FindingRef(finding.Start, finding.Length)).ToList();
        return ImageBars.Cover(File.ReadAllBytes(originalPath), read.Boxes, refs, Path.GetExtension(originalPath), refuseIfUncovered: false)
            ?? throw new InvalidOperationException("Could not read this image.");
    }
}
