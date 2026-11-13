using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Peritum.Protect.App.Native;

namespace Peritum.Protect.App;

public static class AppIcon
{
    private const int WmSetIcon = 0x0080;
    private static readonly Dictionary<Window, IntPtr[]> Handles = new();

    public static (int Small, int Big) FramesForDpi(double dpiScale)
    {
        var small = dpiScale >= 1.25 ? 20 : 16;
        var big = dpiScale >= 1.5 ? 48 : 32;
        return (small, big);
    }

    public static byte[] PngFrame(byte[] ico, int size)
    {
        if (ico.Length < 6)
            throw new InvalidDataException("Icon file is too small.");
        var count = BitConverter.ToUInt16(ico, 4);
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);
            if (entry + 16 > ico.Length)
                break;
            var width = ico[entry] == 0 ? 256 : ico[entry];
            var distance = Math.Abs(width - size);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry;
                if (distance == 0)
                    break;
            }
        }
        if (best < 0)
            throw new InvalidDataException("Icon file has no frames.");
        var length = BitConverter.ToInt32(ico, best + 8);
        var offset = BitConverter.ToInt32(ico, best + 12);
        if (offset < 0 || length < 8 || offset + length > ico.Length)
            throw new InvalidDataException("Icon frame is outside the file.");
        var png = new byte[length];
        Buffer.BlockCopy(ico, offset, png, 0, length);
        return png;
    }

    public static void Apply(Window window)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PeritumProtect.ico");
        if (!File.Exists(path))
            return;
        void Paint(double scale)
        {
            var bytes = File.ReadAllBytes(path);
            var (small, big) = FramesForDpi(scale);
            window.Icon = Bitmap(PngFrame(bytes, big));
            SetHwndIcons(window, bytes, small, big);
        }
        Paint(SystemDpiScale());
        window.SourceInitialized += (_, _) => Paint(CurrentScale(window));
        window.DpiChanged += (_, args) => Paint(args.NewDpi.DpiScaleX);
        window.Closed += (_, _) => Release(window);
    }

    private static double CurrentScale(Window window)
    {
        try
        {
            return VisualTreeHelper.GetDpi(window).DpiScaleX;
        }
        catch
        {
            return SystemDpiScale();
        }
    }

    private static double SystemDpiScale()
    {
        try
        {
            return GetDpiForSystem() / 96.0;
        }
        catch
        {
            return 1;
        }
    }

    private static void SetHwndIcons(Window window, byte[] ico, int small, int big)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var smallIcon = Hicon(PngFrame(ico, small));
        var captionIcon = Hicon(PngFrame(ico, small));
        var bigIcon = Hicon(PngFrame(ico, big));
        SendMessage(hwnd, WmSetIcon, IntPtr.Zero, smallIcon);
        SendMessage(hwnd, WmSetIcon, new IntPtr(1), bigIcon);
        SendMessage(hwnd, WmSetIcon, new IntPtr(2), captionIcon);
        Release(window);
        Handles[window] = [smallIcon, bigIcon, captionIcon];
    }

    private static void Release(Window window)
    {
        if (!Handles.Remove(window, out var icons))
            return;
        foreach (var handle in icons)
        {
            if (handle != IntPtr.Zero)
                DestroyIcon(handle);
        }
    }

    private static IntPtr Hicon(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new System.Drawing.Bitmap(stream);
        return bitmap.GetHicon();
    }

    private static BitmapSource Bitmap(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
