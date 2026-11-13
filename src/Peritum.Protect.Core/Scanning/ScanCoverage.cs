namespace Peritum.Protect.Core.Scanning;

public static class ScanCoverage
{
    public const string EntropyRuleId = "secret.entropy";
    public const string EntropySkipNote = "High-entropy check skipped for part of this file.";
    public const int EntropyGiveUpLength = 100_000;

    public static bool OnlyEntropyIncomplete(bool incomplete, IEnumerable<string> failedRuleIds)
    {
        if (!incomplete)
            return false;
        var any = false;
        foreach (var id in failedRuleIds)
        {
            any = true;
            if (!string.Equals(id, EntropyRuleId, StringComparison.Ordinal))
                return false;
        }
        return any;
    }
}
