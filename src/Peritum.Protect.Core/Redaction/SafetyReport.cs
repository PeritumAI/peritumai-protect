using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Redaction;

public static class SafetyReport
{
    public static string Text(ActivitySummary stats, bool includeFileNames, IEnumerable<string>? fileNames = null)
    {
        var lines = new List<string>
        {
            TextCatalog.Get("reportTitle"),
            TextCatalog.Format("reportScans", ("count", stats.Scans)),
            TextCatalog.Format("reportIncomplete", ("count", stats.IncompleteScans)),
            TextCatalog.Format("reportCopiesMade", ("count", stats.CopiesMade)),
            TextCatalog.Format("reportCopiesRefused", ("count", stats.CopiesRefused)),
            TextCatalog.Get("reportNotCertificate")
        };
        foreach (var rule in stats.Rules)
            lines.Add(TextCatalog.Format("reportRuleLine", ("rule", rule.RuleId), ("hits", rule.Hits), ("falsePositives", rule.FalsePositives)));
        if (includeFileNames && fileNames is not null)
            lines.AddRange(fileNames.Select(name => TextCatalog.Format("reportFile", ("name", name))));
        if (stats.Rules.Any(rule => rule.RuleId.Contains("email", StringComparison.OrdinalIgnoreCase) && rule.Hits > 0))
            lines.Add(TextCatalog.Get("reportEmails"));
        lines.Add(TextCatalog.Get("reportFooter"));
        return string.Join(Environment.NewLine, lines);
    }

    public static byte[] Pdf(ActivitySummary stats) => SimplePdfWriter.FromText(Text(stats, false));

    public static MonthlyReport Build(
        IEnumerable<ProtectEvent> events,
        int year,
        int month,
        bool includeFileNames,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        var scans = 0;
        var warnings = 0;
        var redactions = 0;
        var reversible = 0;
        var stops = 0;
        var hidden = 0;
        var clickFix = 0;
        var incomplete = 0;
        var linkReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        var falsePositives = new Dictionary<string, int>(StringComparer.Ordinal);
        var categories = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var evt in events)
        {
            var local = evt.Ts.ToLocalTime();
            if (local.Year != year || local.Month != month)
                continue;
            if (evt.Action is EventActions.Allow or EventActions.Warn)
            {
                scans++;
                if (evt.Incomplete == true)
                    incomplete++;
                if (evt.Action == EventActions.Warn)
                    warnings++;
            }
            else if (evt.Action == EventActions.Stop)
                stops++;
            else if (evt.Action == EventActions.ClickFix)
                clickFix++;
            else if (evt.Action == EventActions.LinkWarning)
            {
                foreach (var hit in evt.Hits)
                    linkReasons[hit.RuleId] = linkReasons.GetValueOrDefault(hit.RuleId) + Math.Max(1, hit.Count);
            }
            else if (evt.Action == EventActions.FalsePositive)
            {
                foreach (var hit in evt.Hits)
                    falsePositives[hit.RuleId] = falsePositives.GetValueOrDefault(hit.RuleId) + hit.Count;
            }

            var reversibleAction = evt.Action is EventActions.Redact or EventActions.CopyMade
                && string.Equals(evt.Mode, "reversible", StringComparison.OrdinalIgnoreCase);
            if (reversibleAction)
                reversible++;
            else if (evt.Action is EventActions.Redact or EventActions.CopyMade)
                redactions++;

            if (evt.Action is EventActions.Allow or EventActions.Warn)
            {
                foreach (var hit in evt.Hits)
                {
                    hits[hit.RuleId] = hits.GetValueOrDefault(hit.RuleId) + hit.Count;
                    var category = CategoryOf(hit.RuleId);
                    categories[category] = categories.GetValueOrDefault(category) + hit.Count;
                    if (hit.RuleId.StartsWith("hidden.", StringComparison.Ordinal))
                        hidden += hit.Count;
                }
            }

            if (includeFileNames && !string.IsNullOrWhiteSpace(evt.FileName) && !names.Contains(evt.FileName))
                names.Add(evt.FileName);
        }

