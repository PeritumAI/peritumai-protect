using System.Text;
using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Scanning;

public sealed class HiddenHit
{
    public required string Reason { get; init; }
    public required string Text { get; init; }
}

public static class HiddenText
{
    public static IReadOnlyList<HiddenHit> Scan(string text)
    {
        var hits = new List<HiddenHit>();
        if (string.IsNullOrEmpty(text))
            return hits;
        var zero = new StringBuilder();
        var tags = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF)
                zero.Append(rune.ToString());
            else if (c is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069)
                zero.Append(rune.ToString());
            if (c is >= 0xE0000 and <= 0xE007F)
                tags.Append((char)(c - 0xE0000));
        }
        if (zero.Length > 0)
            hits.Add(new HiddenHit { Reason = "Invisible Unicode", Text = zero.ToString() });
        if (tags.Length > 0)
            hits.Add(new HiddenHit { Reason = "Unicode tags", Text = tags.ToString() });
        if (LooksHomoglyph(text))
            hits.Add(new HiddenHit { Reason = "Mixed scripts in one word", Text = text });
        foreach (Match phrase in InstructionPhrases.Pattern.Matches(text))
            hits.Add(new HiddenHit { Reason = "Text addressed to an AI", Text = phrase.Value });
        return hits;
    }

    public static string StripInvisible(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (c is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF)
                continue;
            if (c is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069)
                continue;
            if (c is >= 0xE0000 and <= 0xE007F)
                continue;
            sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    private static bool LooksHomoglyph(string text)
    {
        foreach (var word in text.Split([' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var latin = word.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            var other = word.Any(c => c is >= '\u0400' and <= '\u04FF' or >= '\u0370' and <= '\u03FF');
            if (latin && other)
                return true;
        }
        return false;
    }
}

public static class InstructionPhrases
{
    public static readonly System.Text.RegularExpressions.Regex Pattern = new(
        @"ignore (?:all )?(?:previous|prior|above) instructions|disregard the above|you are now|system prompt|do not (?:tell|inform|reveal to) the user|new instructions",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
}
