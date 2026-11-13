using System.Text.Json;
using System.Text.RegularExpressions;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Rules;

public sealed class RuleExchangeResult
{
    public bool Accepted { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<RulePack> Packs { get; init; } = [];
    public IReadOnlyList<UserTerm> Terms { get; init; } = [];
}

/// <summary>
/// Reads and writes a JSON file of rule packs and My terms.
/// Patterns are stored as text. A regex is only checked to see that it parses. It is not run.
/// </summary>
public static class RuleExchange
{
    public const string Kind = "peritum-protect-export";
    public const int Version = 1;
    public const int MaxBytes = 1024 * 1024;

    private static readonly HashSet<string> DocumentFields = new(StringComparer.Ordinal)
    {
        "kind", "version", "packs", "terms"
    };

    private static readonly HashSet<string> PackFields = new(StringComparer.Ordinal)
    {
        "id", "name", "version", "description", "author", "enabled", "rules"
    };

    private static readonly HashSet<string> RuleFields = new(StringComparer.Ordinal)
    {
        "id", "label", "displayLabel", "plural", "kind", "pattern", "patterns", "algorithm",
        "ignoreCase", "placeholder", "severity", "minLength", "entropyBits",
        "enabled", "notes", "keywords", "category", "reversible"
    };

    private static readonly HashSet<string> TermFields = new(StringComparer.Ordinal)
    {
        "label", "kind", "text"
    };

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "regex", "exact", "prefix", "luhn", "iban", "entropy", "keyword", "pem-block", "phone", "jmbg", "checksum"
    };

    private static readonly HashSet<string> Severities = new(StringComparer.Ordinal)
    {
        "info", "warn", "high", "block"
    };

    public static string Export(IEnumerable<RulePack> packs, IEnumerable<UserTerm> terms)
    {
        var document = new ExportDocument
        {
            Kind = Kind,
            Version = Version,
            Packs = packs.Select(ToPack).ToList(),
            Terms = terms
                .Where(term => !string.IsNullOrWhiteSpace(term.Text))
                .Select(term => new TermDocument
                {
                    Label = string.IsNullOrWhiteSpace(term.Label) ? "Custom" : term.Label.Trim(),
                    Kind = term.Kind == "regex" ? "regex" : "exact",
                    Text = term.Text.Trim()
                })
                .ToList()
        };
        return JsonSerializer.Serialize(document, JsonDefaults.Indented);
    }

