using System.Drawing;
using System.Drawing.Imaging;

namespace Peritum.Protect.Core.Files;

public static class ImageBars
{
    public static bool Covers(IReadOnlyList<OcrBox> boxes, IReadOnlyList<FindingRef> findings)
    {
        foreach (var finding in findings)
        {
            var hit = false;
            foreach (var box in boxes)
            {
                if (Overlaps(box, finding.Start, finding.Length))
                {
                    hit = true;
                    break;
                }
            }

            if (!hit)
                return false;
        }

        return true;
    }

    public static bool Overlaps(OcrBox box, int start, int length)
    {
        var boxEnd = box.Start + box.Length;
        var findingEnd = start + length;
        return start < boxEnd && box.Start < findingEnd;
    }

    /// <summary>
    /// Draws black bars over boxes that overlap a finding. Returns null when a finding has no box
    /// and <paramref name="refuseIfUncovered"/> is set, or when the picture cannot be read.
    /// </summary>
    public static byte[]? Cover(
        byte[] imageBytes,
        IReadOnlyList<OcrBox> boxes,
        IReadOnlyList<FindingRef> findings,
        string extension,
        bool refuseIfUncovered)
    {
        if (refuseIfUncovered && findings.Count > 0 && !Covers(boxes, findings))
            return null;

        try
        {
            using var sourceStream = new MemoryStream(imageBytes);
            using var loaded = new Bitmap(sourceStream);
            using var bitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
                using var brush = new SolidBrush(Color.Black);
                foreach (var box in boxes)
                {
                    if (!findings.Any(finding => Overlaps(box, finding.Start, finding.Length)))
                        continue;
                    var x = Math.Max(0, (int)box.X - 2);
                    var y = Math.Max(0, (int)box.Y - 2);
                    var width = Math.Min(bitmap.Width - x, (int)box.Width + 4);
                    var height = Math.Min(bitmap.Height - y, (int)box.Height + 4);
                    if (width > 0 && height > 0)
                        graphics.FillRectangle(brush, x, y, width, height);
                }
            }

            using var output = new MemoryStream();
            bitmap.Save(output, FormatFor(extension));
            return output.ToArray();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ImageFormat FormatFor(string extension)
    {
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Jpeg;
        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Bmp;
        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Gif;
        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Tiff;
        return ImageFormat.Png;
    }
}

public readonly record struct FindingRef(int Start, int Length);
