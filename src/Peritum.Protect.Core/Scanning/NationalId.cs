using System.Text.RegularExpressions;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Scanning;

/// <summary>
/// National identity numbers. Checksums follow the published rule for each number.
/// Callers pass synthetic values in tests. This type does not store a matched value.
/// </summary>
public static class NationalId
{
    public static void Collect(string text, Rule rule, List<(int Start, int Length)> hits)
    {
        var algorithm = rule.Algorithm;
        if (algorithm.Length == 0 && rule.Kind == "jmbg")
            algorithm = "jmbg";
        switch (algorithm)
        {
            case "jmbg":
                CollectDigits(text, 13, hits, ValidJmbg);
                break;
            case "jmbg-ba":
                CollectDigits(text, 13, hits, value => ValidJmbg(value) && (RegionIn(value, 1) || RegionBetween(value, 10, 19)));
                break;
            case "jmbg-me":
                CollectDigits(text, 13, hits, value => ValidJmbg(value) && (RegionIn(value, 2) || RegionBetween(value, 21, 29)));
                break;
            case "jmbg-si":
                CollectDigits(text, 13, hits, value => ValidJmbg(value) && (RegionIn(value, 5) || RegionBetween(value, 50, 59)));
                break;
            case "jmbg-mk":
                CollectDigits(text, 13, hits, value => ValidJmbg(value) && (RegionIn(value, 4) || RegionBetween(value, 41, 49)));
                break;
            case "pib":
                CollectDigits(text, 9, hits, ValidPib);
                break;
            case "oib":
                CollectDigits(text, 11, hits, ValidOib);
                break;
            case "steuerid":
                CollectDigits(text, 11, hits, ValidSteuerId);
                CollectShaped(text, hits, @"(?<!\d)\d{2} \d{3} \d{3} \d{3}(?!\d)", value => ValidSteuerId(Digits(value)));
                break;
            case "bsn":
                CollectDigits(text, 9, hits, ValidBsn);
                CollectShaped(text, hits, @"(?<!\d)\d{4}\.\d{2}\.\d{3}(?!\d)", value => ValidBsn(Digits(value)));
                break;
            case "pesel":
                CollectDigits(text, 11, hits, ValidPesel);
                break;
            case "nhs":
                CollectDigits(text, 10, hits, ValidNhs);
                CollectShaped(text, hits, @"(?<!\d)\d{3} \d{3} \d{4}(?!\d)", value => ValidNhs(Digits(value)));
                CollectShaped(text, hits, @"(?<!\d)\d{3}-\d{3}-\d{4}(?!\d)", value => ValidNhs(Digits(value)));
                break;
            case "nir":
                CollectPattern(text, hits, @"(?<![A-Z0-9])[123478]\d{2}(?:0[1-9]|1[0-2]|[2-9]\d)(?:\d{8}|2[AB]\d{6})\d{2}(?![A-Z0-9])", ValidNir, ignoreCase: true);
                CollectShaped(text, hits, @"(?<![A-Z0-9])[123478] \d{2} (?:0[1-9]|1[0-2]|[2-9]\d) (?:\d{2}|2[AB]) \d{3} \d{3} \d{2}(?![A-Z0-9])", ValidNir, ignoreCase: true);
                break;
            case "dni":
                CollectPattern(text, hits, @"(?<![A-Z0-9])\d{8}[A-Z](?![A-Z0-9])", ValidDni, ignoreCase: true);
                break;
            case "nie":
                CollectPattern(text, hits, @"(?<![A-Z0-9])[XYZ]\d{7}[A-Z](?![A-Z0-9])", ValidNie, ignoreCase: true);
                break;
            case "codice":
                CollectPattern(text, hits, @"(?<![A-Z0-9])[A-Z]{6}\d{2}[ABCDEHLMPRST]\d{2}[A-Z]\d{3}[A-Z](?![A-Z0-9])", ValidCodice, ignoreCase: true);
                break;
            case "nino":
                CollectPattern(text, hits, @"(?<![A-Z0-9])[A-Z]{2}\d{6}[A-D](?![A-Z0-9])", ValidNino, ignoreCase: true);
                CollectShaped(text, hits, @"(?<![A-Z0-9])[A-Z]{2} \d{2} \d{2} \d{2} [A-D](?![A-Z0-9])", ValidNino, ignoreCase: true);
                break;
            case "ssn":
                CollectPattern(text, hits, @"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)", ValidSsn, ignoreCase: false);
                CollectDigits(text, 9, hits, ValidSsn);
                break;
            case "itin":
                CollectPattern(text, hits, @"(?<!\d)9\d{2}-\d{2}-\d{4}(?!\d)", ValidItin, ignoreCase: false);
                CollectDigits(text, 9, hits, ValidItin);
                break;
            case "cpf":
                CollectDigits(text, 11, hits, ValidCpf);
                CollectShaped(text, hits, @"(?<!\d)\d{3}\.\d{3}\.\d{3}-\d{2}(?!\d)", value => ValidCpf(Digits(value)));
                break;
            case "cnpj":
                CollectDigits(text, 14, hits, ValidCnpj);
                CollectShaped(text, hits, @"(?<!\d)\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}(?!\d)", value => ValidCnpj(Digits(value)));
                break;
            case "aadhaar":
                CollectDigits(text, 12, hits, ValidAadhaar);
                CollectShaped(text, hits, @"(?<!\d)\d{4} \d{4} \d{4}(?!\d)", value => ValidAadhaar(Digits(value)));
                break;
            case "pan":
                CollectPattern(text, hits, @"(?<![A-Z0-9])[A-Z]{3}[ABCFGHLJPT][A-Z]\d{4}[A-Z](?![A-Z0-9])", static value => value.Length == 10, ignoreCase: true);
                break;
        }
    }

