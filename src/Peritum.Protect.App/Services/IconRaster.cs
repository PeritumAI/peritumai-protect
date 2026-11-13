using System.Drawing;
using System.Drawing.Drawing2D;

namespace Peritum.Protect.App.Services;

/// <summary>
/// Draws the decided shield-clipboard icon offline. Shapes follow assets/icon SVGs (64-unit grid).
/// No network and no extra package.
/// </summary>
public static class IconRaster
{
    private static readonly Color Cyan = Color.FromArgb(34, 211, 238);
    private static readonly Color Purple = Color.FromArgb(139, 92, 246);
    private static readonly Color NavyTop = Color.FromArgb(42, 58, 114);
    private static readonly Color NavyBottom = Color.FromArgb(11, 18, 54);
    private static readonly Color Ink = Color.FromArgb(7, 19, 41);
    private static readonly Color Line = Color.FromArgb(159, 179, 209);
    private static readonly Color GreyLight = Color.FromArgb(156, 163, 175);
    private static readonly Color GreyDark = Color.FromArgb(107, 114, 128);

    public static Bitmap Draw(int size, bool small, TrayState state)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.ScaleTransform(size / 64f, size / 64f);

        if (small)
            DrawSmall(g, state);
        else
            DrawFull(g, state);

        if (state == TrayState.Paused)
            DrawPauseBadge(g);
        else if (state == TrayState.Warning)
            DrawWarningBadge(g);
        return bmp;
    }

    private static void DrawFull(Graphics g, TrayState state)
    {
        using var shield = Shield(32, 7, 56, 15, 31, (56, 45.5f), (46, 56), (32, 60.5f), (18, 56), (8, 45.5f), 8);
        FillBody(g, shield, state, 7, 60.5f);

        using var bubble = Bubble(15.5f, 20, 48.5f, 41, 3.5f, (31, 41), (24, 48), (24, 41));
        using (var paper = new SolidBrush(Color.White))
            g.FillPath(paper, bubble);

        FillClip(g, new RectangleF(22, 2, 20, 10), 4, 2f, state);
        FillRound(g, new RectangleF(20, 25, 24, 3.5f), 1.75f, Line);
        FillRound(g, new RectangleF(20, 31, 24, 7), 2, Ink);
    }

    private static void DrawSmall(Graphics g, TrayState state)
    {
        using var shield = Shield(32, 8, 59, 17, 32, (59, 47), (48, 57.5f), (32, 62), (16, 57.5f), (5, 47), 5);
        FillBody(g, shield, state, 8, 62);

        using var bubble = Bubble(13, 21, 51, 43, 4, (33, 43), (24, 51), (24, 43));
        using (var paper = new SolidBrush(Color.White))
            g.FillPath(paper, bubble);

        FillClip(g, new RectangleF(20, 1, 24, 12), 5, 2.5f, state);
        FillRound(g, new RectangleF(17, 27, 30, 10), 3, Ink);
    }

    /// <summary>Shield: top point, straight right edge, two curves down to the tip and back, straight left edge.</summary>
    private static GraphicsPath Shield(
        float topX, float topY, float right, float shoulderY, float sideEndY,
        (float X, float Y) c1, (float X, float Y) c2, (float X, float Y) tip,
        (float X, float Y) c3, (float X, float Y) c4, float left)
    {
        var path = new GraphicsPath();
        path.AddLine(topX, topY, right, shoulderY);
        path.AddLine(right, shoulderY, right, sideEndY);
        path.AddBezier(right, sideEndY, c1.X, c1.Y, c2.X, c2.Y, tip.X, tip.Y);
        path.AddBezier(tip.X, tip.Y, c3.X, c3.Y, c4.X, c4.Y, left, sideEndY);
        path.AddLine(left, sideEndY, left, shoulderY);
        path.CloseFigure();
        return path;
    }

    /// <summary>Rounded rectangle with a speech-bubble tail pointing down-left.</summary>
    private static GraphicsPath Bubble(
        float left, float top, float right, float bottom, float r,
        (float X, float Y) tailStart, (float X, float Y) tailTip, (float X, float Y) tailEnd)
    {
        var path = new GraphicsPath();
        var d = r * 2;
        path.AddArc(left, top, d, d, 180, 90);
        path.AddArc(right - d, top, d, d, 270, 90);
        path.AddArc(right - d, bottom - d, d, d, 0, 90);
        path.AddLine(right - r, bottom, tailStart.X, tailStart.Y);
        path.AddLine(tailStart.X, tailStart.Y, tailTip.X, tailTip.Y);
        path.AddLine(tailTip.X, tailTip.Y, tailEnd.X, tailEnd.Y);
        path.AddArc(left, bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillBody(Graphics g, GraphicsPath shield, TrayState state, float top, float bottom)
    {
        var box = new RectangleF(0, top, 64, bottom - top);
        using (var body = state == TrayState.Paused
                   ? new LinearGradientBrush(box, GreyLight, GreyDark, 45f)
                   : new LinearGradientBrush(box, Cyan, Purple, 45f))
            g.FillPath(body, shield);

        if (state == TrayState.Paused)
            return;
        using var gloss = new LinearGradientBrush(
            new RectangleF(0, top, 64, (bottom - top) / 2 + 0.5f),
            Color.FromArgb(77, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            90f);
        var clip = g.Clip;
        g.SetClip(new RectangleF(0, top, 64, (bottom - top) / 2));
        g.FillPath(gloss, shield);
        g.Clip = clip;
    }

    private static void FillClip(Graphics g, RectangleF rect, float r, float stroke, TrayState state)
    {
        using var path = RoundRect(rect, r);
        using (var fill = state == TrayState.Paused
                   ? (Brush)new SolidBrush(Color.FromArgb(75, 85, 99))
                   : new LinearGradientBrush(rect, NavyTop, NavyBottom, 90f))
            g.FillPath(fill, path);
        using var pen = new Pen(Color.White, stroke);
        g.DrawPath(pen, path);
    }

    private static void DrawPauseBadge(Graphics g)
    {
        using (var fill = new SolidBrush(Color.FromArgb(55, 65, 81)))
            g.FillEllipse(fill, 33, 33, 30, 30);
        using (var pen = new Pen(Color.White, 3))
            g.DrawEllipse(pen, 33, 33, 30, 30);
        FillRound(g, new RectangleF(41.5f, 40, 4.5f, 16), 1.5f, Color.White);
        FillRound(g, new RectangleF(50, 40, 4.5f, 16), 1.5f, Color.White);
    }

    private static void DrawWarningBadge(Graphics g)
    {
        using (var fill = new SolidBrush(Color.FromArgb(245, 158, 11)))
            g.FillEllipse(fill, 36.5f, 2.5f, 27, 27);
        using (var pen = new Pen(Ink, 3))
            g.DrawEllipse(pen, 36.5f, 2.5f, 27, 27);
        FillRound(g, new RectangleF(48, 7.5f, 4, 11), 2, Ink);
        using var dot = new SolidBrush(Ink);
        g.FillEllipse(dot, 47.6f, 20.6f, 4.8f, 4.8f);
    }

    private static void FillRound(Graphics g, RectangleF rect, float r, Color color)
    {
        using var path = RoundRect(rect, r);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundRect(RectangleF rect, float r)
    {
        var path = new GraphicsPath();
        var d = Math.Min(r * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static byte[] Png(int size, bool small, TrayState state)
    {
        using var bmp = Draw(size, small, state);
        using var stream = new MemoryStream();
        bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    public static byte[] Ico(IReadOnlyList<(int Size, bool Small, TrayState State)> images)
    {
        var pngs = images.Select(image => Png(image.Size, image.Small, image.State)).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngs.Count);
        var offset = 6 + (16 * pngs.Count);
        for (var i = 0; i < pngs.Count; i++)
        {
            var size = images[i].Size >= 256 ? (byte)0 : (byte)images[i].Size;
            writer.Write(size);
            writer.Write(size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs)
            writer.Write(png);
        return stream.ToArray();
    }

    public static void WriteRepo(string repoRoot)
    {
        Directory.CreateDirectory(repoRoot);
        File.WriteAllBytes(Path.Combine(repoRoot, "PeritumProtect.png"), Png(512, false, TrayState.On));
        File.WriteAllBytes(Path.Combine(repoRoot, "PeritumProtect.ico"), AppIco());
        var iconDir = Path.Combine(repoRoot, "assets", "icon");
        Directory.CreateDirectory(iconDir);
        File.WriteAllBytes(Path.Combine(iconDir, "tray-on.ico"), TrayIco(TrayState.On));
        File.WriteAllBytes(Path.Combine(iconDir, "tray-paused.ico"), TrayIco(TrayState.Paused));
        File.WriteAllBytes(Path.Combine(iconDir, "tray-warning.ico"), TrayIco(TrayState.Warning));
    }

    public static byte[] AppIco() => Ico(
    [
        (16, true, TrayState.On),
        (20, true, TrayState.On),
        (24, true, TrayState.On),
        (32, false, TrayState.On),
        (40, false, TrayState.On),
        (48, false, TrayState.On),
        (64, false, TrayState.On),
        (256, false, TrayState.On)
    ]);

    public static byte[] TrayIco(TrayState state) => Ico(
    [
        (16, true, state),
        (20, true, state),
        (24, true, state),
        (32, true, state)
    ]);
}

public enum TrayState
{
    On,
    Paused,
    Warning
}
