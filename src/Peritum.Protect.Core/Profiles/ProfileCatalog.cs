using System.Reflection;
using System.Text.Json;
using Peritum.Protect.Core.Config;

namespace Peritum.Protect.Core.Profiles;

public sealed class SuggestedTerm
{
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "exact";
    public string Text { get; set; } = "";
    public string Example { get; set; } = "";
}

public sealed class ProtectionProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<string> EnabledRuleIds { get; init; } = [];
    public IReadOnlyList<string> DisabledRuleIds { get; init; } = [];
    public string DefaultPasteAction { get; init; } = "ask";
    public string RedactionMode { get; init; } = "reversible";
    public string StopBehavior { get; init; } = "restore-previous";
    public bool WatchLinks { get; init; }
    public IReadOnlyList<SuggestedTerm> SuggestedTerms { get; init; } = [];
}

public readonly record struct ProfileApplyResult(bool Applied, string? Reason)
{
    public static ProfileApplyResult Ok() => new(true, null);

    public static ProfileApplyResult Refused(string reason) => new(false, reason);
}

public static class ProfileCatalog
{
    private static readonly string[] Order =
    [
        "general",
        "lawyer",
        "clinic",
        "accountant",
        "hr",
        "developer",
        "journalist"
    ];

    public static IReadOnlyList<ProtectionProfile> LoadEmbedded()
    {
        var assembly = typeof(ProfileCatalog).Assembly;
        var profiles = new List<ProtectionProfile>(Order.Length);
        foreach (var id in Order)
        {
            var name = "profile." + id + ".json";
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing embedded profile " + name);
            using var reader = new StreamReader(stream);
            profiles.Add(Parse(reader.ReadToEnd()));
        }

        return profiles;
    }

    public static ProtectionProfile? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return LoadEmbedded().First(profile => profile.Id == "general");
        var key = id.Trim();
        return LoadEmbedded().FirstOrDefault(profile => profile.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static ProfileApplyResult TryApply(
        string? profileId,
        AppSettings settings,
        IEnumerable<string> knownRuleIds,
        bool profileLocked)
    {
        if (profileLocked)
            return ProfileApplyResult.Refused("The profile is set by your organization.");

        var profile = Find(profileId);
        if (profile is null)
            return ProfileApplyResult.Refused("That profile is not installed.");
        return TryApply(profile, settings, knownRuleIds, profileLocked: false);
    }

    public static ProfileApplyResult TryApply(
        ProtectionProfile profile,
        AppSettings settings,
        IEnumerable<string> knownRuleIds,
        bool profileLocked)
    {
        if (profileLocked)
            return ProfileApplyResult.Refused("The profile is set by your organization.");

        var known = new HashSet<string>(knownRuleIds, StringComparer.OrdinalIgnoreCase);
        var unknown = profile.EnabledRuleIds
            .Concat(profile.DisabledRuleIds)
            .Where(id => !known.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
            return ProfileApplyResult.Refused(
                "This profile names a rule that is not installed: " + string.Join(", ", unknown) + ".");

        settings.Profile = profile.Id;
        settings.EnabledRuleIds = profile.EnabledRuleIds.ToList();
        settings.DisabledRuleIds = profile.DisabledRuleIds.ToList();
        settings.DefaultPasteAction = profile.DefaultPasteAction;
        settings.RedactionMode = profile.RedactionMode;
        settings.StopBehavior = profile.StopBehavior;
        settings.WatchLinks = profile.WatchLinks;
        return ProfileApplyResult.Ok();
    }

    public static ProtectionProfile Parse(string json)
    {
        ProfileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProfileDto>(json, JsonDefaults.Indented)
                ?? throw new InvalidDataException("Profile file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Profile file is not valid JSON.", ex);
        }

        var id = (dto.Id ?? "").Trim().ToLowerInvariant();
        if (id.Length == 0 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')))
            throw new InvalidDataException("Profile file is missing a valid id.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidDataException("Profile " + id + " is missing a name.");
        if (string.IsNullOrWhiteSpace(dto.Description))
            throw new InvalidDataException("Profile " + id + " is missing a description.");

        var enabled = CleanIds(dto.EnabledRuleIds, id, "enabledRuleIds");
        var disabled = CleanIds(dto.DisabledRuleIds, id, "disabledRuleIds");
        var overlap = enabled.FirstOrDefault(ruleId => disabled.Contains(ruleId, StringComparer.OrdinalIgnoreCase));
        if (overlap is not null)
            throw new InvalidDataException("Profile " + id + " lists " + overlap + " as both on and off.");

        var terms = new List<SuggestedTerm>();
        foreach (var term in dto.SuggestedTerms ?? [])
        {
            var label = (term.Label ?? "").Trim();
            if (label.Length == 0)
                throw new InvalidDataException("Profile " + id + " has a suggested term without a label.");
            var kind = term.Kind?.Trim().ToLowerInvariant();
            if (kind is not ("exact" or "regex"))
                throw new InvalidDataException("Profile " + id + " suggested term \"" + label + "\" must be exact or regex.");
            terms.Add(new SuggestedTerm
            {
                Label = label,
                Kind = kind,
                Text = term.Text?.Trim() ?? "",
                Example = string.IsNullOrWhiteSpace(term.Example) ? term.Text?.Trim() ?? "" : term.Example.Trim()
            });
        }

        return new ProtectionProfile
        {
            Id = id,
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            EnabledRuleIds = enabled,
            DisabledRuleIds = disabled,
            DefaultPasteAction = Action(dto.DefaultPasteAction, id, "defaultPasteAction", "ask", "ignore", "stop", "redact"),
            RedactionMode = Action(dto.RedactionMode, id, "redactionMode", "reversible", "permanent"),
            StopBehavior = Action(dto.StopBehavior, id, "stopBehavior", "restore-previous", "clear-clipboard"),
            WatchLinks = dto.WatchLinks == true,
            SuggestedTerms = terms
        };
    }

    private static List<string> CleanIds(List<string>? ids, string profileId, string field)
    {
        var clean = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ids ?? [])
        {
            var id = (raw ?? "").Trim();
            if (id.Length == 0 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')))
                throw new InvalidDataException("Profile " + profileId + " has an invalid id in " + field + ".");
            if (!seen.Add(id))
                throw new InvalidDataException("Profile " + profileId + " repeats " + id + ".");
            clean.Add(id);
        }

        return clean;
    }

    private static string Action(string? value, string profileId, string field, params string[] allowed)
    {
        var text = (value ?? allowed[0]).Trim().ToLowerInvariant();
        if (!allowed.Contains(text, StringComparer.Ordinal))
            throw new InvalidDataException("Profile " + profileId + " has an invalid " + field + ".");
        return text;
    }

    private sealed class ProfileDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string>? EnabledRuleIds { get; set; }
        public List<string>? DisabledRuleIds { get; set; }
        public string? DefaultPasteAction { get; set; }
        public string? RedactionMode { get; set; }
        public string? StopBehavior { get; set; }
        public bool? WatchLinks { get; set; }
        public List<SuggestedTerm>? SuggestedTerms { get; set; }
    }
}
