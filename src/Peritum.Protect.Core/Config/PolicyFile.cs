using System.Text.Json;
using System.Text.Json.Serialization;
using Peritum.Protect.Core.Links;

namespace Peritum.Protect.Core.Config;

public static class PolicyPaths
{
    public const string RegistrySubKey = @"Software\Policies\Peritum\Protect";
    public const int MaxBytes = 64 * 1024;

    public static string DefaultFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Peritum",
            "Protect",
            "policy.json");
}

public static class PolicyKeys
{
    public const string Profile = "profile";
    public const string WatchClipboard = "watchClipboard";
    public const string WatchCommands = "watchCommands";
    public const string WatchLinks = "watchLinks";
    public const string WatchCryptoSwap = "watchCryptoSwap";
    public const string OnlyWhenTargetFocused = "onlyWhenTargetFocused";
    public const string DefaultPasteAction = "defaultPasteAction";
    public const string StopBehavior = "stopBehavior";
    public const string RedactionMode = "redactionMode";
    public const string MinHitsForPopup = "minHitsForPopup";
    public const string LockIgnore = "lockIgnore";
    public const string RememberFileLocations = "rememberFileLocations";
    public const string DisabledRuleIds = "disabledRuleIds";
    public const string EnabledRuleIds = "enabledRuleIds";
    public const string NumberFormats = "numberFormats";
    public const string OrganizationDomains = "organizationDomains";
    public const string LockSites = "lockSites";

    public static IReadOnlyList<string> All { get; } =
    [
        Profile,
        WatchClipboard,
        WatchCommands,
        WatchLinks,
        WatchCryptoSwap,
        OnlyWhenTargetFocused,
        DefaultPasteAction,
        StopBehavior,
        RedactionMode,
        MinHitsForPopup,
        LockIgnore,
        RememberFileLocations,
        DisabledRuleIds,
        EnabledRuleIds,
        NumberFormats,
        OrganizationDomains,
        LockSites
    ];
}

public static class PolicyWarnings
{
    public const string File = "Organization policy was ignored because the policy file is not valid.";
    public const string Registry = "Organization policy was ignored because the registry policy is not valid.";
    public const string Both = "Organization policy was ignored because the policy file and the registry policy are not valid.";
}

public sealed class PolicyBinding
{
    public PolicyBinding(string? filePath, IPolicyRegistry registry)
    {
        FilePath = filePath;
        Registry = registry;
    }

    public string? FilePath { get; }
    public IPolicyRegistry Registry { get; }

    public static PolicyBinding Machine { get; } = new(PolicyPaths.DefaultFile, new WindowsPolicyRegistry());
}

public sealed class PolicyLoadResult
{
    public PolicyLoadResult(PolicyFile policy, string? warning)
    {
        Policy = policy;
        Warning = warning;
    }

    public PolicyFile Policy { get; }
    public string? Warning { get; }
}

public readonly record struct RuleChoice(string Id, bool IsOn, bool DefaultOn);

public sealed class PolicyNumberFormat
{
    public required string Name { get; init; }
    public required string Pattern { get; init; }
    public string Category { get; init; } = "custom";
    public bool Reversible { get; init; } = true;

    public Peritum.Protect.Core.Rules.Rule ToRule() => new()
    {
        Id = "policy.format." + Name,
        Label = Name,
        DisplayLabel = Name,
        PluralLabel = Name,
        Kind = "regex",
        Pattern = Pattern,
        Placeholder = "REF",
        Severity = Peritum.Protect.Core.Scanning.Severity.High,
        Enabled = true,
        Category = Category,
        Reversible = Reversible,
        PackId = "policy.formats",
        Notes = "Set by your organization."
    };
}

