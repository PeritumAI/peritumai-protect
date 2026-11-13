using System.Collections.ObjectModel;
using System.Windows;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Peritum.Protect.App.Services;
using Peritum.Protect.App;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Profiles;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly Action _saved;
    private readonly Func<string?>? _pickTextFile;
    private readonly Func<string?>? _pickRulesFile;
    private readonly Func<string?>? _saveRulesFile;
    private readonly Func<string?>? _pickBookmarksFile;
    private readonly Func<string?>? _saveSitesFile;
    private bool _loading;

    public SettingsViewModel(
        AppState state,
        Action saved,
        Func<string?>? pickTextFile = null,
        Func<string?>? pickRulesFile = null,
        Func<string?>? saveRulesFile = null,
        Func<string?>? pickBookmarksFile = null,
        Func<string?>? saveSitesFile = null)
    {
        _state = state;
        _saved = saved;
        _pickTextFile = pickTextFile;
        _pickRulesFile = pickRulesFile;
        _saveRulesFile = saveRulesFile;
        _pickBookmarksFile = pickBookmarksFile;
        _saveSitesFile = saveSitesFile;
        Sites.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowSitesEmpty));
        Rules.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
            {
                foreach (RuleToggle rule in args.NewItems)
                    rule.PropertyChanged += (_, _) => MarkDirty();
            }
            MarkDirty();
        };
        Terms.CollectionChanged += (_, _) => MarkDirty();
        Load();
    }

    public ObservableCollection<RuleToggle> Rules { get; } = [];
    public ObservableCollection<RuleGroup> RuleGroups { get; } = [];
    public ObservableCollection<UserTerm> Terms { get; } = [];
    public ObservableCollection<string> Ignored { get; } = [];
    public ObservableCollection<string> Sites { get; } = [];
    public bool ShowSitesEmpty => Sites.Count == 0;
    public ObservableCollection<ProtectionProfile> Profiles { get; } = [];
    public ObservableCollection<LanguageChoice> InterfaceLanguages { get; } = [];
    public string OcrStatus => _state.OcrStatus;

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _watchClipboard = true;
    [ObservableProperty] private bool _rememberFileLocations = true;
    [ObservableProperty] private bool _onlyWhenTargetFocused;
    [ObservableProperty] private string _targetApps = "";
    [ObservableProperty] private string _defaultPasteAction = "ask";
    [ObservableProperty] private string _stopBehavior = "restore-previous";
    [ObservableProperty] private string _copyNamePattern = "{name}_protect{ext}";
    [ObservableProperty] private string _copyFolderMode = "beside";
    [ObservableProperty] private string _copyFolder = "";
    [ObservableProperty] private string _termText = "";
    [ObservableProperty] private string _termLabel = "Custom";
    [ObservableProperty] private string _termKind = "exact";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _newSite = "";
    [ObservableProperty] private string? _selectedSite;
    [ObservableProperty] private int _minHitsForPopup = 1;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _theme = "system";
    [ObservableProperty] private string _uiLanguage = "";
    private string _interfaceLanguage = "en";

    public string InterfaceLanguage
    {
        get => _interfaceLanguage;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "en" : value.Trim();
            if (!TextCatalog.IsUserLanguage(next))
                next = "en";
            if (_interfaceLanguage == next)
                return;
            _interfaceLanguage = next;
            OnPropertyChanged(nameof(InterfaceLanguage));
            if (_loading || TextCatalog.IsDeveloperLanguage(UiLanguage))
                return;
            UiLanguage = "";
        }
    }

    partial void OnUiLanguageChanged(string value)
    {
        if (_loading)
            return;
        TextCatalog.Use(TextCatalog.IsDeveloperLanguage(value) ? value : "en");
    }
    [ObservableProperty] private bool _watchCommands = true;
    [ObservableProperty] private bool _watchLinks;
    [ObservableProperty] private bool _watchCryptoSwap;
    [ObservableProperty] private string _redactionMode = "reversible";
    [ObservableProperty] private string _policyWarning = "";
    [ObservableProperty] private bool _showProfileLock;
    [ObservableProperty] private bool _showIgnoreLock;
    [ObservableProperty] private string _selectedProfileId = "general";
    [ObservableProperty] private string _profileDescription = "";
    [ObservableProperty] private string _suggestedTermsLine = "";
    [ObservableProperty] private int _mapRetentionDays = 30;

    public SettingLock ClipboardEdit { get; } = new();
    public SettingLock FileLocationsEdit { get; } = new();
    public SettingLock TargetFocusEdit { get; } = new();
    public SettingLock PasteActionEdit { get; } = new();
    public SettingLock StopEdit { get; } = new();
    public SettingLock MinHitsEdit { get; } = new();
    public SettingLock CommandEdit { get; } = new();
    public SettingLock LinkEdit { get; } = new();
    public SettingLock SitesEdit { get; } = new();
    public SettingLock CryptoEdit { get; } = new();
    public SettingLock RedactionEdit { get; } = new();
    public SettingLock ProfileEdit { get; } = new();

    public string ProfileLine => TextCatalog.Format("profileLine", ("name", ProfileNames.Display(_state.Settings.Profile)));

    public bool HasSuggestedTerms =>
        ProfileCatalog.Find(SelectedProfileId)?.SuggestedTerms.Any(term => !string.IsNullOrWhiteSpace(term.Text)) == true;

    public bool CanAddSuggestedTerms => ProfileEdit.CanEdit && HasSuggestedTerms;
    public string CommandWarningText => UiText.CommandWarning;
    public string CryptoWarningText => UiText.CryptoWarning;
    public string RedactionCaption => UiText.Redaction;
    public string RedactionHint => UiText.RedactionHint;

    public IReadOnlyList<ActionChoice> PasteActions { get; } =
    [
        new("ask", TextCatalog.Get("askEveryTime")),
        new("ignore", "Ignore"),
        new("stop", "Stop"),
        new("redact", "Redact")
    ];

    public IReadOnlyList<ActionChoice> StopBehaviors { get; } =
    [
        new("restore-previous", TextCatalog.Get("stopRestore")),
        new("clear-clipboard", TextCatalog.Get("stopClear"))
    ];

    public IReadOnlyList<ActionChoice> TermKinds { get; } =
    [
        new("exact", "Exact"),
        new("regex", "Regex")
    ];

    public IReadOnlyList<ActionChoice> Themes { get; } =
    [
        new("system", "System"),
        new("light", "Light"),
        new("dark", "Dark")
    ];

    public IReadOnlyList<ActionChoice> RedactionModes { get; } =
    [
        new("reversible", TextCatalog.Get("modeReversible")),
        new("permanent", TextCatalog.Get("modePermanent"))
    ];

    [RelayCommand]
    public void AddTaughtFormat(UserTerm term)
    {
        Terms.Add(term);
        IsDirty = true;
    }

    private void AddTerm()
    {
        var text = TermText.Trim();
        if (text.Length == 0)
            return;
        if (TermKind == "regex")
        {
            try
            {
                _ = new Regex(text, RegexOptions.None, TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException)
            {
                Status = TextCatalog.Get("regexInvalid");
                return;
            }
        }
        Terms.Add(new UserTerm { Label = TermLabel, Text = text, Kind = TermKind == "regex" ? "regex" : "exact" });
        TermText = "";
        Status = "";
    }

    [RelayCommand]
    private void ImportTerms()
    {
        var path = _pickTextFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        var label = string.IsNullOrWhiteSpace(TermLabel) ? "Imported" : TermLabel;
        var added = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var text = raw.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
                continue;
            Terms.Add(new UserTerm { Label = label, Text = text, Kind = "exact" });
            added++;
        }
        Status = added == 0 ? TextCatalog.Get("noTerms") : TextCatalog.Format("importedTerms", ("count", added));
    }

    [RelayCommand]
    private void ExportRules()
    {
        var path = _saveRulesFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return;
        var packs = _state.Packs.Where(pack =>
            !pack.Id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase) &&
            !pack.Id.Equals(UserDictionary.PackId, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(path, RuleExchange.Export(packs, Terms));
        Status = TextCatalog.Get("exportedRules");
    }

    [RelayCommand]
    private void AddSite()
    {
        if (!SitesEdit.CanEdit)
            return;
        if (!RememberSite(NewSite))
        {
            Status = TextCatalog.Get("sitesInvalid");
            return;
        }

        NewSite = "";
        Status = TextCatalog.Get("sitesAdded");
    }

    [RelayCommand]
    private void RemoveSite()
    {
        if (!SitesEdit.CanEdit || string.IsNullOrWhiteSpace(SelectedSite))
            return;
        if (SelectedSite == TextCatalog.Get("sitesLegacy"))
            _state.TrustedSites.RemoveLegacy();
        else
            _state.TrustedSites.Remove(SelectedSite);
        ReloadSites();
    }

    [RelayCommand]
    private void ImportSites()
    {
        if (!SitesEdit.CanEdit)
            return;
        var path = _pickBookmarksFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        var added = ImportBookmarks(File.ReadAllText(path));
        Status = TextCatalog.Format("sitesImported", ("count", added));
    }

    [RelayCommand]
    private void ExportSites()
    {
        var path = _saveSitesFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return;
        File.WriteAllText(path, ExportSitesJson());
        Status = TextCatalog.Get("sitesExported");
    }

    public int ImportBookmarks(string html)
    {
        var added = 0;
        foreach (var domain in BookmarkDomains.FromHtml(html))
        {
            if (RememberSite(domain))
                added++;
        }

        return added;
    }

    public string ExportSitesJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, List<string>> { ["sites"] = Sites.ToList() },
            JsonDefaults.Indented);
    }

    private bool RememberSite(string raw)
    {
        var domain = BookmarkDomains.Normalize(raw);
        if (domain is null)
            return false;
        var added = _state.TrustedSites.Add(domain);
        ReloadSites();
        return added;
    }

    private void ReloadSites()
    {
        var selected = SelectedSite;
        Sites.Clear();
        foreach (var domain in _state.TrustedSites.Domains)
            Sites.Add(domain);
        var legacy = TextCatalog.Get("sitesLegacy");
        for (var i = 0; i < _state.TrustedSites.LegacyCount; i++)
            Sites.Add(legacy);
        SelectedSite = selected is not null && Sites.Contains(selected) ? selected : null;
    }

    [RelayCommand]
    private void ImportRules()
    {
        var path = _pickRulesFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!File.Exists(path))
        {
            Status = TextCatalog.Get("fileUnreadable");
            return;
        }

        try
        {
            Status = ImportRulesJson(File.ReadAllText(path));
        }
        catch (IOException)
        {
            Status = TextCatalog.Get("fileUnreadable");
        }
        catch (UnauthorizedAccessException)
        {
            Status = TextCatalog.Get("fileUnreadable");
        }
    }

    public string ImportRulesJson(string json)
    {
        var result = RuleExchange.Read(
            json,
            _state.Packs.Select(pack => pack.Id),
            _state.Packs.SelectMany(pack => pack.Rules).Select(rule => rule.Id),
            Terms.Select(term => term.Text));
        if (!result.Accepted)
            return result.Message;

        if (result.Packs.Count > 0)
        {
            RuleExchange.WritePacks(Path.Combine(_state.Root, "rules", "imports"), result.Packs);
            _state.ReloadPacks();
            foreach (var pack in result.Packs)
            {
                foreach (var rule in pack.Rules)
                {
                    if (Rules.Any(existing => existing.Id.Equals(rule.Id, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var defaultOn = pack.Enabled && rule.Enabled;
                    Rules.Add(new RuleToggle
                    {
                        Id = rule.Id,
                        Label = RuleName(rule),
                        Category = string.IsNullOrWhiteSpace(rule.Category) ? "custom" : rule.Category,
                        DefaultOn = defaultOn,
                        IsOn = defaultOn,
                        IsLocked = _state.Policy.LocksRule(rule.Id)
                    });
                }
            }

            RebuildGroups();
        }

        foreach (var term in result.Terms)
            Terms.Add(term);
        return result.Message;
    }

    [RelayCommand]
    private void RemoveIgnored(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        _state.AllowList.Remove(value);
        Ignored.Remove(value);
        Status = TextCatalog.Get("ignoredRemoved");
    }

    [RelayCommand]
    private void AddExplorerMenu()
    {
        try
        {
            var exe = ExecutablePathResolver.Resolve(AppContext.BaseDirectory, Environment.ProcessPath);
            ExplorerMenu.Register(exe);
            Status = TextCatalog.Get("explorerAdded");
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveExplorerMenu()
    {
        ExplorerMenu.Unregister();
        Status = TextCatalog.Get("explorerRemoved");
    }

    public bool CopyBeside
    {
        get => CopyFolderMode != "folder";
        set
        {
            if (value)
                CopyFolderMode = "beside";
        }
    }

    public bool CopyInFolder
    {
        get => CopyFolderMode == "folder";
        set
        {
            if (value)
                CopyFolderMode = "folder";
        }
    }

    [RelayCommand]
    private void ChooseCopyFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = TextCatalog.Get("folderForCopies"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(CopyFolder) ? CopyFolder : ""
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;
        CopyFolder = dialog.SelectedPath;
        CopyFolderMode = "folder";
    }

    [RelayCommand]
    private void RemoveTerm(UserTerm? term)
    {
        if (term is not null)
            Terms.Remove(term);
    }

    [RelayCommand]
    private void Save() => SaveSettings();

    public bool SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(CopyNamePattern) || !CopyNamePattern.Contains("{name}", StringComparison.Ordinal))
        {
            Status = TextCatalog.Get("copyNameNeeds");
            return false;
        }

        var settings = _state.UserSettings;
        settings.StartWithWindows = StartWithWindows;
        if (ClipboardEdit.CanEdit)
            settings.WatchClipboard = WatchClipboard;
        if (FileLocationsEdit.CanEdit)
            settings.RememberFileLocations = RememberFileLocations;
        if (TargetFocusEdit.CanEdit)
            settings.OnlyWhenTargetFocused = OnlyWhenTargetFocused;
        settings.TargetProcesses = TargetApps
            .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (settings.TargetProcesses.Count == 0)
            settings.TargetProcesses = [.. TargetProcessList.Default];
        if (PasteActionEdit.CanEdit)
            settings.DefaultPasteAction = DefaultPasteAction;
        if (StopEdit.CanEdit)
            settings.StopBehavior = StopBehavior;
        settings.CopyNamePattern = CopyNamePattern.Trim();
        settings.CopyFolderMode = CopyFolderMode == "folder" ? "folder" : "beside";
        settings.CopyFolder = CopyFolder.Trim();
        if (MinHitsEdit.CanEdit)
            settings.MinHitsForPopup = Math.Max(1, MinHitsForPopup);
        settings.Theme = Theme;
        settings.UiLanguage = TextCatalog.NormalizeSetting(UiLanguage);
        UiLanguage = settings.UiLanguage;
        TextCatalog.Use(TextCatalog.IsDeveloperLanguage(settings.UiLanguage) ? settings.UiLanguage : "en");
        if (Application.Current is global::Peritum.Protect.App.App app)
            app.ApplyTheme?.Invoke(Theme);
        if (CommandEdit.CanEdit)
            settings.WatchCommands = WatchCommands;
        if (LinkEdit.CanEdit)
            settings.WatchLinks = WatchLinks;
        if (CryptoEdit.CanEdit)
            settings.WatchCryptoSwap = WatchCryptoSwap;
        if (RedactionEdit.CanEdit)
            settings.RedactionMode = RedactionMode == "permanent" ? "permanent" : "reversible";
        if (ProfileEdit.CanEdit && !string.IsNullOrWhiteSpace(SelectedProfileId))
            settings.Profile = SelectedProfileId.Trim().ToLowerInvariant();
        settings.MapRetentionDays = MapRetentionDays is < 1 or > 3650 ? 30 : MapRetentionDays;
        PolicyFile.KeepUserRules(
            settings,
            Rules.Select(rule => new RuleChoice(rule.Id, rule.IsOn, rule.DefaultOn)).ToList(),
            _state.Policy.LockedRuleIds);
        _state.SaveSettings();
        ApplyLockedValues();
        UserDictionary.Save(Paths.UserRulesFile, Terms.ToList());
        _state.ReloadPacks();
        StartupShortcutService.Apply(settings.StartWithWindows);
        IsDirty = false;
        Status = TextCatalog.Get("savedOnPc");
        OnPropertyChanged(nameof(ProfileLine));
        _saved();
        return true;
    }

    [RelayCommand]
    private void AddSuggestedTerms()
    {
        if (!CanAddSuggestedTerms)
            return;
        var profile = ProfileCatalog.Find(SelectedProfileId);
        if (profile is null)
            return;
        var added = 0;
        foreach (var term in profile.SuggestedTerms)
        {
            var text = term.Text.Trim();
            if (text.Length == 0)
                continue;
            if (Terms.Any(existing => existing.Text.Equals(text, StringComparison.Ordinal)))
                continue;
            var kind = term.Kind == "regex" ? "regex" : "exact";
            if (kind == "regex")
            {
                try
                {
                    _ = new Regex(text, RegexOptions.None, TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException)
                {
                    Status = TextCatalog.Format("suggestedInvalid", ("label", term.Label));
                    return;
                }
            }

            Terms.Add(new UserTerm { Label = term.Label, Text = text, Kind = kind });
            added++;
        }

        Status = added == 0
            ? TextCatalog.Get("examplesExist")
            : TextCatalog.Format("addedExamples", ("count", added));
    }

    public void DiscardChanges() => Load();

    public void RefreshPolicy()
    {
        _loading = true;
        var settings = _state.Settings;
        WatchClipboard = settings.WatchClipboard;
        RememberFileLocations = settings.RememberFileLocations;
        OnlyWhenTargetFocused = settings.OnlyWhenTargetFocused;
        DefaultPasteAction = settings.DefaultPasteAction;
        StopBehavior = settings.StopBehavior;
        MinHitsForPopup = settings.MinHitsForPopup;
        WatchCommands = settings.WatchCommands;
        WatchLinks = settings.WatchLinks;
        WatchCryptoSwap = settings.WatchCryptoSwap;
        RedactionMode = settings.RedactionMode;
        UiLanguage = TextCatalog.NormalizeSetting(settings.UiLanguage);
        FillInterfaceLanguages();
        var disabled = new HashSet<string>(settings.DisabledRuleIds, StringComparer.OrdinalIgnoreCase);
        var forced = new HashSet<string>(settings.EnabledRuleIds, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
        {
            rule.IsLocked = _state.Policy.LocksRule(rule.Id);
            rule.IsOn = !disabled.Contains(rule.Id) && (forced.Contains(rule.Id) || rule.DefaultOn);
        }

        SyncLocks();
        _loading = false;
    }

    [RelayCommand]
    private void RestoreTargetApps() =>
        TargetApps = string.Join(Environment.NewLine, TargetProcessList.Default);

    [RelayCommand]
    private void DeleteActivityLog()
    {
        if (MessageBox.Show(
                TextCatalog.Get("deleteActivityAsk"),
                TextCatalog.Get("product"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        LocalData.DeleteActivity(Paths.Root);
        Status = TextCatalog.Get("activityDeleted");
        _saved();
    }

    [RelayCommand]
    private void DeleteIgnoredList()
    {
        if (MessageBox.Show(
                TextCatalog.Get("deleteIgnoredAsk"),
                TextCatalog.Get("product"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _state.AllowList.Clear();
        LocalData.DeleteAllowList(Paths.Root);
        Ignored.Clear();
        Status = TextCatalog.Get("ignoredDeleted");
    }

    [RelayCommand]
    private void DeleteMaps()
    {
        if (MessageBox.Show(
                TextCatalog.Get("deleteMapsAsk"),
                TextCatalog.Get("product"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        LocalData.DeleteMaps(Paths.Root);
        Status = TextCatalog.Get("mapsDeleted");
    }

    [RelayCommand]
    private void DeleteAllData()
    {
        var root = Paths.Root;
        var message = LocalData.DescribeExceptSettings(root);
        if (MessageBox.Show(message, TextCatalog.Get("product"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _state.AllowList.Clear();
        LocalData.DeleteExceptSettings(root);
        Ignored.Clear();
        Status = TextCatalog.Get("dataDeleted");
        _saved();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Paths.EnsureDirectories();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = Paths.Root,
            UseShellExecute = true
        });
    }

    private void Load()
    {
        _loading = true;
        var settings = _state.Settings;
        StartWithWindows = settings.StartWithWindows;
        RememberFileLocations = settings.RememberFileLocations;
        WatchClipboard = settings.WatchClipboard;
        OnlyWhenTargetFocused = settings.OnlyWhenTargetFocused;
        TargetApps = string.Join(Environment.NewLine, settings.TargetProcesses);
        DefaultPasteAction = settings.DefaultPasteAction;
        StopBehavior = settings.StopBehavior;
        CopyNamePattern = settings.CopyNamePattern;
        CopyFolderMode = settings.CopyFolderMode;
        CopyFolder = settings.CopyFolder;
        MinHitsForPopup = settings.MinHitsForPopup;
        Theme = settings.Theme;
        UiLanguage = TextCatalog.NormalizeSetting(settings.UiLanguage);
        FillInterfaceLanguages();
        WatchCommands = settings.WatchCommands;
        WatchLinks = settings.WatchLinks;
        WatchCryptoSwap = settings.WatchCryptoSwap;
        RedactionMode = settings.RedactionMode;
        if (Profiles.Count == 0)
        {
            foreach (var profile in ProfileCatalog.LoadEmbedded())
                Profiles.Add(profile);
        }

        SelectedProfileId = string.IsNullOrWhiteSpace(settings.Profile) ? "general" : settings.Profile.Trim().ToLowerInvariant();
        MapRetentionDays = settings.MapRetentionDays is < 1 or > 3650 ? 30 : settings.MapRetentionDays;

        var disabled = new HashSet<string>(settings.DisabledRuleIds, StringComparer.OrdinalIgnoreCase);
        var forced = new HashSet<string>(settings.EnabledRuleIds, StringComparer.OrdinalIgnoreCase);
        Rules.Clear();
        foreach (var pack in _state.Packs)
        {
            foreach (var rule in pack.Rules)
            {
                var regionOn = CountryRegions.RuleIds(_state.Packs, _state.HomeRegion)
                    .Contains(rule.Id, StringComparer.OrdinalIgnoreCase);
                var defaultOn = (pack.Enabled && rule.Enabled) || regionOn;
                var isOn = !disabled.Contains(rule.Id) && (forced.Contains(rule.Id) || defaultOn);
                Rules.Add(new RuleToggle
                {
                    Id = rule.Id,
                    Label = RuleName(rule),
                    Category = string.IsNullOrWhiteSpace(rule.Category) ? "custom" : rule.Category,
                    Country = rule.CountryCode,
                    DefaultOn = defaultOn,
                    IsOn = isOn,
                    IsLocked = _state.Policy.LocksRule(rule.Id)
                });
            }
        }
        RebuildGroups();

        Terms.Clear();
        foreach (var term in UserDictionary.Load(Paths.UserRulesFile))
            Terms.Add(term);

        Ignored.Clear();
        foreach (var value in _state.AllowList.Hints)
            Ignored.Add(value);
        ReloadSites();
        SyncLocks();
        RefreshSelectedProfile();
        _loading = false;
        IsDirty = false;
        Status = "";
    }

    private void RefreshSelectedProfile()
    {
        var profile = ProfileCatalog.Find(SelectedProfileId);
        ProfileDescription = ProfileNames.Description(profile?.Id, profile?.Description);
        SuggestedTermsLine = profile is null || profile.SuggestedTerms.Count == 0
            ? ""
            : TextCatalog.Format("suggestedTerms", ("terms", string.Join("; ", profile.SuggestedTerms.Select(term =>
                string.IsNullOrWhiteSpace(term.Example) ? term.Label : term.Label + " — " + term.Example))));
        OnPropertyChanged(nameof(HasSuggestedTerms));
        OnPropertyChanged(nameof(CanAddSuggestedTerms));
    }

    private void ApplyLockedValues()
    {
        var settings = _state.Settings;
        var policy = _state.Policy;
        if (policy.Locks(PolicyKeys.WatchClipboard))
            WatchClipboard = settings.WatchClipboard;
        if (policy.Locks(PolicyKeys.RememberFileLocations))
            RememberFileLocations = settings.RememberFileLocations;
        if (policy.Locks(PolicyKeys.OnlyWhenTargetFocused))
            OnlyWhenTargetFocused = settings.OnlyWhenTargetFocused;
        if (policy.Locks(PolicyKeys.DefaultPasteAction))
            DefaultPasteAction = settings.DefaultPasteAction;
        if (policy.Locks(PolicyKeys.StopBehavior))
            StopBehavior = settings.StopBehavior;
        if (policy.Locks(PolicyKeys.MinHitsForPopup))
            MinHitsForPopup = settings.MinHitsForPopup;
        if (policy.Locks(PolicyKeys.WatchCommands))
            WatchCommands = settings.WatchCommands;
        if (policy.Locks(PolicyKeys.WatchLinks))
            WatchLinks = settings.WatchLinks;
        if (policy.Locks(PolicyKeys.WatchCryptoSwap))
            WatchCryptoSwap = settings.WatchCryptoSwap;
        if (policy.Locks(PolicyKeys.RedactionMode))
            RedactionMode = settings.RedactionMode;
    }

    private void SyncLocks()
    {
        var policy = _state.Policy;
        ClipboardEdit.CanEdit = !policy.Locks(PolicyKeys.WatchClipboard);
        FileLocationsEdit.CanEdit = !policy.Locks(PolicyKeys.RememberFileLocations);
        TargetFocusEdit.CanEdit = !policy.Locks(PolicyKeys.OnlyWhenTargetFocused);
        PasteActionEdit.CanEdit = !policy.Locks(PolicyKeys.DefaultPasteAction);
        StopEdit.CanEdit = !policy.Locks(PolicyKeys.StopBehavior);
        MinHitsEdit.CanEdit = !policy.Locks(PolicyKeys.MinHitsForPopup);
        CommandEdit.CanEdit = !policy.Locks(PolicyKeys.WatchCommands);
        LinkEdit.CanEdit = !policy.Locks(PolicyKeys.WatchLinks);
        SitesEdit.CanEdit = !policy.Locks(PolicyKeys.LockSites);
        CryptoEdit.CanEdit = !policy.Locks(PolicyKeys.WatchCryptoSwap);
        RedactionEdit.CanEdit = !policy.Locks(PolicyKeys.RedactionMode);
        ShowProfileLock = policy.Locks(PolicyKeys.Profile);
        ProfileEdit.CanEdit = !ShowProfileLock;
        OnPropertyChanged(nameof(HasSuggestedTerms));
        OnPropertyChanged(nameof(CanAddSuggestedTerms));
        ShowIgnoreLock = policy.LockIgnore;
        PolicyWarning = _state.PolicyWarning ?? "";
        OnPropertyChanged(nameof(ProfileLine));
    }

    private void RebuildGroups()
    {
        RuleGroups.Clear();
        var order = new (string Id, string Title, string Description)[]
        {
            ("personal", TextCatalog.Get("groupPersonal"), TextCatalog.Get("groupPersonalHint")),
            ("payment", TextCatalog.Get("groupPayment"), TextCatalog.Get("groupPaymentHint")),
            ("secrets", TextCatalog.Get("groupSecrets"), TextCatalog.Get("groupSecretsHint")),
            ("custom", TextCatalog.Get("groupTerms"), TextCatalog.Get("groupTermsHint")),
            ("hidden", TextCatalog.Get("groupHidden"), TextCatalog.Get("groupHiddenHint")),
            ("court-us", TextCatalog.Get("groupUs"), TextCatalog.Get("groupUsHint")),
            ("court-eu", TextCatalog.Get("groupEu"), TextCatalog.Get("groupEuHint"))
        };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, title, description) in order)
        {
            var rows = Rules.Where(rule =>
                string.IsNullOrEmpty(rule.Country) &&
                rule.Category.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rows.Count == 0)
                continue;
            used.Add(id);
            var group = new RuleGroup(title, description);
            foreach (var row in rows)
                group.Rules.Add(row);
            RuleGroups.Add(group);
        }
        foreach (var code in Rules.Select(rule => rule.Country).Where(code => code.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(code => CountryTitle(code), StringComparer.OrdinalIgnoreCase))
        {
            var rows = Rules.Where(rule => rule.Country.Equals(code, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rows.Count == 0)
                continue;
            var group = new RuleGroup(CountryTitle(code), CountryHint(code), "settings-country-" + code);
            foreach (var row in rows)
                group.Rules.Add(row);
            RuleGroups.Add(group);
        }
        var other = Rules.Where(rule => string.IsNullOrEmpty(rule.Country) && !used.Contains(rule.Category)).ToList();
        if (other.Count == 0)
            return;
        var extra = new RuleGroup(TextCatalog.Get("groupOther"), TextCatalog.Get("groupOtherHint"));
        foreach (var row in other)
            extra.Rules.Add(row);
        RuleGroups.Add(extra);
    }

    private void MarkDirty()
    {
        if (!_loading)
            IsDirty = true;
    }

    partial void OnStartWithWindowsChanged(bool value) => MarkDirty();
    partial void OnWatchClipboardChanged(bool value) => MarkDirty();
    partial void OnRememberFileLocationsChanged(bool value) => MarkDirty();
    partial void OnOnlyWhenTargetFocusedChanged(bool value) => MarkDirty();
    partial void OnThemeChanged(string value) => MarkDirty();
    partial void OnTargetAppsChanged(string value) => MarkDirty();
    partial void OnDefaultPasteActionChanged(string value) => MarkDirty();
    partial void OnStopBehaviorChanged(string value) => MarkDirty();
    partial void OnCopyNamePatternChanged(string value) => MarkDirty();
    partial void OnCopyFolderModeChanged(string value)
    {
        OnPropertyChanged(nameof(CopyBeside));
        OnPropertyChanged(nameof(CopyInFolder));
        MarkDirty();
    }
    partial void OnCopyFolderChanged(string value) => MarkDirty();
    partial void OnMinHitsForPopupChanged(int value) => MarkDirty();
    partial void OnWatchCommandsChanged(bool value) => MarkDirty();
    partial void OnWatchLinksChanged(bool value) => MarkDirty();
    partial void OnWatchCryptoSwapChanged(bool value) => MarkDirty();
    partial void OnRedactionModeChanged(string value) => MarkDirty();
    partial void OnMapRetentionDaysChanged(int value) => MarkDirty();

    partial void OnSelectedProfileIdChanged(string value)
    {
        RefreshSelectedProfile();
        var profile = ProfileCatalog.Find(value);
        if (_loading || !ProfileEdit.CanEdit || profile is null)
            return;

        if (PasteActionEdit.CanEdit)
            DefaultPasteAction = profile.DefaultPasteAction;
        if (RedactionEdit.CanEdit)
            RedactionMode = profile.RedactionMode;
        if (StopEdit.CanEdit)
            StopBehavior = profile.StopBehavior;
        if (LinkEdit.CanEdit)
            WatchLinks = profile.WatchLinks;

        var enabled = new HashSet<string>(profile.EnabledRuleIds, StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(profile.DisabledRuleIds, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
        {
            if (rule.IsLocked)
                continue;
            if (enabled.Contains(rule.Id))
                rule.IsOn = true;
            else if (disabled.Contains(rule.Id))
                rule.IsOn = false;
            else
                rule.IsOn = rule.DefaultOn;
        }

        MarkDirty();
    }

    private void FillInterfaceLanguages()
    {
        InterfaceLanguages.Clear();
        foreach (var info in TextCatalog.UserLanguages())
        {
            var label = string.IsNullOrWhiteSpace(info.NativeName) ? info.EnglishName : info.NativeName;
            if (string.IsNullOrWhiteSpace(label))
                label = "English";
            InterfaceLanguages.Add(new LanguageChoice { Code = "en", Label = label });
        }
        if (InterfaceLanguages.Count == 0)
            InterfaceLanguages.Add(new LanguageChoice { Code = "en", Label = "English" });
        _interfaceLanguage = "en";
        OnPropertyChanged(nameof(InterfaceLanguage));
    }

    private static string CountryTitle(string code)
    {
        var key = "country." + code;
        var text = TextCatalog.Get(key);
        return text.Equals(key, StringComparison.Ordinal) ? code : text;
    }

    private static string CountryHint(string code)
    {
        var key = "country." + code + ".hint";
        var text = TextCatalog.Get(key);
        return text.Equals(key, StringComparison.Ordinal) ? "" : text;
    }

    private static string RuleName(Rule rule)
    {
        var key = "name." + rule.Id;
        var text = TextCatalog.Get(key);
        return text.Equals(key, StringComparison.Ordinal) ? rule.Label : text;
    }
}

public sealed partial class SettingLock : ObservableObject
{
    [ObservableProperty] private bool _canEdit = true;

    public bool ShowLock => !CanEdit;

    partial void OnCanEditChanged(bool value) => OnPropertyChanged(nameof(ShowLock));
}

public static class ProfileNames
{
    public static string Display(string? id) => id?.Trim().ToLowerInvariant() switch
    {
        "developer" => TextCatalog.Get("profileDeveloper"),
        "lawyer" => TextCatalog.Get("profileLawyer"),
        "clinic" => TextCatalog.Get("profileClinic"),
        "accountant" => TextCatalog.Get("profileAccountant"),
        "hr" => TextCatalog.Get("profileHr"),
        "journalist" => TextCatalog.Get("profileJournalist"),
        "general" or "" or null => TextCatalog.Get("profileGeneral"),
        _ => id!.Trim()
    };

    public static string Description(string? id, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(id))
            return fallback ?? "";
        var key = "profile." + id.Trim().ToLowerInvariant() + ".body";
        var text = TextCatalog.Get(key);
        return text.Equals(key, StringComparison.Ordinal) ? fallback ?? "" : text;
    }
}

public sealed class LanguageChoice
{
    public required string Code { get; init; }
    public required string Label { get; init; }
}

public sealed class ActionChoice
{
    public ActionChoice(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }
    public string Label { get; }
}

public partial class RuleToggle : ObservableObject
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Category { get; init; } = "custom";
    public string Country { get; init; } = "";
    public bool DefaultOn { get; init; }
    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private bool _isLocked;

    public bool CanEdit => !IsLocked;
    public bool ShowLock => IsLocked;

    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowLock));
    }
}

public sealed class RuleGroup
{
    public RuleGroup(string title, string description, string automationId = "")
    {
        Title = title;
        Description = description;
        AutomationId = automationId;
    }

    public string Title { get; }
    public string Description { get; }
    public string AutomationId { get; }
    public ObservableCollection<RuleToggle> Rules { get; } = [];
}
