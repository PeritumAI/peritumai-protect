namespace Peritum.Protect.Core.Scanning;

public static class EntropyScanner
{
    public static IEnumerable<(int Start, int Length)> Find(string text, int minLength, double minBits, int maxHits = 40)
    {
        if (minLength < 8)
            minLength = 8;
        var i = 0;
        var yielded = 0;
        while (i < text.Length && yielded < maxHits)
        {
            if (!IsTokenChar(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && IsTokenChar(text[i]))
                i++;
            var len = i - start;
            if (len < minLength || SkipEntropyToken(text, start, len))
                continue;
            if (Shannon(text.AsSpan(start, len)) >= minBits)
            {
                yield return (start, len);
                yielded++;
            }
        }
    }

    public static double Shannon(ReadOnlySpan<char> value)
    {
        Span<int> counts = stackalloc int[128];
        var n = 0;
        foreach (var c in value)
        {
            if (c >= 128)
                continue;
            counts[c]++;
            n++;
        }
        if (n == 0)
            return 0;
        double entropy = 0;
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
                continue;
            var p = (double)counts[i] / n;
            entropy -= p * Math.Log(p, 2);
        }
        return entropy;
    }

    private static bool SkipEntropyToken(string text, int start, int length)
    {
        var value = text.AsSpan(start, length);
        var window = Math.Max(0, start - 96);
        if (text.AsSpan(window, start - window).Contains("base64,", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c) && c != '-')
                return false;
        }
        return true;
    }

    private static bool IsTokenChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '_' or '-';
}
