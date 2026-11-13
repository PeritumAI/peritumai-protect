using System.Globalization;

namespace Peritum.Protect.Core.Links;

/// <summary>
/// Longest-match registrable domain. Multi-label suffixes come from a reviewed subset.
/// Anything else uses its final label, which is the public-suffix default rule.
/// </summary>
public static class PublicSuffixList
{
    public const string AsOf = "2026-10-01";

    private static readonly HashSet<string> Suffixes = Load();

    public static string? RegistrableDomain(string? host)
    {
        var ascii = Confusables.ToAscii(host);
        if (ascii.Length == 0)
            return null;
        var labels = ascii.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
            return null;

        var suffixLabels = 1;
        for (var length = Math.Min(labels.Length - 1, 4); length >= 2; length--)
        {
            var suffix = string.Join('.', labels[^length..]);
            if (Suffixes.Contains(suffix))
            {
                suffixLabels = length;
                break;
            }
        }

        if (labels.Length <= suffixLabels)
            return null;
        return string.Join('.', labels[^(suffixLabels + 1)..]);
    }

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var stream = typeof(PublicSuffixList).Assembly.GetManifestResourceStream("links.public_suffixes.txt")
            ?? throw new InvalidOperationException("Missing links.public_suffixes.txt");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var text = line.Trim().ToLowerInvariant();
            if (text.Length == 0 || text.StartsWith('#'))
                continue;
            set.Add(text);
        }

        return set;
    }
}

static class Confusables
{
    private static readonly Dictionary<int, char> Map = Build();

    public static string ToAscii(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";
        var trimmed = host.Trim().Trim('.').Trim();
        if (trimmed.Length == 0)
            return "";
        try
        {
            return new IdnMapping().GetAscii(trimmed).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return trimmed.ToLowerInvariant();
        }
    }

    public static string ToUnicode(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return "";
        try
        {
            return new IdnMapping().GetUnicode(host.Trim().Trim('.'));
        }
        catch (ArgumentException)
        {
            return host.Trim().Trim('.');
        }
    }

    public static string Fold(string value)
    {
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            chars[i] = Map.TryGetValue(ch, out var mapped) ? mapped : char.ToLowerInvariant(ch);
        }

        return new string(chars);
    }

    public static bool MixedScript(string label)
    {
        var latin = false;
        var cyrillic = false;
        var greek = false;
        foreach (var ch in label)
        {
            if (ch is '-' or '_' or >= '0' and <= '9')
                continue;
            if (IsLatin(ch))
                latin = true;
            else if (ch is >= '\u0400' and <= '\u052F')
                cyrillic = true;
            else if (ch is >= '\u0370' and <= '\u03FF')
                greek = true;
        }

        var scripts = (latin ? 1 : 0) + (cyrillic ? 1 : 0) + (greek ? 1 : 0);
        return scripts >= 2;
    }

    private static bool IsLatin(char ch) =>
        ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '\u00C0' and <= '\u024F');

    private static Dictionary<int, char> Build()
    {
        var map = new Dictionary<int, char>();
        void Add(string from, char to)
        {
            foreach (var ch in from)
                map[ch] = to;
        }

        Add("Аа", 'a');
        Add("Вв", 'b');
        Add("ЕеЄє", 'e');
        Add("Кк", 'k');
        Add("Мм", 'm');
        Add("НнҺһ", 'h');
        Add("ОоӨө", 'o');
        Add("Рр", 'p');
        Add("Сс", 'c');
        Add("Тт", 't');
        Add("Уу", 'y');
        Add("Хх", 'x');
        Add("ІӀӏ", 'l');
        Add("і", 'i');
        Add("Јј", 'j');
        Add("Ѕѕ", 's');
        Add("ԁ", 'd');
        Add("Ԛԛ", 'q');
        Add("Ԝԝ", 'w');
        Add("Ѵѵ", 'v');
        Add("Αα", 'a');
        Add("Β", 'b');
        Add("Εε", 'e');
        Add("Ζ", 'z');
        Add("Η", 'h');
        Add("Ιι", 'i');
        Add("Κκ", 'k');
        Add("Μ", 'm');
        Add("Νν", 'v');
        Add("Οο", 'o');
        Add("Ρρ", 'p');
        Add("Ττ", 't');
        Add("Υυ", 'y');
        Add("Χ", 'x');
        map['I'] = 'l';
        map['|'] = 'l';
        map['ı'] = 'i';
        return map;
    }
}