    public static RuleExchangeResult Read(
        string? json,
        IEnumerable<string>? existingPackIds = null,
        IEnumerable<string>? existingRuleIds = null,
        IEnumerable<string>? existingTermTexts = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Reject("The file is empty.");
        if (json.Length > MaxBytes)
            return Reject("That file is too large to import.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Reject("This file is not valid JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Reject("The file must be a JSON object.");

            var errors = new List<string>();
            Unexpected(document.RootElement, DocumentFields, "the file", errors);
            if (!StringField(document.RootElement, "kind", out var kind) || kind != Kind)
                errors.Add("The file kind must be \"" + Kind + "\".");
            if (!document.RootElement.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionNumber) ||
                versionNumber != Version)
                errors.Add("The file version must be 1.");

            var packs = new List<RulePack>();
            var terms = new List<UserTerm>();
            var seenPacks = new HashSet<string>(existingPackIds ?? [], StringComparer.OrdinalIgnoreCase);
            var seenRules = new HashSet<string>(existingRuleIds ?? [], StringComparer.OrdinalIgnoreCase);
            var seenTerms = new HashSet<string>(existingTermTexts ?? [], StringComparer.Ordinal);

            if (document.RootElement.TryGetProperty("packs", out var packElement))
            {
                if (packElement.ValueKind != JsonValueKind.Array)
                    errors.Add("Packs must be a list.");
                else
                {
                    foreach (var item in packElement.EnumerateArray())
                        ReadPack(item, packs, seenPacks, seenRules, errors);
                }
            }

            if (document.RootElement.TryGetProperty("terms", out var termElement))
            {
                if (termElement.ValueKind != JsonValueKind.Array)
                    errors.Add("Terms must be a list.");
                else
                {
                    foreach (var item in termElement.EnumerateArray())
                        ReadTerm(item, terms, seenTerms, errors);
                }
            }

            if (packs.Count == 0 && terms.Count == 0 && errors.Count == 0)
                errors.Add("The file has no rules and no terms.");
            if (errors.Count > 0)
                return Reject(string.Join(" ", errors.Distinct()));

            return new RuleExchangeResult
            {
                Accepted = true,
                Message = Describe(packs.Count, terms.Count),
                Packs = packs,
                Terms = terms
            };
        }
    }

    public static void WritePacks(string directory, IReadOnlyList<RulePack> packs)
    {
        Directory.CreateDirectory(directory);
        foreach (var pack in packs)
        {
            if (!IsId(pack.Id) || pack.Id.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("Rule pack id cannot be saved as a file name.");
            var name = pack.Id + ".json";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Rule pack id cannot be saved as a file name.");
            RulePackLoader.Save(Path.Combine(directory, name), pack);
        }
    }

    private static void ReadPack(
        JsonElement item,
        List<RulePack> packs,
        HashSet<string> seenPacks,
        HashSet<string> seenRules,
        List<string> errors)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            errors.Add("A rule pack must be a JSON object.");
            return;
        }

        Unexpected(item, PackFields, "a rule pack", errors);
        if (!StringField(item, "id", out var id) || !IsId(id))
        {
            errors.Add("A rule pack is missing a valid id.");
            return;
        }

        if (!seenPacks.Add(id))
        {
            errors.Add("Duplicate rule pack id " + id + ". Nothing was merged.");
            return;
        }

        if (id.Equals(UserDictionary.PackId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("My terms belong in the terms list, not in a rule pack named " + id + ".");
            return;
        }

        if (!StringField(item, "name", out var name))
        {
            errors.Add("Rule pack " + id + " is missing a name.");
            return;
        }

        if (!item.TryGetProperty("version", out var versionElement) || versionElement.ValueKind != JsonValueKind.String)
        {
            errors.Add("Rule pack " + id + " is missing a version.");
            return;
        }

        if (!item.TryGetProperty("rules", out var rulesElement) || rulesElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Rule pack " + id + " is missing a rules list.");
            return;
        }

        var pack = new RulePack
        {
            Id = id,
            Name = name,
            Version = versionElement.GetString() ?? "1.0.0",
            Description = OptionalString(item, "description"),
            Author = OptionalString(item, "author"),
            Enabled = !item.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False
        };

        foreach (var ruleElement in rulesElement.EnumerateArray())
        {
            var rule = ReadRule(ruleElement, id, seenRules, errors);
            if (rule is not null)
                pack.Rules.Add(rule);
        }

        packs.Add(pack);
    }

    private static Rule? ReadRule(JsonElement item, string packId, HashSet<string> seenRules, List<string> errors)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            errors.Add("A rule in " + packId + " must be a JSON object.");
            return null;
        }

        Unexpected(item, RuleFields, "rule in " + packId, errors);
        if (!StringField(item, "id", out var id) || !IsId(id))
        {
            errors.Add("A rule in " + packId + " is missing a valid id.");
            return null;
        }

        if (!seenRules.Add(id))
        {
            errors.Add("Duplicate rule id " + id + ". Nothing was merged.");
            return null;
        }

        if (!StringField(item, "label", out var label))
        {
            errors.Add("Rule " + id + " is missing a label.");
            return null;
        }

        if (!StringField(item, "kind", out var kind) || !Kinds.Contains(kind))
        {
            errors.Add("Rule " + id + " has a kind that is not allowed.");
            return null;
        }

        var pattern = OptionalString(item, "pattern");
        var patterns = new List<string>();
        if (item.TryGetProperty("patterns", out var patternList))
        {
            if (patternList.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Rule " + id + " has a patterns list that is not valid.");
                return null;
            }

            foreach (var entry in patternList.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    errors.Add("Rule " + id + " has a pattern that is not text.");
                    return null;
                }

                var text = entry.GetString() ?? "";
                if (text.Length == 0 || text.Length > 2000)
                {
                    errors.Add("Rule " + id + " has a pattern that is empty or too long.");
                    return null;
                }

                patterns.Add(text);
            }
        }

        if (pattern is { Length: > 2000 })
        {
            errors.Add("Rule " + id + " has a pattern that is too long.");
            return null;
        }

        if (kind is "regex" or "exact" or "prefix" or "keyword" &&
            string.IsNullOrEmpty(pattern) && patterns.Count == 0)
        {
            errors.Add("Rule " + id + " is missing a pattern.");
            return null;
        }

        if (kind == "regex")
        {
            var sources = new List<string>();
            if (!string.IsNullOrEmpty(pattern))
                sources.Add(pattern);
            sources.AddRange(patterns);
            if (sources.Any(source => !RegexParses(source)))
            {
                errors.Add("The regex for " + id + " is not valid.");
                return null;
            }
        }

        var severity = "warn";
        if (item.TryGetProperty("severity", out var severityElement))
        {
            if (severityElement.ValueKind != JsonValueKind.String ||
                !Severities.Contains(severityElement.GetString() ?? ""))
            {
                errors.Add("Rule " + id + " has a severity that is not allowed.");
                return null;
            }

            severity = severityElement.GetString() ?? "warn";
        }

        var category = OptionalString(item, "category") ?? "custom";
        if (category is not ("personal" or "payment" or "secrets" or "custom" or "hidden"))
        {
            errors.Add("Rule " + id + " has a category that is not allowed.");
            return null;
        }

        var keywords = new List<string>();
        if (item.TryGetProperty("keywords", out var keywordList))
        {
            if (keywordList.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Rule " + id + " has a keywords list that is not valid.");
                return null;
            }

            foreach (var entry in keywordList.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                {
                    errors.Add("Rule " + id + " has a keyword that is not text.");
                    return null;
                }

                var word = (entry.GetString() ?? "").Trim();
                if (word.Length == 0 || word.Length > 80)
                {
                    errors.Add("Rule " + id + " has a keyword that is empty or too long.");
                    return null;
                }

                keywords.Add(word);
            }
        }

        return new Rule
        {
            Id = id,
            Label = label,
            DisplayLabel = OptionalString(item, "displayLabel") ?? "",
            PluralLabel = OptionalString(item, "plural") ?? "",
            Kind = kind,
            Pattern = pattern,
            Patterns = patterns,
            Algorithm = OptionalString(item, "algorithm")?.ToLowerInvariant() ?? "",
            CountryCode = CountryRegions.PackCode(packId),
            IgnoreCase = !item.TryGetProperty("ignoreCase", out var ignore) || ignore.ValueKind != JsonValueKind.False,
            Placeholder = OptionalString(item, "placeholder") ?? "CUSTOM",
            Severity = SeverityParser.Parse(severity),
            MinLength = OptionalInt(item, "minLength", id, errors),
            EntropyBits = OptionalNumber(item, "entropyBits", id, errors),
            Enabled = !item.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            Notes = OptionalString(item, "notes"),
            Keywords = keywords,
            PackId = packId,
            Category = category,
            Reversible = !item.TryGetProperty("reversible", out var reversible) || reversible.ValueKind != JsonValueKind.False
        };
    }

    private static void ReadTerm(JsonElement item, List<UserTerm> terms, HashSet<string> seenTerms, List<string> errors)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            errors.Add("A term must be a JSON object.");
            return;
        }

        Unexpected(item, TermFields, "a term", errors);
        if (!StringField(item, "text", out var text) || text.Length > 500)
        {
            errors.Add("A term is missing text, or the text is too long.");
            return;
        }

        if (!seenTerms.Add(text))
        {
            errors.Add("Duplicate term \"" + text + "\". Nothing was merged.");
            return;
        }

        var kind = "exact";
        if (item.TryGetProperty("kind", out var kindElement))
        {
            if (kindElement.ValueKind != JsonValueKind.String || kindElement.GetString() is not ("exact" or "regex"))
            {
                errors.Add("A term must be exact or regex.");
                return;
            }

            kind = kindElement.GetString() ?? "exact";
        }

        if (kind == "regex" && !RegexParses(text))
        {
            errors.Add("The regex for a term is not valid.");
            return;
        }

        var label = OptionalString(item, "label");
        terms.Add(new UserTerm
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Custom" : label,
            Kind = kind,
            Text = text
        });
    }

    private static bool RegexParses(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void Unexpected(JsonElement item, HashSet<string> allowed, string where, List<string> errors)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                errors.Add("Unexpected field \"" + property.Name + "\" on " + where + ".");
        }
    }

    private static bool StringField(JsonElement item, string name, out string value)
    {
        value = "";
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = (property.GetString() ?? "").Trim();
        return value.Length > 0;
    }

    private static string? OptionalString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return null;
        var value = property.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? OptionalInt(JsonElement item, string name, string ruleId, List<string> errors)
    {
        if (!item.TryGetProperty(name, out var property))
            return null;
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value) || value < 1)
        {
            errors.Add("Rule " + ruleId + " has a minLength that is not valid.");
            return null;
        }

        return value;
    }

    private static double? OptionalNumber(JsonElement item, string name, string ruleId, List<string> errors)
    {
        if (!item.TryGetProperty(name, out var property))
            return null;
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var value))
        {
            errors.Add("Rule " + ruleId + " has an entropyBits value that is not valid.");
            return null;
        }

        return value;
    }

    private static bool IsId(string value) =>
        value.Length is > 0 and <= 80 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-');

    private static string Describe(int packs, int terms)
    {
        var parts = new List<string>();
        if (packs > 0)
            parts.Add(packs == 1 ? "1 rule pack" : packs + " rule packs");
        if (terms > 0)
            parts.Add(terms == 1 ? "1 term" : terms + " terms");
        var saved = terms > 0 ? " Save to keep the terms on this PC." : "";
        return "Imported " + string.Join(" and ", parts) + "." + saved;
    }

    private static RuleExchangeResult Reject(string message) => new()
    {
        Accepted = false,
        Message = message
    };

    private static PackDocument ToPack(RulePack pack) => new()
    {
        Id = pack.Id,
        Name = pack.Name,
        Version = string.IsNullOrWhiteSpace(pack.Version) ? "1.0.0" : pack.Version,
        Description = pack.Description,
        Author = pack.Author,
        Enabled = pack.Enabled,
        Rules = pack.Rules.Select(rule => new RuleDocument
        {
            Id = rule.Id,
            Label = rule.Label,
            DisplayLabel = string.IsNullOrWhiteSpace(rule.DisplayLabel) ? null : rule.DisplayLabel,
            Plural = string.IsNullOrWhiteSpace(rule.PluralLabel) ? null : rule.PluralLabel,
            Kind = rule.Kind,
            Pattern = rule.Pattern,
            Patterns = rule.Patterns.Count > 0 ? rule.Patterns.ToList() : null,
            Algorithm = string.IsNullOrWhiteSpace(rule.Algorithm) ? null : rule.Algorithm,
            IgnoreCase = rule.IgnoreCase,
            Placeholder = rule.Placeholder,
            Severity = rule.Severity switch
            {
                Severity.Info => "info",
                Severity.High => "high",
                Severity.Block => "block",
                _ => "warn"
            },
            MinLength = rule.MinLength,
            EntropyBits = rule.EntropyBits,
            Enabled = rule.Enabled,
            Notes = rule.Notes,
            Keywords = rule.Keywords.Count > 0 ? rule.Keywords.ToList() : null,
            Category = string.IsNullOrWhiteSpace(rule.Category) ? null : rule.Category,
            Reversible = rule.Reversible
        }).ToList()
    };

    private sealed class ExportDocument
    {
        public string Kind { get; set; } = RuleExchange.Kind;
        public int Version { get; set; } = RuleExchange.Version;
        public List<PackDocument> Packs { get; set; } = [];
        public List<TermDocument> Terms { get; set; } = [];
    }

    private sealed class PackDocument
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string? Description { get; set; }
        public string? Author { get; set; }
        public bool Enabled { get; set; } = true;
        public List<RuleDocument> Rules { get; set; } = [];
    }

    private sealed class RuleDocument
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string? DisplayLabel { get; set; }
        public string? Plural { get; set; }
        public string Kind { get; set; } = "exact";
        public string? Pattern { get; set; }
        public List<string>? Patterns { get; set; }
        public string? Algorithm { get; set; }
        public bool IgnoreCase { get; set; } = true;
        public string? Placeholder { get; set; }
        public string Severity { get; set; } = "warn";
        public int? MinLength { get; set; }
        public double? EntropyBits { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Notes { get; set; }
        public List<string>? Keywords { get; set; }
        public string? Category { get; set; }
        public bool Reversible { get; set; } = true;
    }

    private sealed class TermDocument
    {
        public string Label { get; set; } = "Custom";
        public string Kind { get; set; } = "exact";
        public string Text { get; set; } = "";
    }
}
