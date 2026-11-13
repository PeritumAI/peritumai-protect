namespace Peritum.Protect.Core.Rules;

/// <summary>
/// Country packs are turned on by the Windows region or by a profile. The UI language does not turn them on.
/// </summary>
public static class CountryRegions
{
    public static string PackCode(string? packId)
    {
        const string prefix = "country.";
        if (string.IsNullOrWhiteSpace(packId) ||
            !packId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            packId.Length != prefix.Length + 2)
            return "";
        return packId[prefix.Length..].ToUpperInvariant();
    }

    public static string Canonical(string? region)
    {
        var code = (region ?? "").Trim().ToUpperInvariant();
        return code switch
        {
            "UK" => "GB",
            _ when code.Length == 2 && code.All(char.IsAsciiLetter) => code,
            _ => ""
        };
    }

    public static IReadOnlyList<string> RuleIds(IEnumerable<RulePack> packs, string? region)
    {
        var code = Canonical(region);
        if (code.Length == 0)
            return [];
        var ids = new List<string>();
        foreach (var pack in packs)
        {
            if (!PackCode(pack.Id).Equals(code, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var rule in pack.Rules)
                ids.Add(rule.Id);
        }
        return ids;
    }
}
