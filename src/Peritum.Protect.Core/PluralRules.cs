namespace Peritum.Protect.Core;

/// <summary>
/// CLDR cardinal plural categories for the languages the UI needs.
/// </summary>
public static class PluralRules
{
    public static string Category(string? language, long number)
    {
        var code = Base(language);
        if (code is "sr" or "hr" or "bs")
            return SouthSlavic(number);
        if (code is "ru" or "uk")
            return EastSlavic(number);
        if (code == "pl")
            return Polish(number);
        if (code == "ar")
            return Arabic(number);
        return number == 1 ? "one" : "other";
    }

    public static bool IsRightToLeft(string? language)
    {
        var code = Base(language);
        return code is "ar" or "he" or "fa" or "ur" || language == "qps-rtl";
    }

    private static string Base(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "en";
        var dash = language.IndexOf('-');
        var code = (dash > 0 ? language[..dash] : language).ToLowerInvariant();
        return code is "qps" ? "en" : code;
    }

    private static string SouthSlavic(long number)
    {
        var mod10 = number % 10;
        var mod100 = number % 100;
        if (mod10 == 1 && mod100 != 11)
            return "one";
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return "few";
        return "other";
    }

    private static string EastSlavic(long number)
    {
        var mod10 = number % 10;
        var mod100 = number % 100;
        if (mod10 == 1 && mod100 != 11)
            return "one";
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return "few";
        if (mod10 == 0 || mod10 is >= 5 and <= 9 || mod100 is >= 11 and <= 14)
            return "many";
        return "other";
    }

    private static string Polish(long number)
    {
        if (number == 1)
            return "one";
        var mod10 = number % 10;
        var mod100 = number % 100;
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return "few";
        return "many";
    }

    private static string Arabic(long number)
    {
        if (number == 0)
            return "zero";
        if (number == 1)
            return "one";
        if (number == 2)
            return "two";
        var mod100 = number % 100;
        if (mod100 is >= 3 and <= 10)
            return "few";
        if (mod100 is >= 11 and <= 99)
            return "many";
        return "other";
    }
}
