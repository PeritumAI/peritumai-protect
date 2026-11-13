using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Scanning;

public static class CryptoWatch
{
    private static readonly Regex Eth = new(@"^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    private static readonly Regex Btc = new(@"^(bc1|[13])[A-Za-z0-9]{20,}$", RegexOptions.Compiled);

    public static string? Kind(string text)
    {
        var value = text.Trim();
        if (Eth.IsMatch(value))
            return "eth";
        if (Btc.IsMatch(value))
            return "btc";
        return null;
    }

    public static bool WasSwapped(string? previous, string? current, TimeSpan gap, bool sameWindow, bool writtenByUs)
    {
        if (writtenByUs || !sameWindow || gap > TimeSpan.FromSeconds(2))
            return false;
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
            return false;
        var left = Kind(previous);
        var right = Kind(current);
        return left is not null && left == right && !string.Equals(previous.Trim(), current.Trim(), StringComparison.Ordinal);
    }
}
