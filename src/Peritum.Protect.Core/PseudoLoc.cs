using System.Text;

namespace Peritum.Protect.Core;

public static class PseudoLoc
{
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Contains('|'))
            return text;
        var kept = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    kept.Add(text[i..(end + 1)]);
                    sb.Append('\u0001');
                    i = end;
                    continue;
                }
            }
            sb.Append(Accent(text[i]));
        }
        var extra = (int)Math.Ceiling(text.Length * 0.4);
        sb.Append('~', extra);
        var body = sb.ToString();
        var slot = 0;
        var restored = new StringBuilder();
        foreach (var ch in body)
        {
            if (ch == '\u0001' && slot < kept.Count)
                restored.Append(kept[slot++]);
            else
                restored.Append(ch);
        }
        return "[" + restored + "]";
    }

    private static char Accent(char c) => c switch
    {
        'a' => 'à',
        'e' => 'é',
        'i' => 'í',
        'o' => 'ö',
        'u' => 'ü',
        'A' => 'Å',
        'E' => 'É',
        'I' => 'Í',
        'O' => 'Ö',
        'U' => 'Ü',
        'c' => 'ç',
        'n' => 'ñ',
        'y' => 'ý',
        _ => c
    };
}
