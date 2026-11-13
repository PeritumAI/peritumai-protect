namespace Peritum.Protect.Core.Scanning;

public static class ValueMask
{
    public static string Mask(string value, string? ruleId = null)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var id = ruleId ?? "";
        if (id.Contains("email", StringComparison.OrdinalIgnoreCase) || value.Contains('@'))
            return MaskEmail(value);
        if (id.Contains("card", StringComparison.OrdinalIgnoreCase) || id.Contains("luhn", StringComparison.OrdinalIgnoreCase))
            return MaskCard(value);
        if (id.Contains("iban", StringComparison.OrdinalIgnoreCase))
            return MaskEdges(Digits(value), 4, 4);
        if (id.Contains("phone", StringComparison.OrdinalIgnoreCase))
            return MaskEdges(Digits(value), 0, 2);
        return MaskSecret(value);
    }

    public static bool HidesFullValue(string value, string masked) =>
        string.IsNullOrEmpty(value) || !string.Equals(value, masked, StringComparison.Ordinal);

    private static string MaskEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0)
            return MaskSecret(value);
        var local = value[..at];
        var domain = value[(at + 1)..];
        var head = local.Length == 0 ? "*" : local[..1] + "***";
        return head + "@" + domain;
    }

    private static string MaskCard(string value)
    {
        var digits = Digits(value);
        if (digits.Length < 4)
            return "****";
        return "**** **** **** " + digits[^4..];
    }

    private static string MaskSecret(string value)
    {
        if (value.Length <= 4)
            return new string('*', value.Length);
        return value[..Math.Min(3, value.Length)] + "…" + value[^2..];
    }

    private static string MaskEdges(string digits, int head, int tail)
    {
        if (digits.Length <= head + tail)
            return new string('*', Math.Max(digits.Length, 4));
        return digits[..head] + new string('*', digits.Length - head - tail) + digits[^tail..];
    }

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
}
