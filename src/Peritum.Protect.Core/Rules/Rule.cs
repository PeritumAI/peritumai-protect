using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Rules;

public sealed class Rule
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    /// <summary>Count line singular, with the casing shown in the UI. Empty means use <see cref="Label"/>.</summary>
    public string DisplayLabel { get; init; } = "";
    /// <summary>Count line plural, for example "emails" or "API keys".</summary>
    public string PluralLabel { get; init; } = "";
    public required string Kind { get; init; }
    public string? Pattern { get; init; }
    public IReadOnlyList<string> Patterns { get; init; } = Array.Empty<string>();
    /// <summary>Checksum or format name for kind checksum. Empty for other kinds.</summary>
    public string Algorithm { get; init; } = "";
    /// <summary>ISO region when the rule comes from rules/country. Empty for other packs.</summary>
    public string CountryCode { get; init; } = "";
    public bool IgnoreCase { get; init; } = true;
    public required string Placeholder { get; init; }
    public Severity Severity { get; init; } = Severity.Warn;
    public int? MinLength { get; init; }
    public double? EntropyBits { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Notes { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string PackId { get; init; } = "";
    /// <summary>personal, payment, secrets, custom, or hidden.</summary>
    public string Category { get; init; } = "custom";
    public bool Reversible { get; init; } = true;
}
