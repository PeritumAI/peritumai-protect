using System.Text.Json;

namespace Peritum.Protect.Core.Config;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public bool StartWithWindows { get; set; } = true;
    public bool WatchClipboard { get; set; } = true;
    public bool OnlyWhenTargetFocused { get; set; } = true;
    public List<string> TargetProcesses { get; set; } = [.. TargetProcessList.Default];
    /// <summary>ask, ignore, stop, or redact. The popup still asks in V1. This picks the highlighted button.</summary>
    public string DefaultPasteAction { get; set; } = "ask";
    public string StopBehavior { get; set; } = "restore-previous";
    public string CopyNamePattern { get; set; } = "{name}_protect{ext}";
    /// <summary>beside or folder.</summary>
    public string CopyFolderMode { get; set; } = "beside";
    public string CopyFolder { get; set; } = "";
    public List<string> EnabledRuleIds { get; set; } = [];
    public List<string> DisabledRuleIds { get; set; } = [];
    public int MinHitsForPopup { get; set; } = 1;
    public bool ShowFirstRun { get; set; } = true;
    /// <summary>system, light, or dark.</summary>
    public string Theme { get; set; } = "system";
    /// <summary>reversible or permanent.</summary>
    public string RedactionMode { get; set; } = "reversible";
    public bool WatchCommands { get; set; } = true;
    /// <summary>Warn when a copied link uses a known phishing trick. Off until a profile, the first-run checkbox, or a policy turns it on.</summary>
    public bool WatchLinks { get; set; }
    public bool WatchCryptoSwap { get; set; }
    /// <summary>Profile id. general until the user or a policy chooses another.</summary>
    public string Profile { get; set; } = "general";
    /// <summary>How long restore maps are kept. Default 30 days.</summary>
    public int MapRetentionDays { get; set; } = 30;
    /// <summary>Store full paths of scanned files and copies in the activity log on this PC.</summary>
    public bool RememberFileLocations { get; set; } = true;
    /// <summary>The one-time reference-number tip has been dismissed.</summary>
    public bool FormatTipSeen { get; set; }
    /// <summary>Empty is English. qps-ploc and qps-rtl are developer modes. Other codes fall back to English.</summary>
    public string UiLanguage { get; set; } = "";

    public static AppSettings CreateDefault() => new();

    public AppSettings Copy() => new()
    {
        SchemaVersion = SchemaVersion,
        StartWithWindows = StartWithWindows,
        WatchClipboard = WatchClipboard,
        OnlyWhenTargetFocused = OnlyWhenTargetFocused,
        TargetProcesses = TargetProcesses is null ? [] : [.. TargetProcesses],
        DefaultPasteAction = DefaultPasteAction,
        StopBehavior = StopBehavior,
        CopyNamePattern = CopyNamePattern,
        CopyFolderMode = CopyFolderMode,
        CopyFolder = CopyFolder,
        EnabledRuleIds = EnabledRuleIds is null ? [] : [.. EnabledRuleIds],
        DisabledRuleIds = DisabledRuleIds is null ? [] : [.. DisabledRuleIds],
        MinHitsForPopup = MinHitsForPopup,
        ShowFirstRun = ShowFirstRun,
        Theme = Theme,
        RedactionMode = RedactionMode,
        WatchCommands = WatchCommands,
        WatchLinks = WatchLinks,
        WatchCryptoSwap = WatchCryptoSwap,
        Profile = Profile,
        MapRetentionDays = MapRetentionDays,
        RememberFileLocations = RememberFileLocations,
        FormatTipSeen = FormatTipSeen,
        UiLanguage = UiLanguage
    };
}

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        if (!File.Exists(_path))
            return AppSettings.CreateDefault();
        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Indented);
            if (settings is null)
                return AppSettings.CreateDefault();
            settings.TargetProcesses ??= [.. TargetProcessList.Default];
            if (settings.SchemaVersion < 3)
            {
                foreach (var process in TargetProcessList.Default)
                {
                    if (!settings.TargetProcesses.Contains(process, StringComparer.OrdinalIgnoreCase))
                        settings.TargetProcesses.Add(process);
                }
            }
            settings.SchemaVersion = 3;
            settings.EnabledRuleIds ??= [];
            settings.DisabledRuleIds ??= [];
            if (string.IsNullOrWhiteSpace(settings.CopyNamePattern))
                settings.CopyNamePattern = "{name}_protect{ext}";
            settings.CopyFolderMode = settings.CopyFolderMode == "folder" ? "folder" : "beside";
            settings.CopyFolder ??= "";
            if (settings.MinHitsForPopup < 1)
                settings.MinHitsForPopup = 1;
            settings.DefaultPasteAction = NormalizeAction(settings.DefaultPasteAction);
            settings.Theme = settings.Theme?.Trim().ToLowerInvariant() switch
            {
                "light" => "light",
                "dark" => "dark",
                _ => "system"
            };
            settings.RedactionMode = settings.RedactionMode == "permanent" ? "permanent" : "reversible";
            settings.StopBehavior = settings.StopBehavior == "clear-clipboard"
                ? "clear-clipboard"
                : "restore-previous";
            settings.Profile = string.IsNullOrWhiteSpace(settings.Profile) ? "general" : settings.Profile.Trim();
            settings.UiLanguage = Peritum.Protect.Core.TextCatalog.NormalizeSetting(settings.UiLanguage);
            if (settings.MapRetentionDays < 1 || settings.MapRetentionDays > 3650)
                settings.MapRetentionDays = 30;
            return settings;
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        settings.SchemaVersion = 3;
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Indented);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
            File.Replace(tmp, _path, null);
        else
            File.Move(tmp, _path);
    }

    private static string NormalizeAction(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ignore" => "ignore",
        "stop" => "stop",
        "redact" => "redact",
        "ask" => "ask",
        _ => "ask"
    };
}
