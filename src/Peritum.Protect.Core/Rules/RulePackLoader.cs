using System.Text.Json;
using System.Text.Json.Serialization;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Rules;

public static class RulePackLoader
{
    public static RulePack LoadFile(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static RulePack Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<RulePackDto>(json, JsonDefaults.Indented)
            ?? throw new InvalidDataException("Rule pack is empty.");
        if (string.IsNullOrWhiteSpace(dto.Id))
            throw new InvalidDataException("Rule pack is missing id.");

        var pack = new RulePack
        {
            Id = dto.Id.Trim(),
            Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.Id : dto.Name.Trim(),
            Version = string.IsNullOrWhiteSpace(dto.Version) ? "1.0.0" : dto.Version.Trim(),
            Description = dto.Description,
            Author = dto.Author,
            Enabled = dto.Enabled != false
        };

        foreach (var rule in dto.Rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Kind))
                continue;
            pack.Rules.Add(Map(rule, pack.Id));
        }

        return pack;
    }

    public static void Save(string path, RulePack pack)
    {
        var dto = new RulePackDto
        {
            Id = pack.Id,
            Name = pack.Name,
            Version = pack.Version,
            Description = pack.Description,
            Author = pack.Author,
            Enabled = pack.Enabled,
            Rules = pack.Rules.Select(ToDto).ToList()
        };
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        WriteAtomic(path, json);
    }

    public static IReadOnlyList<Rule> SelectActive(IEnumerable<RulePack> packs, IReadOnlyCollection<string>? enabledRuleIds, IReadOnlyCollection<string>? disabledRuleIds)
    {
        var forceOn = new HashSet<string>(enabledRuleIds ?? [], StringComparer.OrdinalIgnoreCase);
        var forceOff = new HashSet<string>(disabledRuleIds ?? [], StringComparer.OrdinalIgnoreCase);
        var list = new List<Rule>();
        foreach (var pack in packs)
        {
            foreach (var rule in pack.Rules)
            {
                if (forceOff.Contains(rule.Id))
                    continue;
                var on = forceOn.Contains(rule.Id) || (pack.Enabled && rule.Enabled);
                if (!on)
                    continue;
                list.Add(rule);
            }
        }
        return list;
    }

    private static Rule Map(RuleDto dto, string packId)
    {
        var patterns = dto.Patterns ?? [];
        if (patterns.Count == 0 && !string.IsNullOrEmpty(dto.Pattern) && dto.Kind is "exact" or "prefix" or "keyword" or "pem-block")
            patterns = [dto.Pattern];

        var placeholder = dto.Placeholder;
        if (string.IsNullOrWhiteSpace(placeholder))
            placeholder = new string((dto.Label ?? "REDACTED").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(placeholder))
            placeholder = "REDACTED";

        return new Rule
        {
            Id = dto.Id!.Trim(),
            Label = string.IsNullOrWhiteSpace(dto.Label) ? dto.Id!.Trim() : dto.Label.Trim(),
            DisplayLabel = string.IsNullOrWhiteSpace(dto.DisplayLabel) ? "" : dto.DisplayLabel.Trim(),
            PluralLabel = string.IsNullOrWhiteSpace(dto.Plural) ? "" : dto.Plural.Trim(),
            Kind = dto.Kind!.Trim().ToLowerInvariant(),
            Pattern = dto.Pattern,
            Patterns = patterns,
            Algorithm = string.IsNullOrWhiteSpace(dto.Algorithm) ? "" : dto.Algorithm.Trim().ToLowerInvariant(),
            CountryCode = CountryRegions.PackCode(packId),
            IgnoreCase = dto.IgnoreCase != false,
            Placeholder = placeholder.Trim().Trim('[', ']'),
            Severity = SeverityParser.Parse(dto.Severity),
            MinLength = dto.MinLength,
            EntropyBits = dto.EntropyBits,
            Enabled = dto.Enabled != false,
            Notes = dto.Notes,
            Keywords = dto.Keywords ?? [],
            PackId = packId,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? CategoryFor(packId) : dto.Category.Trim().ToLowerInvariant(),
            Reversible = dto.Reversible ?? !packId.Contains("secret", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static RuleDto ToDto(Rule rule) => new()
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
        Category = rule.Category,
        Reversible = rule.Reversible
    };

    public static string CategoryFor(string packId)
    {
        if (packId.Contains("secret", StringComparison.OrdinalIgnoreCase))
            return "secrets";
        if (packId.Contains("finance", StringComparison.OrdinalIgnoreCase))
            return "payment";
        if (packId.Contains("pii", StringComparison.OrdinalIgnoreCase) || packId.Contains("country", StringComparison.OrdinalIgnoreCase))
            return "personal";
        if (packId.Contains("injection", StringComparison.OrdinalIgnoreCase) || packId.Contains("hidden", StringComparison.OrdinalIgnoreCase))
            return "hidden";
        return "custom";
    }

    private static void WriteAtomic(string path, string json)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    private sealed class RulePackDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public bool? Enabled { get; set; }
        public List<RuleDto>? Rules { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? DisplayLabel { get; set; }
        public string? Plural { get; set; }
        public string? Kind { get; set; }
        public string? Pattern { get; set; }
        public List<string>? Patterns { get; set; }
        public string? Algorithm { get; set; }
        public bool? IgnoreCase { get; set; }
        public string? Placeholder { get; set; }
        public string? Severity { get; set; }
        public int? MinLength { get; set; }
        public double? EntropyBits { get; set; }
        public bool? Enabled { get; set; }
        public string? Notes { get; set; }
        public List<string>? Keywords { get; set; }
        public string? Category { get; set; }
        public bool? Reversible { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
