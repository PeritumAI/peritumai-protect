using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Redaction;

public sealed class RestoreReport
{
    public string Text { get; set; } = "";
    public List<string> Restored { get; } = [];
    public List<string> Missing { get; } = [];
    public List<string> Invented { get; } = [];
}

public static class TokenRestorer
{
    private static readonly Regex Token = new(
        @"\[{1,2}\s*([A-Za-z][A-Za-z0-9]*(?:_[A-Za-z][A-Za-z0-9]*)*)_([0-9]+)\s*\]{1,2}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static RestoreReport Restore(string text, IReadOnlyDictionary<string, string> tokens)
    {
        var report = new RestoreReport();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = (text ?? "").Replace("\\[", "[", StringComparison.Ordinal).Replace("\\]", "]", StringComparison.Ordinal).Replace("\\_", "_", StringComparison.Ordinal);
        var output = Token.Replace(cleaned, match =>
        {
            var type = match.Groups[1].Value.ToUpperInvariant();
            var number = match.Groups[2].Value;
            var canonical = "[[" + type + "_" + number + "]]";
            seen.Add(canonical);
            if (type == "SECRET" && number == "REMOVED")
                return match.Value;
            if (tokens.TryGetValue(canonical, out var value))
            {
                report.Restored.Add(canonical);
                return value;
            }
            var folded = tokens.Keys.FirstOrDefault(key => key.Equals(canonical, StringComparison.OrdinalIgnoreCase));
            if (folded is not null)
            {
                report.Restored.Add(folded);
                return tokens[folded];
            }
            report.Invented.Add(canonical);
            return match.Value;
        });
        foreach (var key in tokens.Keys)
        {
            if (!seen.Contains(key))
                report.Missing.Add(key);
        }
        report.Text = output;
        return report;
    }
}