        var ids = hits.Keys.Concat(falsePositives.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
        var rules = ids.Select(id => new RuleStat
        {
            RuleId = id,
            Label = LabelFor(labels, id),
            Hits = hits.GetValueOrDefault(id),
            FalsePositives = falsePositives.GetValueOrDefault(id)
        }).ToList();
        var top = categories
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CategoryCount(pair.Key, pair.Value))
            .ToList();
        var report = new MonthlyReport
        {
            Year = year,
            Month = month,
            Scans = scans,
            Warnings = warnings,
            Redactions = redactions,
            ReversibleCopies = reversible,
            Stops = stops,
            HiddenFindings = hidden,
            ClickFixWarnings = clickFix,
            IncompleteScans = incomplete,
            LinkReasons = linkReasons
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RuleStat { RuleId = pair.Key, Hits = pair.Value })
                .ToList(),
            Categories = top,
            Rules = rules,
            FileNames = names
        };
        report.Meaning = Meaning(report);
        report.Actions = Suggest(report);
        return report;
    }

    public static string Text(MonthlyReport report, bool includeFileNames)
    {
        var lines = new List<string>
        {
            TextCatalog.Get("reportTitle"),
            TextCatalog.Format("reportPeriod", ("period", report.Year + "-" + report.Month.ToString("00"))),
            TextCatalog.Format("reportScans", ("count", report.Scans)),
            TextCatalog.Format("reportWarnings", ("count", report.Warnings)),
            TextCatalog.Format("reportRedactions", ("count", report.Redactions)),
            TextCatalog.Format("reportReversible", ("count", report.ReversibleCopies)),
            TextCatalog.Format("reportStops", ("count", report.Stops)),
            TextCatalog.Format("reportHidden", ("count", report.HiddenFindings)),
            TextCatalog.Format("reportClickFix", ("count", report.ClickFixWarnings)),
            TextCatalog.Format("reportLinkWarnings", ("summary", LinkSummary(report))),
            TextCatalog.Format("reportIncomplete", ("count", report.IncompleteScans)),
            TextCatalog.Get("reportTop")
        };
        if (report.Categories.Count == 0)
            lines.Add(TextCatalog.Get("reportNone"));
        foreach (var category in report.Categories)
            lines.Add(TextCatalog.Format("reportCategoryLine", ("name", category.Name), ("hits", category.Hits)));
        lines.Add(TextCatalog.Get("reportFalsePositive"));
        if (report.Rules.Count == 0)
            lines.Add(TextCatalog.Get("reportNone"));
        foreach (var rule in report.Rules)
        {
            var name = string.IsNullOrWhiteSpace(rule.Label) ? rule.RuleId : rule.Label;
            lines.Add(TextCatalog.Format("reportRuleDetail", ("name", name), ("rule", rule.RuleId), ("hits", rule.Hits), ("falsePositives", rule.FalsePositives), ("rate", rule.FalsePositiveRate.ToString("0.###"))));
        }
        lines.Add(TextCatalog.Get("reportMeaningTitle"));
        lines.Add(report.Meaning);
        lines.Add(TextCatalog.Get("reportActionsTitle"));
        foreach (var action in report.Actions)
            lines.Add("- " + action);
        lines.Add(TextCatalog.Get("reportCompliance"));
        if (includeFileNames)
        {
            foreach (var name in report.FileNames)
                lines.Add(TextCatalog.Format("reportFile", ("name", name)));
        }
        lines.Add(TextCatalog.Get("reportFooter"));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Csv(MonthlyReport report, bool includeFileNames)
    {
        var lines = new List<string>
        {
            "metric,value",
            "period," + report.Year + "-" + report.Month.ToString("00"),
            "scans," + report.Scans,
            "warnings," + report.Warnings,
            "redactions," + report.Redactions,
            "reversible_copies," + report.ReversibleCopies,
            "stops," + report.Stops,
            "hidden_findings," + report.HiddenFindings,
            "clickfix_warnings," + report.ClickFixWarnings,
            "incomplete_scans," + report.IncompleteScans,
            "link_reason,count"
        };
        foreach (var reason in report.LinkReasons)
            lines.Add(reason.RuleId + "," + reason.Hits);
        lines.Add("category,hits");
        foreach (var category in report.Categories)
            lines.Add(category.Name + "," + category.Hits);
        lines.Add("rule,label,hits,false_positives,false_positive_rate");
        foreach (var rule in report.Rules)
            lines.Add(string.Join(",", Csv(rule.RuleId), Csv(rule.Label), rule.Hits, rule.FalsePositives, rule.FalsePositiveRate.ToString("0.###")));
        if (includeFileNames)
        {
            lines.Add("file_name");
            foreach (var name in report.FileNames)
                lines.Add(Csv(name));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static byte[] Pdf(MonthlyReport report, bool includeFileNames) =>
        SimplePdfWriter.FromText(Text(report, includeFileNames));

    private static string LinkSummary(MonthlyReport report)
    {
        if (report.LinkReasons.Count == 0)
            return "0";
        return string.Join(", ", report.LinkReasons.Select(reason => reason.RuleId + " " + reason.Hits));
    }

    private static string Meaning(MonthlyReport report)
    {
        if (report.Scans == 0 && report.ClickFixWarnings == 0)
            return TextCatalog.Get("reportMeaningEmpty");
        return TextCatalog.Format("reportMeaning", ("scans", report.Scans), ("warnings", report.Warnings));
    }

    private static IReadOnlyList<string> Suggest(MonthlyReport report)
    {
        var actions = new List<string>();
        if (report.Rules.Any(rule => rule.RuleId.Contains("email", StringComparison.OrdinalIgnoreCase) && rule.Hits > 0))
            actions.Add(TextCatalog.Get("reportEmails"));
        if (report.Rules.Any(rule => rule.RuleId.StartsWith("secret.", StringComparison.Ordinal) && rule.Hits > 0))
            actions.Add(TextCatalog.Get("reportSecrets"));
        if (report.HiddenFindings > 0)
            actions.Add(TextCatalog.Get("reportHiddenAction"));
        if (report.ClickFixWarnings > 0)
            actions.Add(TextCatalog.Get("reportClickFixAction"));
        if (report.IncompleteScans > 0)
            actions.Add(TextCatalog.Get("reportIncompleteAction"));
        if (report.Rules.Any(rule => rule.Hits >= 4 && rule.FalsePositiveRate >= 0.5))
            actions.Add(TextCatalog.Get("reportFalsePositiveAction"));
        var fillers = new[]
        {
            TextCatalog.Get("reportActionCheck"),
            TextCatalog.Get("reportActionKeep"),
            TextCatalog.Get("reportActionTerm")
        };
        foreach (var filler in fillers)
        {
            if (actions.Count >= 3)
                break;
            actions.Add(filler);
        }
        return actions.Take(5).ToList();
    }

    private static string CategoryOf(string ruleId)
    {
        if (ruleId.StartsWith("secret.", StringComparison.Ordinal))
            return "Secrets";
        if (ruleId.StartsWith("fin.", StringComparison.Ordinal))
            return "Payment";
        if (ruleId.StartsWith("hidden.", StringComparison.Ordinal))
            return "Hidden";
        if (ruleId.StartsWith("pii.", StringComparison.Ordinal) || ruleId.StartsWith("ref.", StringComparison.Ordinal) || ruleId.StartsWith("court.", StringComparison.Ordinal))
            return "Personal";
        return "Custom";
    }

    private static string LabelFor(IReadOnlyDictionary<string, string>? labels, string id)
    {
        if (labels is not null && labels.TryGetValue(id, out var label))
            return label;
        return "";
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

public sealed class CategoryCount
{
    public CategoryCount(string name, int hits)
    {
        Name = name;
        Hits = hits;
    }

    public string Name { get; }
    public int Hits { get; }
}

public sealed class MonthlyReport
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Scans { get; init; }
    public int Warnings { get; init; }
    public int Redactions { get; init; }
    public int ReversibleCopies { get; init; }
    public int Stops { get; init; }
    public int HiddenFindings { get; init; }
    public int ClickFixWarnings { get; init; }
    public int IncompleteScans { get; init; }
    public IReadOnlyList<RuleStat> LinkReasons { get; init; } = [];
    public IReadOnlyList<CategoryCount> Categories { get; init; } = [];
    public IReadOnlyList<RuleStat> Rules { get; init; } = [];
    public IReadOnlyList<string> FileNames { get; init; } = [];
    public string Meaning { get; set; } = "";
    public IReadOnlyList<string> Actions { get; set; } = [];
}