    public static bool ValidJmbg(string value)
    {
        if (value.Length != 13 || !AllDigits(value))
            return false;
        var day = int.Parse(value[..2]);
        var month = int.Parse(value.Substring(2, 2));
        if (day is < 1 or > 31 || month is < 1 or > 12)
            return false;
        var check = JmbgCheck(value[..12]);
        return check is not null && check == value[12] - '0';
    }

    public static string? WithJmbgCheck(string first12)
    {
        var check = JmbgCheck(first12);
        return check is null ? null : first12 + check.Value;
    }

    public static bool ValidPib(string value) =>
        value.Length == 9 && value[0] != '0' && AllDigits(value) && Iso7064Check(value[..8]) == value[8] - '0';

    public static string WithPibCheck(string first8) => first8 + Iso7064Check(first8);

    public static bool ValidOib(string value) =>
        value.Length == 11 && AllDigits(value) && Iso7064Check(value[..10]) == value[10] - '0';

    public static string WithOibCheck(string first10) => first10 + Iso7064Check(first10);

    public static bool ValidSteuerId(string value)
    {
        if (value.Length != 11 || value[0] == '0' || !AllDigits(value))
            return false;
        if (!SteuerBody(value[..10]))
            return false;
        return Iso7064Check(value[..10]) == value[10] - '0';
    }

    public static bool ValidBsn(string value)
    {
        if (value.Length != 9 || !AllDigits(value))
            return false;
        var sum = 0;
        for (var i = 0; i < 8; i++)
            sum += (value[i] - '0') * (9 - i);
        sum -= value[8] - '0';
        return sum > 0 && sum % 11 == 0;
    }

