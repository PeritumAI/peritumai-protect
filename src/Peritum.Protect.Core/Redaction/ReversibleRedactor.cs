using System.Text;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Redaction;

public sealed class RedactionMap
{
    public string Id { get; init; } = "";
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);
}

public static class ReversibleRedactor
{
    public const string RemovedSecret = "[[SECRET_REMOVED]]";

    public static (string Text, RedactionMap Map) Apply(string text, IReadOnlyList<Finding> findings, bool reversible)
    {
        var session = new ReversibleSession();
        return (session.Apply(text, findings, reversible), session.Map);
    }

    public static string Normalize(string value, Finding finding)
    {
        if (finding.RuleId.Contains("email", StringComparison.OrdinalIgnoreCase))
            return value.Trim().ToLowerInvariant();
        if (finding.RuleId.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            finding.RuleId.Contains("card", StringComparison.OrdinalIgnoreCase) ||
            finding.RuleId.Contains("iban", StringComparison.OrdinalIgnoreCase))
            return new string(value.Where(char.IsDigit).ToArray());
        return value;
    }

    public static string TokenType(Finding finding)
    {
        if (finding.RuleId.Contains("email", StringComparison.OrdinalIgnoreCase))
            return "EMAIL";
        if (finding.RuleId.Contains("phone", StringComparison.OrdinalIgnoreCase))
            return "PHONE";
        if (finding.RuleId.Contains("iban", StringComparison.OrdinalIgnoreCase))
            return "IBAN";
        if (finding.RuleId.Contains("card", StringComparison.OrdinalIgnoreCase))
            return "CARD";
        if (finding.Category == "custom")
            return "CUSTOM_" + new string(finding.Placeholder.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).ToUpperInvariant();
        return "ID";
    }

    public static string NewId()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        var chars = new char[9];
        for (var i = 0; i < 8; i++)
            chars[i < 4 ? i : i + 1] = alphabet[bytes[i] % alphabet.Length];
        chars[4] = '-';
        return new string(chars);
    }
}

public sealed class ReversibleSession
{
    private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _assigned = new(StringComparer.Ordinal);

    public ReversibleSession() => Map = new RedactionMap { Id = ReversibleRedactor.NewId() };

    public RedactionMap Map { get; }

    public string MapLine => "Peritum map: " + Map.Id;

    public string Placeholder(string value, Finding finding, bool reversible)
    {
        if (!finding.RuleAllowsReverse(reversible))
            return ReversibleRedactor.RemovedSecret;
        var key = ReversibleRedactor.Normalize(value, finding);
        if (_assigned.TryGetValue(key, out var existing))
            return existing;
        var type = ReversibleRedactor.TokenType(finding);
        var n = _numbers.GetValueOrDefault(type) + 1;
        _numbers[type] = n;
        var token = "[[" + type + "_" + n + "]]";
        _assigned[key] = token;
        Map.Tokens[token] = value;
        return token;
    }

    public string Apply(string text, IReadOnlyList<Finding> findings) => Apply(text, findings, reversible: true);

    public string Apply(string text, IReadOnlyList<Finding> findings, bool reversible)
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

        // Number by first appearance. Replacement still walks backward so earlier offsets stay valid.
        foreach (var finding in kept)
            Placeholder(text.Substring(finding.Start, finding.Length), finding, reversible);

        var sb = new StringBuilder(text);
        for (var i = kept.Count - 1; i >= 0; i--)
        {
            var finding = kept[i];
            var value = text.Substring(finding.Start, finding.Length);
            var token = Placeholder(value, finding, reversible);
            sb.Remove(finding.Start, finding.Length);
            sb.Insert(finding.Start, token);
        }

        return sb.ToString();
    }

    public static string AppendMapLine(string text, string mapLine)
    {
        if (text.Contains(mapLine, StringComparison.Ordinal))
            return text;
        if (text.Length == 0)
            return mapLine;
        return text.EndsWith('\n') ? text + mapLine : text + "\n" + mapLine;
    }
}

public static class FindingReverse
{
    public static bool RuleAllowsReverse(this Finding finding, bool modeOn) =>
        modeOn && finding.Category != "secrets" && !finding.RuleId.StartsWith("secret.", StringComparison.OrdinalIgnoreCase);
}
