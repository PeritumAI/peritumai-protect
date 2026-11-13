using System.Globalization;
using System.Text;

namespace Peritum.Protect.Core.Clipboard;

public static class RtfText
{
    public static string ToPlain(string? rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf))
            return "";
        var source = rtf.TrimStart();
        if (!source.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
            return "";

        var sb = new StringBuilder();
        var group = 0;
        var skipUntil = 0;
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '{')
            {
                group++;
                if (i + 2 < source.Length && source[i + 1] == '\\' && source[i + 2] == '*')
                    skipUntil = group;
                i++;
                continue;
            }

            if (c == '}')
            {
                if (skipUntil > 0 && group == skipUntil)
                    skipUntil = 0;
                group = Math.Max(0, group - 1);
                i++;
                continue;
            }

            if (skipUntil > 0)
            {
                i++;
                continue;
            }

            if (c == '\\')
            {
                i++;
                if (i >= source.Length)
                    break;
                var mark = source[i];
                if (mark is '\\' or '{' or '}')
                {
                    sb.Append(mark);
                    i++;
                    continue;
                }

                if (mark == '\'')
                {
                    if (i + 2 < source.Length && byte.TryParse(source.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                        sb.Append((char)b);
                    i += 3;
                    continue;
                }

                if (mark == 'u' && i + 1 < source.Length && (char.IsDigit(source[i + 1]) || source[i + 1] == '-'))
                {
                    i++;
                    var sign = 1;
                    if (source[i] == '-')
                    {
                        sign = -1;
                        i++;
                    }

                    var number = 0;
                    while (i < source.Length && char.IsDigit(source[i]))
                    {
                        number = number * 10 + (source[i] - '0');
                        i++;
                    }

                    var code = sign * number;
                    if (code < 0)
                        code += 65536;
                    if (code is >= 0 and <= 0xFFFF)
                        sb.Append((char)code);
                    if (i < source.Length && source[i] == '?')
                        i++;
                    else if (i < source.Length && source[i] is not '\\' and not '{' and not '}' and not ' ')
                        i++;
                    continue;
                }

                var wordStart = i;
                while (i < source.Length && char.IsLetter(source[i]))
                    i++;
                var word = source[wordStart..i];
                if (i < source.Length && source[i] == '-')
                    i++;
                while (i < source.Length && char.IsDigit(source[i]))
                    i++;
                if (i < source.Length && source[i] == ' ')
                    i++;
                if (word is "par" or "line")
                    sb.Append('\n');
                else if (word == "tab")
                    sb.Append('\t');
                continue;
            }

            if (c is '\r' or '\n')
            {
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString().Trim();
    }
}
