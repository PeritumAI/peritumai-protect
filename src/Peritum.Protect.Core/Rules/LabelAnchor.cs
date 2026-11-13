using System.Text;
using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Rules;

public static class LabelAnchor
{
    public static string Compile(IReadOnlyList<string> labels)
    {
        var ordered = labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(label => label.Length)
            .Select(Escape)
            .ToList();
        if (ordered.Count == 0)
            throw new InvalidDataException("A label rule needs at least one label.");
        var alternation = string.Join("|", ordered);
        return "(?i)(?<=(?:^|[^\\p{L}\\p{N}])(?:" + alternation + ")(?:[ ]?(?:[:#\\-]|[Nn]o\\.))?[ ]?)"
            + "(?=[\\p{L}\\p{N}\\-./_:]{0,39}\\d)"
            + "[\\p{L}\\p{N}\\-./_:]{3,40}"
            + "(?![\\p{L}\\p{N}\\-./_:])";
    }

    private static string Escape(string label)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < label.Length)
        {
            if (char.IsWhiteSpace(label[i]))
            {
                sb.Append("\\s+");
                while (i < label.Length && char.IsWhiteSpace(label[i]))
                    i++;
                continue;
            }

            var c = label[i];
            if (c is '.' or '#' or '-' or ':' or '/' or '(' or ')')
                sb.Append('\\');
            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    public static Rule RuleFor(string id, string display, string plural, string placeholder, IReadOnlyList<string> labels) => new()
    {
        Id = id,
        Label = display,
        DisplayLabel = display,
        PluralLabel = plural,
        Kind = "regex",
        Pattern = Compile(labels),
        IgnoreCase = true,
        Placeholder = placeholder,
        Severity = Scanning.Severity.High,
        Enabled = false,
        Notes = "TODO: Why is this risky?",
        PackId = "builtin.labels",
        Category = "personal",
        Reversible = true
    };
}
