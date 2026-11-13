using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Events;

public sealed class RuleStat
{
    public string RuleId { get; init; } = "";
    public string Label { get; init; } = "";
    public int Hits { get; init; }
    public int FalsePositives { get; init; }
    public double FalsePositiveRate => Hits == 0 ? 0 : (double)FalsePositives / Hits;
}

public sealed class ActivitySummary
{
    public int Scans { get; init; }
    public int IncompleteScans { get; init; }
    public int AverageScanMs { get; init; }
    public int MaxScanMs { get; init; }
    public int CopiesMade { get; init; }
    public int CopiesRefused { get; init; }
    public IReadOnlyList<RuleStat> Rules { get; init; } = [];

    public string ToDisplay()
    {
        var lines = new List<string>
        {
            TextCatalog.Format("activityScans", ("scans", Scans), ("incomplete", IncompleteScans), ("average", AverageScanMs), ("max", MaxScanMs)),
            TextCatalog.Format("activityCopies", ("made", CopiesMade), ("refused", CopiesRefused))
        };
        foreach (var rule in Rules)
        {
            var rate = rule.Hits == 0 ? "0" : rule.FalsePositiveRate.ToString("0%");
            var name = string.IsNullOrWhiteSpace(rule.Label) ? rule.RuleId : rule.Label;
            lines.Add(TextCatalog.Format("activityRule", ("name", name), ("hits", rule.Hits), ("falsePositives", rule.FalsePositives), ("rate", rate)));
        }
        return string.Join(Environment.NewLine, lines);
    }

    public string ToCsv()
    {
        var lines = new List<string>
        {
            "metric,value",
            "scans," + Scans,
            "incomplete_scans," + IncompleteScans,
            "average_scan_ms," + AverageScanMs,
            "max_scan_ms," + MaxScanMs,
            "copies_made," + CopiesMade,
            "copies_refused," + CopiesRefused,
            "rule,label,hits,false_positives,false_positive_rate"
        };
        foreach (var rule in Rules)
            lines.Add(string.Join(",", Csv(rule.RuleId), Csv(rule.Label), rule.Hits, rule.FalsePositives, rule.FalsePositiveRate.ToString("0.###")));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

public static class ActivityStats
{
    public static ActivitySummary Summarize(IEnumerable<ProtectEvent> events, IReadOnlyDictionary<string, string>? labels = null)
    {
        var scans = 0;
        var incomplete = 0;
        long durationTotal = 0;
        var durationCount = 0;
        var max = 0;
        var copies = 0;
        var refused = 0;
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        var falsePositives = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var evt in events)
        {
            if (evt.Action is EventActions.Allow or EventActions.Warn)
            {
                scans++;
                if (evt.Incomplete == true)
                    incomplete++;
                if (evt.DurationMs is int ms)
                {
                    durationTotal += ms;
                    durationCount++;
                    if (ms > max)
                        max = ms;
                }
                foreach (var hit in evt.Hits)
                    hits[hit.RuleId] = hits.GetValueOrDefault(hit.RuleId) + hit.Count;
            }
            else if (evt.Action == EventActions.CopyMade)
                copies++;
            else if (evt.Action == EventActions.CopyRefused)
                refused++;
            else if (evt.Action == EventActions.FalsePositive)
            {
                foreach (var hit in evt.Hits)
                    falsePositives[hit.RuleId] = falsePositives.GetValueOrDefault(hit.RuleId) + hit.Count;
            }
        }

        var ids = hits.Keys.Concat(falsePositives.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
        var rules = ids.Select(id => new RuleStat
        {
            RuleId = id,
            Label = LabelFor(labels, id),
            Hits = hits.GetValueOrDefault(id),
            FalsePositives = falsePositives.GetValueOrDefault(id)
        }).ToList();
        return new ActivitySummary
        {
            Scans = scans,
            IncompleteScans = incomplete,
            AverageScanMs = durationCount == 0 ? 0 : (int)(durationTotal / durationCount),
            MaxScanMs = max,
            CopiesMade = copies,
            CopiesRefused = refused,
            Rules = rules
        };
    }

    private static string LabelFor(IReadOnlyDictionary<string, string>? labels, string id)
    {
        if (labels is null)
            return "";
        if (labels.TryGetValue(id, out var label))
            return label;
        foreach (var pair in labels)
        {
            if (pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }
        return "";
    }
}
