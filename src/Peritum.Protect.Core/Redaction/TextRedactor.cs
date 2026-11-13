using System.Text;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Redaction;

public sealed class TextRedactor : IRedactor
{
    public static string Apply(string text, IReadOnlyList<Finding> findings)
    {
        if (string.IsNullOrEmpty(text) || findings.Count == 0)
            return text ?? "";

        var ordered = findings
            .Where(f => f.Start >= 0 && f.Length > 0 && f.Start + f.Length <= text.Length)
            .OrderBy(f => f.Start)
            .ThenByDescending(f => f.Length)
            .ToList();

        var kept = new List<Finding>(ordered.Count);
        var cursor = 0;
        foreach (var finding in ordered)
        {
            if (finding.Start < cursor)
                continue;
            kept.Add(finding);
            cursor = finding.Start + finding.Length;
        }

        var sb = new StringBuilder(text);
        for (var i = kept.Count - 1; i >= 0; i--)
        {
            var finding = kept[i];
            sb.Remove(finding.Start, finding.Length);
            sb.Insert(finding.Start, PlaceholderStyle.Wrap(finding.Placeholder));
        }
        return sb.ToString();
    }

    string IRedactor.Apply(string text, IReadOnlyList<Finding> findings) => Apply(text, findings);
}
