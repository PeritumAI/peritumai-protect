using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Services;

public sealed class AppState
{
    public AppState()
        : this(Paths.Root, PolicyBinding.Machine)
    {
    }

    public AppState(string root)
        : this(root, null)
    {
    }

    public AppState(string root, PolicyBinding? policy, string? region = null)
    {
        HomeRegion = CountryRegions.Canonical(string.IsNullOrWhiteSpace(region)
            ? System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName
            : region);
        Root = root;
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "rules"));
        Directory.CreateDirectory(Path.Combine(Root, "rules", "imports"));
        Directory.CreateDirectory(Path.Combine(Root, "logs"));
        Directory.CreateDirectory(Path.Combine(Root, "maps"));
        SettingsStore = new SettingsStore(Path.Combine(Root, "settings.json"));
        Events = new EventLog(Path.Combine(Root, "events.jsonl"));
        AllowList = new AllowListStore(Path.Combine(Root, "allowlist.json"));
        TrustedSites = new TrustedSiteStore(Path.Combine(Root, "sites.json"));
        _policyBinding = policy;
        UserSettings = SettingsStore.Load();
        Settings = UserSettings.Copy();
        Policy = PolicyFile.Empty;
        if (policy is not null)
            ReloadPolicy();
        ReloadPacks();
        var ocr = new WindowsOcrEngine();
        Engine = new ProtectService(ocr)
        {
            LinkCatalog = () => BrandCatalog.LoadAll(BrandDomains()),
            TrustedSite = TrustedSites.Contains
        };
        OcrStatus = Engine.OcrStatus;
    }

    private readonly PolicyBinding? _policyBinding;

    public string Root { get; }

    public ProtectService Engine { get; }
    public string OcrStatus { get; }
    public SettingsStore SettingsStore { get; }
    public EventLog Events { get; }
    public AllowListStore AllowList { get; }
    public TrustedSiteStore TrustedSites { get; }
    public AppSettings UserSettings { get; private set; }
    public AppSettings Settings { get; private set; }
    public PolicyFile Policy { get; private set; } = PolicyFile.Empty;
    public string? PolicyWarning { get; private set; }
    public event Action? PolicyChanged;
    public string HomeRegion { get; }
    public List<RulePack> Packs { get; private set; } = [];
    public PauseSchedule Pause { get; } = new();
    public bool Paused => Pause.IsPaused(DateTimeOffset.Now);
    public ScanSession? Last { get; set; }
    public string? LastProcess { get; set; }

    public BrandCatalog RegionLinkCatalog() =>
        BrandCatalog.ForRegion(HomeRegion, extraDomains: BrandDomains());

    public IEnumerable<string> BrandDomains() =>
        Policy.OrganizationDomains.Concat(TrustedSites.Domains);

    public IReadOnlyList<Rule> ActiveRules()
    {
        var enabled = Settings.EnabledRuleIds
            .Concat(CountryRegions.RuleIds(Packs, HomeRegion))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rules = RulePackLoader.SelectActive(Packs, enabled, Settings.DisabledRuleIds).ToList();
        foreach (var format in Policy.NumberFormats)
            rules.Add(format.ToRule());
        return rules;
    }

    public void ReloadPacks()
    {
        var packs = BuiltinRules.LoadEmbedded().ToList();
        var rulesDir = Path.Combine(Root, "rules");
        if (!Directory.Exists(rulesDir))
        {
            Packs = packs;
            return;
        }

        AddPackFiles(packs, rulesDir);
        var imports = Path.Combine(rulesDir, "imports");
        if (Directory.Exists(imports))
            AddPackFiles(packs, imports);

        Packs = packs;
    }

    private static void AddPackFiles(List<RulePack> packs, string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                var pack = RulePackLoader.LoadFile(file);
                if (packs.Any(existing => string.Equals(existing.Id, pack.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                packs.Add(pack);
            }
            catch
            {
                // A bad pack on disk must not stop startup. The user can fix the file.
            }
        }
    }

    public void ReloadPolicy()
    {
        if (_policyBinding is null)
            return;
        var result = PolicyFile.Read(_policyBinding.FilePath, _policyBinding.Registry.Read());
        Policy = result.Policy;
        PolicyWarning = result.Warning;
        foreach (var domain in Policy.OrganizationDomains)
            TrustedSites.Add(domain);
        Settings = Policy.ApplyCopy(UserSettings);
        PolicyChanged?.Invoke();
    }

    public void SaveSettings()
    {
        SettingsStore.Save(UserSettings);
        Settings = Policy.ApplyCopy(UserSettings);
    }
}
