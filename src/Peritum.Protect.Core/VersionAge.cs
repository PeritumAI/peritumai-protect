namespace Peritum.Protect.Core;

public static class VersionAge
{
    public const string ProductUrl = "https://peritumai.com/protect";
    public const int QuietAfterDays = 180;

    public static int DaysOld(DateTime buildDate, DateTime today)
    {
        var days = (today.Date - buildDate.Date).Days;
        return days < 0 ? 0 : days;
    }

    public static string AgeLine(DateTime buildDate, DateTime today)
    {
        var days = DaysOld(buildDate, today);
        return TextCatalog.Plural("versionAge", days, ("count", days));
    }

    public static bool ShowQuietLine(DateTime buildDate, DateTime today) =>
        DaysOld(buildDate, today) > QuietAfterDays;

    public const string QuietLine = "This version is more than 180 days old.";
}
