using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Scanning;

public static class RuleLabels
{
    public static string Name(Finding finding)
    {
        var key = NameKey(finding);
        var text = TextCatalog.Get(key);
        if (!text.Equals(key, StringComparison.Ordinal))
            return text;
        return string.IsNullOrWhiteSpace(finding.DisplayLabel) ? finding.Label : finding.DisplayLabel;
    }

    public static string Count(Finding finding, int count)
    {
        var key = NameKey(finding);
        var phrase = TextCatalog.Plural(key, count, ("count", count));
        if (!phrase.Equals(key, StringComparison.Ordinal))
            return phrase;
        return count + " " + Name(finding);
    }

    private static string NameKey(Finding finding)
    {
        if (finding.RuleId is RiskExplanations.HiddenContent or RiskExplanations.HiddenInstruction)
        {
            var reason = RiskExplanations.Match(finding.Label) ?? RiskExplanations.Match(finding.DisplayLabel);
            if (reason is not null)
                return "name." + reason;
        }

        return "name." + finding.RuleId;
    }
}
