using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Scanning;

public sealed class CategorySummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public int Count { get; init; }
    public string CountsText { get; init; } = "";
    public string Chip { get; init; } = "None";
    public string ChipText { get; init; } = "";
}

public static class ResultGroups
{
    public static readonly (string Id, string Title, string Description)[] Order =
    [
        ("personal", "Personal data", "Emails, phones, and identity numbers."),
        ("payment", "Payment and bank", "Cards and IBANs."),
        ("secrets", "Secrets and keys", "API keys, tokens, private keys, and passwords in config."),
        ("custom", "Your terms", "Words and patterns you added.")
    ];

    public static IReadOnlyList<CategorySummary> Build(IReadOnlyList<Finding> findings)
    {
        var list = new List<CategorySummary>();
        foreach (var (id, _, _) in Order)
        {
            var rows = findings.Where(f => CategoryOf(f) == id).ToList();
            var high = rows.Any(f => f.Severity is Severity.High or Severity.Block);
            var chip = rows.Count == 0 ? "None" : high ? "High" : "Found";
            list.Add(new CategorySummary
            {
                Id = id,
                Title = TextCatalog.Get("cat." + id + ".title"),
                Description = TextCatalog.Get("cat." + id + ".body"),
                Count = rows.Count,
                CountsText = rows.Count == 0 ? "" : CountText(rows),
                Chip = chip,
                ChipText = TextCatalog.Get("chip." + chip.ToLowerInvariant())
            });
        }

        var hiddenRows = findings.Where(f => CategoryOf(f) == "hidden").ToList();
        if (hiddenRows.Count > 0)
        {
            var high = hiddenRows.Any(f => f.Severity is Severity.High or Severity.Block);
            var hiddenChip = high ? "High" : "Found";
            list.Add(new CategorySummary
            {
                Id = "hidden",
                Title = TextCatalog.Get("cat.hidden.title"),
                Description = TextCatalog.Get("cat.hidden.body"),
                Count = hiddenRows.Count,
                CountsText = CountText(hiddenRows),
                Chip = hiddenChip,
                ChipText = TextCatalog.Get("chip." + hiddenChip.ToLowerInvariant())
            });
        }

        return list;
    }

    public static string LinkSummary(int linkCount, int suspicious)
    {
        if (linkCount <= 0 && suspicious <= 0)
            return "";
        if (suspicious <= 0)
            return TextCatalog.Plural("linksNone", linkCount, ("count", linkCount));
        return TextCatalog.Plural("linksSuspicious", suspicious, ("count", suspicious));
    }

    public static string FileSummary(IReadOnlyList<Finding> findings, int linkCount, int suspicious)
    {
        var links = LinkSummary(linkCount, suspicious);
        if (findings.Count == 0)
        {
            if (suspicious > 0)
                return links;
            var quiet = TextCatalog.Get("nothingFound");
            return links.Length == 0 ? quiet : quiet + " " + links;
        }

        var summary = Summary(findings);
        if (links.Length > 0)
            summary += ", " + links;
        return summary;
    }

    public static string Summary(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
            return TextCatalog.Get("nothingFound");
        var parts = findings
            .GroupBy(CountKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => CountPhrase(group.First(), group.Count()));
        return TextCatalog.Plural("fileFindings", findings.Count, ("count", findings.Count), ("parts", string.Join(", ", parts)));
    }

    public static string CategoryOf(Finding finding)
    {
        if (!string.IsNullOrWhiteSpace(finding.Category) && finding.Category != "custom")
            return finding.Category;
        var id = finding.RuleId;
        if (id.Contains("card", StringComparison.OrdinalIgnoreCase) || id.Contains("iban", StringComparison.OrdinalIgnoreCase))
            return "payment";
        if (id.StartsWith("secret.", StringComparison.OrdinalIgnoreCase))
            return "secrets";
        if (id.StartsWith("pii.", StringComparison.OrdinalIgnoreCase))
            return "personal";
        if (id.StartsWith("user.", StringComparison.OrdinalIgnoreCase) || finding.Category == "custom")
            return "custom";
        return string.IsNullOrWhiteSpace(finding.Category) ? "custom" : finding.Category;
    }

    public static string CountPhrase(Finding sample, int count) => RuleLabels.Count(sample, count);

    private static string CountText(IReadOnlyList<Finding> rows)
    {
        return string.Join(" · ", rows
            .GroupBy(CountKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => CountPhrase(g.First(), g.Count())));
    }

    private static string CountKey(Finding finding) =>
        string.IsNullOrWhiteSpace(finding.DisplayLabel) ? finding.Label : finding.DisplayLabel;
}
