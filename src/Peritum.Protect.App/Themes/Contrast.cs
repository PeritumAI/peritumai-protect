using System.Globalization;

namespace Peritum.Protect.App.Themes;

public static class Contrast
{
    public static double Ratio(string foregroundHex, string backgroundHex)
    {
        var lighter = Math.Max(Luminance(foregroundHex), Luminance(backgroundHex));
        var darker = Math.Min(Luminance(foregroundHex), Luminance(backgroundHex));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(int value)
    {
        var s = value / 255d;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static (int R, int G, int B) Parse(string hex)
    {
        var text = hex.Trim().TrimStart('#');
        return (
            int.Parse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