public sealed class PolicyFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    private readonly HashSet<string> _lockedKeys;
    private readonly HashSet<string> _lockedRules;
    private readonly bool _hasLockIgnore;

    private PolicyFile(Builder builder)
    {
        Profile = builder.Profile;
        WatchClipboard = builder.WatchClipboard;
        WatchCommands = builder.WatchCommands;
        WatchLinks = builder.WatchLinks;
        WatchCryptoSwap = builder.WatchCryptoSwap;
        OnlyWhenTargetFocused = builder.OnlyWhenTargetFocused;
        DefaultPasteAction = builder.DefaultPasteAction;
        StopBehavior = builder.StopBehavior;
        RedactionMode = builder.RedactionMode;
        MinHitsForPopup = builder.MinHitsForPopup;
        LockIgnore = builder.LockIgnore;
        RememberFileLocations = builder.RememberFileLocations;
        _hasLockIgnore = builder.HasLockIgnore;
        DisabledRuleIds = builder.DisabledRuleIds is null ? null : [.. builder.DisabledRuleIds];
        EnabledRuleIds = builder.EnabledRuleIds is null ? null : [.. builder.EnabledRuleIds];
        NumberFormats = builder.NumberFormats is null ? [] : builder.NumberFormats;
        OrganizationDomains = builder.OrganizationDomains is null ? [] : builder.OrganizationDomains;
        _lockedKeys = new HashSet<string>(builder.LockedKeys, StringComparer.OrdinalIgnoreCase);
        _lockedRules = new HashSet<string>(builder.LockedRules, StringComparer.OrdinalIgnoreCase);
    }

    public static PolicyFile Empty { get; } = new(new Builder());

    public string? Profile { get; }
    public bool? WatchClipboard { get; }
    public bool? WatchCommands { get; }
    public bool? WatchLinks { get; }
    public bool? WatchCryptoSwap { get; }
    public bool? OnlyWhenTargetFocused { get; }
    public string? DefaultPasteAction { get; }
    public string? StopBehavior { get; }
    public string? RedactionMode { get; }
    public int? MinHitsForPopup { get; }
    public bool LockIgnore { get; }
    public bool? RememberFileLocations { get; }
    public IReadOnlyList<string>? DisabledRuleIds { get; }
    public IReadOnlyList<string>? EnabledRuleIds { get; }
    public IReadOnlyList<PolicyNumberFormat> NumberFormats { get; } = [];
    public IReadOnlyList<string> OrganizationDomains { get; } = [];
    public IReadOnlySet<string> LockedRuleIds => _lockedRules;

    public bool Locks(string key) => _lockedKeys.Contains(key);

    public bool LocksRule(string id) => _lockedRules.Contains(id);

    public static PolicyFile? Load(string path)
    {
        var parsed = ParseFile(path);
        return parsed.Status == SourceStatus.Valid ? parsed.Policy : null;
    }

    public static PolicyLoadResult Read(string? filePath, PolicyRegistryRead registry)
    {
        var file = string.IsNullOrWhiteSpace(filePath) ? Source.Missing() : ParseFile(filePath);
        var reg = registry.Unreadable
            ? Source.Invalid()
            : registry.Snapshot is null
                ? Source.Missing()
                : ParseRegistry(registry.Snapshot);

        var warning = (file.Status, reg.Status) switch
        {
            (SourceStatus.Invalid, SourceStatus.Invalid) => PolicyWarnings.Both,
            (SourceStatus.Invalid, _) => PolicyWarnings.File,
            (_, SourceStatus.Invalid) => PolicyWarnings.Registry,
            _ => null
        };

        var policy = Merge(
            file.Status == SourceStatus.Valid ? file.Policy : null,
            reg.Status == SourceStatus.Valid ? reg.Policy : null);
        return new PolicyLoadResult(policy, warning);
    }

    public static PolicyLoadResult Read(string? filePath, PolicyRegistrySnapshot? registry) =>
        Read(filePath, registry is null
            ? PolicyRegistryRead.None
            : new PolicyRegistryRead { Snapshot = registry });

    public AppSettings ApplyCopy(AppSettings user)
    {
        var copy = user.Copy();
        ApplyTo(copy);
        return copy;
    }

    public void ApplyTo(AppSettings settings)
    {
        if (Profile is not null)
            settings.Profile = Profile;
        if (WatchClipboard is bool watchClipboard)
            settings.WatchClipboard = watchClipboard;
        if (WatchCommands is bool watchCommands)
            settings.WatchCommands = watchCommands;
        if (WatchLinks is bool watchLinks)
            settings.WatchLinks = watchLinks;
        if (WatchCryptoSwap is bool watchCrypto)
            settings.WatchCryptoSwap = watchCrypto;
        if (OnlyWhenTargetFocused is bool onlyTarget)
            settings.OnlyWhenTargetFocused = onlyTarget;
        if (DefaultPasteAction is not null)
            settings.DefaultPasteAction = DefaultPasteAction;
        if (StopBehavior is not null)
            settings.StopBehavior = StopBehavior;
        if (RedactionMode is not null)
            settings.RedactionMode = RedactionMode;
        if (MinHitsForPopup is int minHits)
            settings.MinHitsForPopup = minHits;
        if (RememberFileLocations is bool rememberLocations)
            settings.RememberFileLocations = rememberLocations;

        if (DisabledRuleIds is not null)
        {
            foreach (var id in DisabledRuleIds)
            {
                if (!settings.DisabledRuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    settings.DisabledRuleIds.Add(id);
            }

            settings.EnabledRuleIds.RemoveAll(id => Contains(DisabledRuleIds, id));
        }

        if (EnabledRuleIds is not null)
        {
            settings.DisabledRuleIds.RemoveAll(id => Contains(EnabledRuleIds, id));
            foreach (var id in EnabledRuleIds)
            {
                if (!settings.EnabledRuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    settings.EnabledRuleIds.Add(id);
            }
        }
    }

    public static void KeepUserRules(AppSettings user, IReadOnlyList<RuleChoice> choices, IReadOnlySet<string> lockedRuleIds)
    {
        var disabled = new List<string>();
        var enabled = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in choices)
        {
            seen.Add(rule.Id);
            if (lockedRuleIds.Contains(rule.Id))
                continue;
            if (!rule.IsOn)
                disabled.Add(rule.Id);
            else if (!rule.DefaultOn)
                enabled.Add(rule.Id);
        }

        foreach (var id in user.DisabledRuleIds)
        {
            if (lockedRuleIds.Contains(id) || !seen.Contains(id))
                disabled.Add(id);
        }

        foreach (var id in user.EnabledRuleIds)
        {
            if ((lockedRuleIds.Contains(id) || !seen.Contains(id)) &&
                !disabled.Contains(id, StringComparer.OrdinalIgnoreCase))
                enabled.Add(id);
        }

        user.DisabledRuleIds = Distinct(disabled);
        user.EnabledRuleIds = Distinct(enabled);
    }

    private static PolicyFile Merge(PolicyFile? file, PolicyFile? registry)
    {
        if (file is null && registry is null)
            return Empty;
        if (file is null)
            return registry!;
        if (registry is null)
            return file;
        return file.ToBuilder().Overlay(registry.ToBuilder()).Build();
    }

    private static Source ParseFile(string path)
    {
        if (!File.Exists(path))
            return Source.Missing();
        try
        {
            var info = new FileInfo(path);
            if (info.Length > PolicyPaths.MaxBytes)
                return Source.Invalid();
            return ParseJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Source.Invalid();
        }
    }

    private static Source ParseJson(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = 8
            });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                return Source.Invalid();
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                if (!IsKnownKey(property.Name) || property.Value.ValueKind == JsonValueKind.Null)
                    return Source.Invalid();
            }

            var document = JsonSerializer.Deserialize<PolicyDocument>(json, Options);
            if (document is null)
                return Source.Invalid();
            return TryBuild(document, out var policy) ? Source.Valid(policy) : Source.Invalid();
        }
        catch (JsonException)
        {
            return Source.Invalid();
        }
    }

    private static Source ParseRegistry(PolicyRegistrySnapshot snapshot)
    {
        var document = new PolicyDocument();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in snapshot.Values)
        {
            if (value.Kind == PolicyValueKind.Unsupported)
                return Source.Invalid();
            if (!seen.Add(value.Name))
                return Source.Invalid();
            if (!TryMapRegistryValue(document, value))
                return Source.Invalid();
        }

        return TryBuild(document, out var policy) ? Source.Valid(policy) : Source.Invalid();
    }

    private static bool TryMapRegistryValue(PolicyDocument document, PolicyRegistryValue value)
    {
        switch (value.Name.Trim().ToLowerInvariant())
        {
            case "profile":
                if (value.Kind != PolicyValueKind.String)
                    return false;
                document.Profile = value.Text;
                return true;
            case "watchclipboard":
                return TryBool(value, bit => document.WatchClipboard = bit);
            case "watchcommands":
                return TryBool(value, bit => document.WatchCommands = bit);
            case "watchlinks":
                return TryBool(value, bit => document.WatchLinks = bit);
            case "watchcryptoswap":
                return TryBool(value, bit => document.WatchCryptoSwap = bit);
            case "onlywhentargetfocused":
                return TryBool(value, bit => document.OnlyWhenTargetFocused = bit);
            case "lockignore":
                return TryBool(value, bit => document.LockIgnore = bit);
            case "rememberfilelocations":
                return TryBool(value, bit => document.RememberFileLocations = bit);
            case "minhitsforpopup":
                if (value.Kind != PolicyValueKind.Dword)
                    return false;
                document.MinHitsForPopup = value.Number;
                return true;
            case "defaultpasteaction":
                return TryText(value, text => document.DefaultPasteAction = text);
            case "stopbehavior":
                return TryText(value, text => document.StopBehavior = text);
            case "redactionmode":
                return TryText(value, text => document.RedactionMode = text);
            case "disabledruleids":
                if (value.Kind != PolicyValueKind.MultiString)
                    return false;
                document.DisabledRuleIds = value.Lines.ToList();
                return true;
            case "enabledruleids":
                if (value.Kind != PolicyValueKind.MultiString)
                    return false;
                document.EnabledRuleIds = value.Lines.ToList();
                return true;
            case "organizationdomains":
                if (value.Kind != PolicyValueKind.MultiString)
                    return false;
                document.OrganizationDomains = value.Lines.ToList();
                return true;
            case "locksites":
                return TryBool(value, bit => document.LockSites = bit);
            default:
                return false;
        }
    }

    private static bool TryBool(PolicyRegistryValue value, Action<bool> set)
    {
        if (value.Kind != PolicyValueKind.Dword || value.Number is not (0 or 1))
            return false;
        set(value.Number == 1);
        return true;
    }

    private static bool TryText(PolicyRegistryValue value, Action<string> set)
    {
        if (value.Kind != PolicyValueKind.String || value.Text is null)
            return false;
        set(value.Text);
        return true;
    }

    private static bool TryBuild(PolicyDocument document, out PolicyFile policy)
    {
        var builder = new Builder();
        if (document.Profile is not null)
        {
            var profile = document.Profile.Trim().ToLowerInvariant();
            if (!IsToken(profile, 32))
            {
                policy = Empty;
                return false;
            }

            builder.SetProfile(profile);
        }

        if (!SetBool(document.WatchClipboard, builder.SetWatchClipboard, out policy))
            return false;
        if (!SetBool(document.WatchCommands, builder.SetWatchCommands, out policy))
            return false;
        if (!SetBool(document.WatchLinks, builder.SetWatchLinks, out policy))
            return false;
        if (!SetBool(document.WatchCryptoSwap, builder.SetWatchCryptoSwap, out policy))
            return false;
        if (!SetBool(document.OnlyWhenTargetFocused, builder.SetOnlyWhenTargetFocused, out policy))
            return false;
        if (!SetBool(document.RememberFileLocations, builder.SetRememberFileLocations, out policy))
            return false;

        if (document.DefaultPasteAction is not null &&
            !SetChoice(document.DefaultPasteAction, NormalizeAction, builder.SetDefaultPasteAction, out policy))
            return false;
        if (document.StopBehavior is not null &&
            !SetChoice(document.StopBehavior, NormalizeStop, builder.SetStopBehavior, out policy))
            return false;
        if (document.RedactionMode is not null &&
            !SetChoice(document.RedactionMode, NormalizeRedaction, builder.SetRedactionMode, out policy))
            return false;

        if (document.MinHitsForPopup is int hits)
        {
            if (hits is < 1 or > 100)
            {
                policy = Empty;
                return false;
            }

            builder.SetMinHits(hits);
        }

        if (document.LockIgnore is bool lockIgnore)
            builder.SetLockIgnore(lockIgnore);

        List<string>? disabled = null;
        List<string>? enabled = null;
        if (document.DisabledRuleIds is not null)
        {
            if (!TryIds(document.DisabledRuleIds, out var ids))
            {
                policy = Empty;
                return false;
            }

            disabled = ids;
        }

        if (document.EnabledRuleIds is not null)
        {
            if (!TryIds(document.EnabledRuleIds, out var ids))
            {
                policy = Empty;
                return false;
            }

            enabled = ids;
        }

        if (disabled is not null &&
            enabled is not null &&
            disabled.Any(id => Contains(enabled, id)))
        {
            policy = Empty;
            return false;
        }

        if (disabled is not null)
            builder.SetDisabled(disabled);
        if (enabled is not null)
            builder.SetEnabled(enabled);
        if (document.OrganizationDomains is not null)
        {
            if (!TryDomains(document.OrganizationDomains, out var domains))
            {
                policy = Empty;
                return false;
            }

            builder.SetOrganizationDomains(domains);
        }

        if (document.LockSites is bool lockSites)
            builder.SetLockSites(lockSites);
        if (document.NumberFormats is not null)
        {
            if (!TryFormats(document.NumberFormats, out var formats))
            {
                policy = Empty;
                return false;
            }

            builder.SetNumberFormats(formats);
        }

        policy = builder.Build();
        return true;
    }

    private static bool SetBool(bool? value, Action<bool> set, out PolicyFile policy)
    {
        policy = Empty;
        if (value is bool bit)
            set(bit);
        return true;
    }

    private static bool SetChoice(string raw, Func<string, string?> normalize, Action<string> set, out PolicyFile policy)
    {
        var choice = normalize(raw);
        if (choice is null)
        {
            policy = Empty;
            return false;
        }

        set(choice);
        policy = Empty;
        return true;
    }

    private static bool TryIds(IReadOnlyList<string> raw, out List<string> ids)
    {
        ids = [];
        if (raw.Count > 200)
            return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            var id = item.Trim();
            if (!IsToken(id, 80) || !seen.Add(id))
                return false;
            ids.Add(id);
        }

        return true;
    }

    private static bool TryDomains(IReadOnlyList<string> raw, out List<string> domains)
    {
        domains = [];
        if (raw.Count > 200)
            return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            var domain = BookmarkDomains.Normalize(item);
            if (domain is null || !seen.Add(domain))
                return false;
            domains.Add(domain);
        }

        return true;
    }

    private static bool TryFormats(IReadOnlyList<NumberFormatDocument> raw, out List<PolicyNumberFormat> formats)
    {
        formats = [];
        if (raw.Count > 20)
            return false;
        foreach (var item in raw)
        {
            var name = item.Name?.Trim() ?? "";
            var pattern = item.Pattern?.Trim() ?? "";
            if (!IsToken(name, 40) || pattern.Length is < 1 or > 200)
                return false;
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException)
            {
                return false;
            }

            var category = string.IsNullOrWhiteSpace(item.Category) ? "custom" : item.Category.Trim().ToLowerInvariant();
            if (category is not "personal" and not "payment" and not "secrets" and not "custom")
                return false;
            formats.Add(new PolicyNumberFormat
            {
                Name = name,
                Pattern = pattern,
                Category = category,
                Reversible = item.Reversible ?? true
            });
        }

        return true;
    }

    private static bool IsKnownKey(string name) =>
        PolicyKeys.All.Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsToken(string value, int max)
    {
        if (value.Length < 1 || value.Length > max)
            return false;
        if (!char.IsAsciiLetterOrDigit(value[0]))
            return false;
        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-')
                return false;
        }

        return true;
    }

    private static string? NormalizeAction(string value) => value.Trim().ToLowerInvariant() switch
    {
        "ask" => "ask",
        "ignore" => "ignore",
        "stop" => "stop",
        "redact" => "redact",
        _ => null
    };

    private static string? NormalizeStop(string value) => value.Trim().ToLowerInvariant() switch
    {
        "restore-previous" => "restore-previous",
        "clear-clipboard" => "clear-clipboard",
        _ => null
    };

    private static string? NormalizeRedaction(string value) => value.Trim().ToLowerInvariant() switch
    {
        "reversible" => "reversible",
        "permanent" => "permanent",
        _ => null
    };

    private static bool Contains(IReadOnlyList<string> list, string id) =>
        list.Any(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static List<string> Distinct(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var id in ids)
        {
            if (seen.Add(id))
                list.Add(id);
        }

        return list;
    }

    private Builder ToBuilder()
    {
        var builder = new Builder();
        if (Locks(PolicyKeys.Profile) && Profile is not null)
            builder.SetProfile(Profile);
        if (WatchClipboard is bool watchClipboard && Locks(PolicyKeys.WatchClipboard))
            builder.SetWatchClipboard(watchClipboard);
        if (WatchCommands is bool watchCommands && Locks(PolicyKeys.WatchCommands))
            builder.SetWatchCommands(watchCommands);
        if (WatchLinks is bool watchLinks && Locks(PolicyKeys.WatchLinks))
            builder.SetWatchLinks(watchLinks);
        if (WatchCryptoSwap is bool watchCrypto && Locks(PolicyKeys.WatchCryptoSwap))
            builder.SetWatchCryptoSwap(watchCrypto);
        if (OnlyWhenTargetFocused is bool onlyTarget && Locks(PolicyKeys.OnlyWhenTargetFocused))
            builder.SetOnlyWhenTargetFocused(onlyTarget);
        if (DefaultPasteAction is not null && Locks(PolicyKeys.DefaultPasteAction))
            builder.SetDefaultPasteAction(DefaultPasteAction);
        if (StopBehavior is not null && Locks(PolicyKeys.StopBehavior))
            builder.SetStopBehavior(StopBehavior);
        if (RedactionMode is not null && Locks(PolicyKeys.RedactionMode))
            builder.SetRedactionMode(RedactionMode);
        if (MinHitsForPopup is int minHits && Locks(PolicyKeys.MinHitsForPopup))
            builder.SetMinHits(minHits);
        if (_hasLockIgnore)
            builder.SetLockIgnore(LockIgnore);
        if (RememberFileLocations is bool rememberLocations && Locks(PolicyKeys.RememberFileLocations))
            builder.SetRememberFileLocations(rememberLocations);
        if (DisabledRuleIds is not null && Locks(PolicyKeys.DisabledRuleIds))
            builder.SetDisabled(DisabledRuleIds.ToList());
        if (EnabledRuleIds is not null && Locks(PolicyKeys.EnabledRuleIds))
            builder.SetEnabled(EnabledRuleIds.ToList());
        if (OrganizationDomains.Count > 0 && Locks(PolicyKeys.OrganizationDomains))
            builder.SetOrganizationDomains(OrganizationDomains.ToList());
        if (Locks(PolicyKeys.LockSites))
            builder.SetLockSites(true);
        return builder;
    }

    private enum SourceStatus
    {
        Missing,
        Valid,
        Invalid
    }

    private readonly struct Source
    {
        public SourceStatus Status { get; init; }
        public PolicyFile? Policy { get; init; }

        public static Source Missing() => new() { Status = SourceStatus.Missing };

        public static Source Invalid() => new() { Status = SourceStatus.Invalid };

        public static Source Valid(PolicyFile policy) => new() { Status = SourceStatus.Valid, Policy = policy };
    }

    private sealed class PolicyDocument
    {
        public string? Profile { get; set; }
        public bool? WatchClipboard { get; set; }
        public bool? WatchCommands { get; set; }
        public bool? WatchLinks { get; set; }
        public bool? WatchCryptoSwap { get; set; }
        public bool? OnlyWhenTargetFocused { get; set; }
        public string? DefaultPasteAction { get; set; }
        public string? StopBehavior { get; set; }
        public string? RedactionMode { get; set; }
        public int? MinHitsForPopup { get; set; }
        public bool? LockIgnore { get; set; }
        public bool? RememberFileLocations { get; set; }
        public List<string>? DisabledRuleIds { get; set; }
        public List<string>? EnabledRuleIds { get; set; }
        public List<string>? OrganizationDomains { get; set; }
        public bool? LockSites { get; set; }
        public List<NumberFormatDocument>? NumberFormats { get; set; }
    }

    private sealed class NumberFormatDocument
    {
        public string? Name { get; set; }
        public string? Pattern { get; set; }
        public string? Category { get; set; }
        public bool? Reversible { get; set; }
    }

    private sealed class Builder
    {
        public string? Profile { get; private set; }
        public bool? WatchClipboard { get; private set; }
        public bool? WatchCommands { get; private set; }
        public bool? WatchLinks { get; private set; }
        public bool? WatchCryptoSwap { get; private set; }
        public bool? OnlyWhenTargetFocused { get; private set; }
        public string? DefaultPasteAction { get; private set; }
        public string? StopBehavior { get; private set; }
        public string? RedactionMode { get; private set; }
        public int? MinHitsForPopup { get; private set; }
        public bool LockIgnore { get; private set; }
        public bool? RememberFileLocations { get; private set; }
        public bool HasLockIgnore { get; private set; }
        public List<string>? DisabledRuleIds { get; private set; }
        public List<string>? EnabledRuleIds { get; private set; }
        public List<string>? OrganizationDomains { get; private set; }
        public List<PolicyNumberFormat>? NumberFormats { get; private set; }
        public HashSet<string> LockedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LockedRules { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void SetProfile(string value)
        {
            Profile = value;
            Lock(PolicyKeys.Profile);
        }

        public void SetWatchClipboard(bool value)
        {
            WatchClipboard = value;
            Lock(PolicyKeys.WatchClipboard);
        }

        public void SetWatchCommands(bool value)
        {
            WatchCommands = value;
            Lock(PolicyKeys.WatchCommands);
        }

        public void SetWatchLinks(bool value)
        {
            WatchLinks = value;
            Lock(PolicyKeys.WatchLinks);
        }

        public void SetWatchCryptoSwap(bool value)
        {
            WatchCryptoSwap = value;
            Lock(PolicyKeys.WatchCryptoSwap);
        }

        public void SetOnlyWhenTargetFocused(bool value)
        {
            OnlyWhenTargetFocused = value;
            Lock(PolicyKeys.OnlyWhenTargetFocused);
        }

        public void SetDefaultPasteAction(string value)
        {
            DefaultPasteAction = value;
            Lock(PolicyKeys.DefaultPasteAction);
        }

        public void SetStopBehavior(string value)
        {
            StopBehavior = value;
            Lock(PolicyKeys.StopBehavior);
        }

        public void SetRedactionMode(string value)
        {
            RedactionMode = value;
            Lock(PolicyKeys.RedactionMode);
        }

        public void SetMinHits(int value)
        {
            MinHitsForPopup = value;
            Lock(PolicyKeys.MinHitsForPopup);
        }

        public void SetRememberFileLocations(bool value)
        {
            RememberFileLocations = value;
            Lock(PolicyKeys.RememberFileLocations);
        }

        public void SetLockIgnore(bool value)
        {
            HasLockIgnore = true;
            LockIgnore = value;
            if (value)
                Lock(PolicyKeys.LockIgnore);
            else
                LockedKeys.Remove(PolicyKeys.LockIgnore);
        }

        public void SetDisabled(List<string> ids)
        {
            DisabledRuleIds = ids;
            if (EnabledRuleIds is not null)
                EnabledRuleIds = EnabledRuleIds.Where(id => !Contains(ids, id)).ToList();
            Lock(PolicyKeys.DisabledRuleIds);
        }

        public void SetNumberFormats(List<PolicyNumberFormat> formats)
        {
            NumberFormats = formats;
            Lock(PolicyKeys.NumberFormats);
        }

        public void SetEnabled(List<string> ids)
        {
            EnabledRuleIds = ids;
            if (DisabledRuleIds is not null)
                DisabledRuleIds = DisabledRuleIds.Where(id => !Contains(ids, id)).ToList();
            Lock(PolicyKeys.EnabledRuleIds);
        }

        public void SetOrganizationDomains(List<string> domains)
        {
            OrganizationDomains = domains;
            Lock(PolicyKeys.OrganizationDomains);
        }

        public void SetLockSites(bool value)
        {
            if (value)
                Lock(PolicyKeys.LockSites);
            else
                LockedKeys.Remove(PolicyKeys.LockSites);
        }

        public Builder Overlay(Builder above)
        {
            if (above.Locks(PolicyKeys.Profile) && above.Profile is not null)
                SetProfile(above.Profile);
            if (above.WatchClipboard is bool watchClipboard && above.Locks(PolicyKeys.WatchClipboard))
                SetWatchClipboard(watchClipboard);
            if (above.WatchCommands is bool watchCommands && above.Locks(PolicyKeys.WatchCommands))
                SetWatchCommands(watchCommands);
            if (above.WatchLinks is bool watchLinks && above.Locks(PolicyKeys.WatchLinks))
                SetWatchLinks(watchLinks);
            if (above.WatchCryptoSwap is bool watchCrypto && above.Locks(PolicyKeys.WatchCryptoSwap))
                SetWatchCryptoSwap(watchCrypto);
            if (above.OnlyWhenTargetFocused is bool onlyTarget && above.Locks(PolicyKeys.OnlyWhenTargetFocused))
                SetOnlyWhenTargetFocused(onlyTarget);
            if (above.RememberFileLocations is bool rememberLocations && above.Locks(PolicyKeys.RememberFileLocations))
                SetRememberFileLocations(rememberLocations);
            if (above.DefaultPasteAction is not null && above.Locks(PolicyKeys.DefaultPasteAction))
                SetDefaultPasteAction(above.DefaultPasteAction);
            if (above.StopBehavior is not null && above.Locks(PolicyKeys.StopBehavior))
                SetStopBehavior(above.StopBehavior);
            if (above.RedactionMode is not null && above.Locks(PolicyKeys.RedactionMode))
                SetRedactionMode(above.RedactionMode);
            if (above.MinHitsForPopup is int minHits && above.Locks(PolicyKeys.MinHitsForPopup))
                SetMinHits(minHits);
            if (above.HasLockIgnore)
                SetLockIgnore(above.LockIgnore);
            if (above.DisabledRuleIds is not null && above.Locks(PolicyKeys.DisabledRuleIds))
                SetDisabled(above.DisabledRuleIds.ToList());
            if (above.EnabledRuleIds is not null && above.Locks(PolicyKeys.EnabledRuleIds))
                SetEnabled(above.EnabledRuleIds.ToList());
            if (above.OrganizationDomains is not null && above.Locks(PolicyKeys.OrganizationDomains))
                SetOrganizationDomains(above.OrganizationDomains.ToList());
            if (above.Locks(PolicyKeys.LockSites))
                SetLockSites(true);
            if (above.NumberFormats is not null && above.Locks(PolicyKeys.NumberFormats))
                SetNumberFormats(above.NumberFormats.ToList());
            return this;
        }

        public PolicyFile Build()
        {
            LockedRules.Clear();
            if (DisabledRuleIds is not null)
            {
                foreach (var id in DisabledRuleIds)
                    LockedRules.Add(id);
            }

            if (EnabledRuleIds is not null)
            {
                foreach (var id in EnabledRuleIds)
                    LockedRules.Add(id);
            }

            return new PolicyFile(this);
        }

        private void Lock(string key) => LockedKeys.Add(key);

        private bool Locks(string key) => LockedKeys.Contains(key);
    }
}
