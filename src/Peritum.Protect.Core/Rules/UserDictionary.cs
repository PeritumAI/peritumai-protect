using System.Security.Cryptography;
using System.Text;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Rules;

public sealed class UserTerm
{
    public string Label { get; set; } = "Custom";
    public string Text { get; set; } = "";
    /// <summary>exact or regex.</summary>
    public string Kind { get; set; } = "exact";
    public string Category { get; set; } = "custom";
    public bool Reversible { get; set; } = true;
}

/// <summary>Exact-string terms the user types on the settings page.</summary>
public static class UserDictionary
{
    public const string PackId = "user.terms";

    public static IReadOnlyList<UserTerm> Load(string path)
    {
        if (!File.Exists(path))
            return [];
        RulePack pack;
        try
        {
            pack = RulePackLoader.LoadFile(path);
        }
        catch
        {
            return [];
        }

        return pack.Rules
            .Where(r => r.Id.StartsWith("user.term.", StringComparison.Ordinal) && r.Kind is "exact" or "regex")
            .Select(r => new UserTerm
            {
                Label = r.Label,
                Kind = r.Kind,
                Category = string.IsNullOrWhiteSpace(r.Category) ? "custom" : r.Category,
                Reversible = r.Reversible,
                Text = r.Kind == "regex" ? (r.Pattern ?? "") : (r.Patterns.FirstOrDefault() ?? r.Pattern ?? "")
            })
            .Where(t => t.Text.Length > 0)
            .ToList();
    }

    public static void Save(string path, IReadOnlyList<UserTerm> terms)
    {
        RulePack pack;
        if (File.Exists(path))
        {
            try
            {
                pack = RulePackLoader.LoadFile(path);
            }
            catch
            {
                pack = NewPack();
            }
        }
        else
        {
            pack = NewPack();
        }

        pack.Id = string.IsNullOrWhiteSpace(pack.Id) ? PackId : pack.Id;
        if (string.IsNullOrWhiteSpace(pack.Name))
            pack.Name = "My terms";
        pack.Enabled = true;

        var kept = pack.Rules
            .Where(r => !r.Id.StartsWith("user.term.", StringComparison.Ordinal))
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            var text = term.Text?.Trim() ?? "";
            if (text.Length == 0 || !seen.Add(text))
                continue;
            var label = SanitizeLabel(term.Label);
            var kind = term.Kind == "regex" ? "regex" : "exact";
            kept.Add(new Rule
            {
                Id = "user.term." + ShortHash(kind + "\n" + label + "\n" + text),
                Label = label,
                Kind = kind,
                Pattern = text,
                Patterns = kind == "exact" ? [text] : [],
                IgnoreCase = kind == "regex",
                Placeholder = "CUSTOM:" + label,
                Severity = Severity.High,
                Enabled = true,
                PackId = pack.Id,
                Category = string.IsNullOrWhiteSpace(term.Category) ? "custom" : term.Category,
                Reversible = term.Reversible
            });
        }

        pack.Rules = kept;
        RulePackLoader.Save(path, pack);
    }

    private static RulePack NewPack() => new()
    {
        Id = PackId,
        Name = "My terms",
        Version = "1.0.0",
        Description = "Exact strings added in PeritumAI Protect.",
        Enabled = true
    };

    public static string SanitizeLabel(string? label)
    {
        var raw = (label ?? "").Trim();
        if (raw.Length == 0)
            return "Custom";
        var chars = raw.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray();
        var clean = new string(chars);
        return clean.Length == 0 ? "Custom" : clean;
    }

    private static string ShortHash(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