    public static bool ValidPesel(string value)
    {
        if (value.Length != 11 || !AllDigits(value) || !PeselDate(value))
            return false;
        var weights = new[] { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
        var sum = 0;
        for (var i = 0; i < 10; i++)
            sum += (value[i] - '0') * weights[i];
        var check = (10 - (sum % 10)) % 10;
        return check == value[10] - '0';
    }

    public static bool ValidNhs(string value)
    {
        if (value.Length != 10 || !AllDigits(value) || SameDigit(value))
            return false;
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (value[i] - '0') * (10 - i);
        var check = 11 - (sum % 11);
        if (check == 11)
            check = 0;
        if (check == 10)
            return false;
        return check == value[9] - '0';
    }

    public static bool ValidNir(string value)
    {
        var text = value.Replace(" ", "").ToUpperInvariant();
        if (text.Length != 15 || text[0] is not ('1' or '2' or '3' or '4' or '7' or '8'))
            return false;
        if (!int.TryParse(text.AsSpan(3, 2), out var month) || month is 0 or (> 12 and < 20))
            return false;
        var number = NirNumber(text[..13]);
        if (number is null)
            return false;
        var key = 97 - (number.Value % 97);
        return text[13..] == key.ToString("00");
    }

    public static bool ValidDni(string value)
    {
        var text = value.ToUpperInvariant();
        if (text.Length != 9 || !AllDigits(text[..8]) || !char.IsLetter(text[8]))
            return false;
        return text[8] == DniLetter(int.Parse(text[..8]));
    }

    public static bool ValidNie(string value)
    {
        var text = value.ToUpperInvariant();
        if (text.Length != 9 || text[0] is not ('X' or 'Y' or 'Z') || !AllDigits(text.Substring(1, 7)) || !char.IsLetter(text[8]))
            return false;
        var prefix = text[0] switch { 'X' => "0", 'Y' => "1", _ => "2" };
        return text[8] == DniLetter(int.Parse(prefix + text.Substring(1, 7)));
    }

    public static bool ValidCodice(string value)
    {
        var text = value.ToUpperInvariant();
        if (text.Length != 16)
            return false;
        const string months = "ABCDEHLMPRST";
        if (!months.Contains(text[8]))
            return false;
        if (!int.TryParse(text.AsSpan(9, 2), out var day))
            return false;
        var birthDay = day > 40 ? day - 40 : day;
        if (birthDay is < 1 or > 31)
            return false;
        var sum = 0;
        for (var i = 0; i < 15; i++)
            sum += (i % 2 == 0) ? CodiceOdd(text[i]) : CodiceEven(text[i]);
        return text[15] == (char)('A' + (sum % 26));
    }

    public static bool ValidNino(string value)
    {
        var text = value.Replace(" ", "").ToUpperInvariant();
        if (text.Length != 9 || !char.IsLetter(text[0]) || !char.IsLetter(text[1]))
            return false;
        const string badFirst = "DFIQUV";
        const string badSecond = "DFIOQUV";
        if (badFirst.Contains(text[0]) || badSecond.Contains(text[1]))
            return false;
        var prefix = text[..2];
        if (prefix is "BG" or "GB" or "NK" or "KN" or "TN" or "NT" or "ZZ")
            return false;
        if (!AllDigits(text.Substring(2, 6)))
            return false;
        return text[8] is 'A' or 'B' or 'C' or 'D';
    }

    public static bool ValidSsn(string value)
    {
        var digits = Digits(value);
        if (digits.Length != 9 || digits == "078051120")
            return false;
        var area = int.Parse(digits[..3]);
        var group = int.Parse(digits.Substring(3, 2));
        var serial = int.Parse(digits.Substring(5, 4));
        return area is not (0 or 666 or >= 900) && group != 0 && serial != 0;
    }

    public static bool ValidItin(string value)
    {
        var digits = Digits(value);
        if (digits.Length != 9 || digits[0] != '9')
            return false;
        var group = int.Parse(digits.Substring(3, 2));
        return group is >= 50 and <= 65 or >= 70 and <= 88 or >= 90 and <= 92 or >= 94 and <= 99;
    }

    public static bool ValidCpf(string value)
    {
        if (value.Length != 11 || !AllDigits(value) || SameDigit(value))
            return false;
        return CpfDigit(value, 9) == value[9] - '0' && CpfDigit(value, 10) == value[10] - '0';
    }

    public static string WithCpf(string first9)
    {
        var first = first9 + CpfDigit(first9, 9);
        return first + CpfDigit(first, 10);
    }

    public static bool ValidCnpj(string value)
    {
        if (value.Length != 14 || !AllDigits(value) || SameDigit(value))
            return false;
        return CnpjDigit(value, 12) == value[12] - '0' && CnpjDigit(value, 13) == value[13] - '0';
    }

    public static string WithCnpj(string first12)
    {
        var first = first12 + CnpjDigit(first12, 12);
        return first + CnpjDigit(first, 13);
    }

    public static bool ValidAadhaar(string value) =>
        value.Length == 12 && AllDigits(value) && value[0] is >= '2' and <= '9' && Verhoeff(value);

    public static string WithAadhaar(string first11)
    {
        for (var digit = 0; digit <= 9; digit++)
        {
            var value = first11 + digit;
            if (Verhoeff(value))
                return value;
        }
        throw new InvalidOperationException("no Verhoeff digit");
    }

    private static string Digits(string value)
    {
        var digits = new char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (char.IsDigit(c))
                digits[n++] = c;
        }
        return new string(digits, 0, n);
    }

