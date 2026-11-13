using Peritum.Protect.Core.Files;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Peritum.Protect.App.Services;

public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine? _engine;

    public WindowsOcrEngine()
    {
        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            IsAvailable = _engine is not null;
        }
        catch
        {
            IsAvailable = false;
        }

        UnavailableReason = IsAvailable
            ? null
            : "OCR is not available on this PC. Image scan is off.";
    }

    public bool IsAvailable { get; }
    public string? UnavailableReason { get; }

    public OcrDocument? Recognize(string path)
    {
        try
        {
            return RecognizeBytes(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    public OcrDocument? RecognizeBytes(ReadOnlyMemory<byte> image)
    {
        if (_engine is null)
            return null;
        try
        {
            return Task.Run(() => RecognizeAsync(image.ToArray())).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private async Task<OcrDocument?> RecognizeAsync(byte[] bytes)
    {
        if (_engine is null)
            return null;
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var bitmap = await decoder.GetSoftwareBitmapAsync();
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            bitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await _engine.RecognizeAsync(bitmap);
        var text = new System.Text.StringBuilder();
        var boxes = new List<OcrBox>();
        foreach (var line in result.Lines)
        {
            if (text.Length > 0)
                text.Append('\n');
            var first = true;
            foreach (var word in line.Words)
            {
                if (!first)
                    text.Append(' ');
                first = false;
                var value = word.Text ?? "";
                var start = text.Length;
                text.Append(value);
                var rect = word.BoundingRect;
                boxes.Add(new OcrBox
                {
                    Start = start,
                    Length = value.Length,
                    X = (float)rect.X,
                    Y = (float)rect.Y,
                    Width = (float)rect.Width,
                    Height = (float)rect.Height
                });
            }
        }

        return new OcrDocument { Text = text.ToString(), Words = boxes };
    }
}
