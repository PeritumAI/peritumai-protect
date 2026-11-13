using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Peritum.Protect.App;

/// <summary>
/// Screenshot helper. Per-monitor DPI comes from Windows. When the monitor cannot be set to the
/// requested scale, a layout transform enlarges the window by the same factor.
/// </summary>
public static class UiScale
{
    public static double Capture { get; private set; } = 1;

    public static void Read(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            var name = args[i].Trim();
            if (name is not ("--layout-scale" or "/layout-scale"))
                continue;
            if (double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) &&
                scale is >= 1 and <= 3)
                Capture = scale;
        }
    }

    public static string[] Strip(string[] args)
    {
        var kept = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i].Trim();
            if (name is "--layout-scale" or "/layout-scale")
            {
                i++;
                continue;
            }
            kept.Add(args[i]);
        }
        return kept.ToArray();
    }

    public static void Apply(Window window)
    {
        if (Capture <= 1.01)
            return;
        window.Loaded += (_, _) => Fit(window);
    }

    private static void Fit(Window window)
    {
        if (window.Content is not FrameworkElement content)
            return;
        if (content.LayoutTransform is ScaleTransform)
            return;
        var width = content.RenderSize.Width;
        var height = content.RenderSize.Height;
        if (width < 40 || height < 40)
        {
            width = window.ActualWidth;
            height = window.ActualHeight;
        }
        if (width < 40 || height < 40)
            return;
        var work = SystemParameters.WorkArea;
        var chromeW = Math.Max(0, window.ActualWidth - content.RenderSize.Width);
        var chromeH = Math.Max(0, window.ActualHeight - content.RenderSize.Height);
        var maxInnerW = Math.Max(640, (work.Width - 24 - chromeW) / Capture);
        var maxInnerH = Math.Max(480, (work.Height - 24 - chromeH) / Capture);
        content.Width = Math.Min(width, maxInnerW);
        content.Height = Math.Min(height, maxInnerH);
        content.LayoutTransform = new ScaleTransform(Capture, Capture);
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = work.Left;
        window.Top = work.Top;
        window.SizeToContent = SizeToContent.WidthAndHeight;
    }
}