    private static int CpfDigit(string value, int length)
    {
        var weight = length + 1;
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += (value[i] - '0') * (weight - i);
        var digit = (sum * 10) % 11;
        return digit == 10 ? 0 : digit;
    }

    private static int CnpjDigit(string value, int length)
    {
        var weights = length == 12
            ? new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }
            : new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += (value[i] - '0') * weights[i];
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static bool Verhoeff(string value)
    {
        int[,] d =
        {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 1, 2, 3, 4, 0, 6, 7, 8, 9, 5 },
            { 2, 3, 4, 0, 1, 7, 8, 9, 5, 6 },
            { 3, 4, 0, 1, 2, 8, 9, 5, 6, 7 },
            { 4, 0, 1, 2, 3, 9, 5, 6, 7, 8 },
            { 5, 9, 8, 7, 6, 0, 4, 3, 2, 1 },
            { 6, 5, 9, 8, 7, 1, 0, 4, 3, 2 },
            { 7, 6, 5, 9, 8, 2, 1, 0, 4, 3 },
            { 8, 7, 6, 5, 9, 3, 2, 1, 0, 4 },
            { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 }
        };
        int[,] p =
        {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 1, 5, 7, 6, 2, 8, 3, 0, 9, 4 },
            { 5, 8, 0, 3, 7, 9, 6, 1, 4, 2 },
            { 8, 9, 1, 6, 0, 4, 3, 5, 2, 7 },
            { 9, 4, 5, 3, 1, 2, 6, 8, 7, 0 },
            { 4, 2, 8, 6, 5, 7, 3, 9, 0, 1 },
            { 2, 7, 9, 3, 8, 0, 6, 4, 1, 5 },
            { 7, 0, 4, 6, 9, 1, 3, 2, 5, 8 }
        };
        var checksum = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var digit = value[value.Length - 1 - i] - '0';
            checksum = d[checksum, p[i % 8, digit]];
        }
        return checksum == 0;
    }

    private static bool SteuerBody(string ten)
    {
        var counts = new int[10];
        foreach (var c in ten)
            counts[c - '0']++;
        var twice = 0;
        var thrice = 0;
        for (var digit = 0; digit < 10; digit++)
        {
            if (counts[digit] == 2)
                twice++;
            else if (counts[digit] == 3)
                thrice++;
            else if (counts[digit] > 3)
                return false;
        }
        if (twice == 1 && thrice == 0)
            return true;
        if (twice != 0 || thrice != 1)
            return false;
        for (var digit = 0; digit < 10; digit++)
        {
            if (counts[digit] != 3)
                continue;
            var previous = -2;
            for (var i = 0; i < ten.Length; i++)
            {
                if (ten[i] - '0' != digit)
                    continue;
                if (i == previous + 1)
                    return false;
                previous = i;
            }
        }
        return true;
    }

    private static bool PeselDate(string value)
    {
        var yearTwo = int.Parse(value[..2]);
        var monthCode = int.Parse(value.Substring(2, 2));
        var day = int.Parse(value.Substring(4, 2));
        var (century, month) = monthCode switch
        {
            >= 1 and <= 12 => (1900, monthCode),
            >= 21 and <= 32 => (2000, monthCode - 20),
            >= 41 and <= 52 => (2100, monthCode - 40),
            >= 61 and <= 72 => (2200, monthCode - 60),
            >= 81 and <= 92 => (1800, monthCode - 80),
            _ => (0, 0)
        };
        if (month is < 1 or > 12 || day < 1)
            return false;
        var year = century + yearTwo;
        return day <= DateTime.DaysInMonth(year, month);
    }

    private static long? NirNumber(string body)
    {
        if (body.Length != 13)
            return null;
        if (body[5] == '2' && body[6] is 'A' or 'B')
        {
            var replaced = body[..6] + "0" + body[7..];
            if (!long.TryParse(replaced, out var corsica))
                return null;
            return body[6] == 'A' ? corsica - 1_000_000 : corsica - 2_000_000;
        }
        if (!AllDigits(body) || !long.TryParse(body, out var number))
            return null;
        return number;
    }

    private static char DniLetter(int number) => "TRWAGMYFPDXBNJZSQVHLCKE"[number % 23];

    private static int CodiceOdd(char c)
    {
        if (c is >= '0' and <= '9')
            return (new[] { 1, 0, 5, 7, 9, 13, 15, 17, 19, 21 })[c - '0'];
        if (c is >= 'A' and <= 'Z')
            return (new[] { 1, 0, 5, 7, 9, 13, 15, 17, 19, 21, 2, 4, 18, 20, 11, 3, 6, 8, 12, 14, 16, 10, 22, 25, 24, 23 })[c - 'A'];
        return 0;
    }

    private static int CodiceEven(char c)
    {
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c is >= 'A' and <= 'Z')
            return c - 'A';
        return 0;
    }

    private static bool SameDigit(string value)
    {
        foreach (var c in value)
        {
            if (c != value[0])
                return false;
        }
        return true;
    }

    private static void CollectShaped(string text, List<(int Start, int Length)> hits, string pattern, Func<string, bool> valid, bool ignoreCase = false)
    {
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        foreach (Match match in Regex.Matches(text, pattern, options, TimeSpan.FromMilliseconds(200)))
        {
            if (!match.Success || !valid(match.Value))
                continue;
            hits.Add((match.Index, match.Length));
        }
    }

    private static void CollectPattern(string text, List<(int Start, int Length)> hits, string pattern, Func<string, bool> valid, bool ignoreCase)
    {
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;
        foreach (Match match in Regex.Matches(text, pattern, options, TimeSpan.FromMilliseconds(200)))
        {
            if (!match.Success || !valid(match.Value))
                continue;
            hits.Add((match.Index, match.Length));
        }
    }

    private static int? JmbgCheck(string first12)
    {
        if (first12.Length != 12 || !AllDigits(first12))
            return null;
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (first12[i] - '0') * (7 - (i % 6));
        var remainder = sum % 11;
        if (remainder == 1)
            return null;
        return remainder == 0 ? 0 : 11 - remainder;
    }

    private static int Iso7064Check(string body)
    {
        var product = 10;
        foreach (var c in body)
        {
            var sum = (c - '0' + product) % 10;
            if (sum == 0)
                sum = 10;
            product = (sum * 2) % 11;
        }
        return (11 - product) % 10;
    }

    private static bool RegionIn(string value, int region) => Region(value) == region;

    private static bool RegionBetween(string value, int min, int max)
    {
        var region = Region(value);
        return region >= min && region <= max;
    }

    private static int Region(string value) => int.Parse(value.Substring(7, 2));

    private static void CollectDigits(string text, int length, List<(int Start, int Length)> hits, Func<string, bool> valid)
    {
        for (var i = 0; i <= text.Length - length; i++)
        {
            if (i > 0 && char.IsDigit(text[i - 1]))
                continue;
            var digits = true;
            for (var n = 0; n < length; n++)
            {
                if (!char.IsDigit(text[i + n]))
                {
                    digits = false;
                    break;
                }
            }
            if (!digits)
                continue;
            if (i + length < text.Length && char.IsDigit(text[i + length]))
                continue;
            var value = text.Substring(i, length);
            if (!valid(value))
                continue;
            hits.Add((i, length));
            i += length - 1;
        }
    }

    private static bool AllDigits(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsDigit(c))
                return false;
        }
        return value.Length > 0;
    }
}
