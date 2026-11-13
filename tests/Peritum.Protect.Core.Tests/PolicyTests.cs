using Peritum.Protect.Core.Config;

namespace Peritum.Protect.Core.Tests;

public class PolicyTests
{
    [Fact]
    public void Policy_beats_user_settings_and_the_user_file_keeps_the_old_value()
    {
        using var dir = new TempDir();
        var path = dir.Write("policy.json", """{"watchClipboard":false,"disabledRuleIds":["pii.email"],"profile":"Lawyer"}""");
        var user = AppSettings.CreateDefault();
        user.DisabledRuleIds.Add("pii.phone");
        var result = PolicyFile.Read(path, registry: null);
        Assert.Null(result.Warning);
        var effective = result.Policy.ApplyCopy(user);

        Assert.False(effective.WatchClipboard);
        Assert.True(user.WatchClipboard);
        Assert.Contains("pii.email", effective.DisabledRuleIds);
        Assert.Contains("pii.phone", effective.DisabledRuleIds);
        Assert.DoesNotContain("pii.email", user.DisabledRuleIds);
        Assert.Equal("lawyer", effective.Profile);
        Assert.Equal("general", user.Profile);
        Assert.True(result.Policy.Locks(PolicyKeys.WatchClipboard));
        Assert.True(result.Policy.LocksRule("pii.email"));
        Assert.False(result.Policy.LocksRule("pii.phone"));

        var store = new SettingsStore(dir.File("settings.json"));
        store.Save(user);
        Assert.True(store.Load().WatchClipboard);
    }

    [Fact]
    public void Invalid_file_is_ignored_completely()
    {
        using var dir = new TempDir();
        var path = dir.Write("policy.json", """{"watchClipboard":false,"defaultPasteAction":"dance"}""");
        var result = PolicyFile.Read(path, registry: null);
        var settings = AppSettings.CreateDefault();
        result.Policy.ApplyTo(settings);

        Assert.Equal(PolicyWarnings.File, result.Warning);
        Assert.True(settings.WatchClipboard);
        Assert.False(result.Policy.Locks(PolicyKeys.WatchClipboard));
        Assert.Null(PolicyFile.Load(path));
    }

    [Fact]
    public void Unknown_key_null_and_oversized_files_are_rejected()
    {
        using var dir = new TempDir();
        Assert.Equal(PolicyWarnings.File, PolicyFile.Read(dir.Write("a.json", """{"watchClipboard":false,"extra":1}"""), null).Warning);
        Assert.Equal(PolicyWarnings.File, PolicyFile.Read(dir.Write("b.json", """{"watchClipboard":null}"""), null).Warning);
        Assert.Equal(PolicyWarnings.File, PolicyFile.Read(dir.Write("c.json", "{"), null).Warning);
        var huge = dir.File("huge.json");
        File.WriteAllText(huge, "{" + new string(' ', PolicyPaths.MaxBytes) + "}");
        Assert.Equal(PolicyWarnings.File, PolicyFile.Read(huge, null).Warning);
    }

    [Fact]
    public void Registry_wins_per_key_and_an_invalid_registry_does_not_drop_a_valid_file()
    {
        using var dir = new TempDir();
        var path = dir.Write("policy.json", """{"watchClipboard":false,"disabledRuleIds":["pii.email"],"defaultPasteAction":"stop"}""");
        var registry = Snapshot(
            Dword("watchClipboard", 1),
            Multi("disabledRuleIds", "pii.phone"));
        var merged = PolicyFile.Read(path, registry);
        Assert.Null(merged.Warning);
        Assert.Equal(true, merged.Policy.WatchClipboard);
        Assert.Equal(["pii.phone"], merged.Policy.DisabledRuleIds);
        Assert.DoesNotContain("pii.email", merged.Policy.LockedRuleIds);
        Assert.Equal("stop", merged.Policy.DefaultPasteAction);

        var broken = PolicyFile.Read(path, Snapshot(Dword("watchClipboard", 2), Dword("minHitsForPopup", 3)));
        Assert.Equal(PolicyWarnings.Registry, broken.Warning);
        Assert.Equal(false, broken.Policy.WatchClipboard);
        Assert.Contains("pii.email", broken.Policy.LockedRuleIds);
    }

    [Fact]
    public void Registry_rule_list_replaces_a_conflicting_file_rule()
    {
        using var dir = new TempDir();
        var path = dir.Write("policy.json", """{"enabledRuleIds":["pii.email"]}""");
        var result = PolicyFile.Read(path, Snapshot(Multi("disabledRuleIds", "pii.email")));
        Assert.Null(result.Warning);
        Assert.DoesNotContain("pii.email", result.Policy.EnabledRuleIds!);
        Assert.Contains("pii.email", result.Policy.DisabledRuleIds!);
        Assert.True(result.Policy.LocksRule("pii.email"));
    }

    [Fact]
    public void Both_invalid_sources_produce_one_warning_and_change_nothing()
    {
        using var dir = new TempDir();
        var path = dir.Write("policy.json", """{"profile":"../x"}""");
        var result = PolicyFile.Read(path, Snapshot(Text("defaultPasteAction", "dance")));
        var settings = AppSettings.CreateDefault();
        result.Policy.ApplyTo(settings);

        Assert.Equal(PolicyWarnings.Both, result.Warning);
        Assert.Equal("general", settings.Profile);
        Assert.Equal("ask", settings.DefaultPasteAction);
    }

    [Fact]
    public void Missing_policy_is_silent_and_lock_ignore_only_locks_when_true()
    {
        var missing = PolicyFile.Read(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("n"), "policy.json"), null);
        Assert.Null(missing.Warning);
        Assert.False(missing.Policy.LockIgnore);

        using var dir = new TempDir();
        var off = PolicyFile.Read(dir.Write("policy.json", """{"lockIgnore":false}"""), null);
        Assert.False(off.Policy.LockIgnore);
        Assert.False(off.Policy.Locks(PolicyKeys.LockIgnore));

        var on = PolicyFile.Read(dir.Write("on.json", """{"lockIgnore":true,"enabledRuleIds":["pii.ipv4"],"disabledRuleIds":["pii.ipv4"]}"""), null);
        Assert.Equal(PolicyWarnings.File, on.Warning);
    }

    [Fact]
    public void Overlapping_rule_lists_and_bad_ids_reject_the_whole_file()
    {
        using var dir = new TempDir();
        var overlap = PolicyFile.Read(dir.Write("policy.json", """{"enabledRuleIds":["pii.email"],"disabledRuleIds":["PII.Email"]}"""), null);
        Assert.Equal(PolicyWarnings.File, overlap.Warning);

        var badId = PolicyFile.Read(dir.Write("bad.json", """{"disabledRuleIds":["","pii.email"]}"""), null);
        Assert.Equal(PolicyWarnings.File, badId.Warning);
        Assert.Empty(badId.Policy.LockedRuleIds);
    }

    [Fact]
    public void User_rule_edits_keep_locked_ids_and_update_the_rest()
    {
        var user = AppSettings.CreateDefault();
        user.DisabledRuleIds.Add("pii.email");
        user.EnabledRuleIds.Add("pii.ipv4");
        PolicyFile.KeepUserRules(
            user,
            [
                new RuleChoice("pii.email", false, true),
                new RuleChoice("pii.phone", false, true),
                new RuleChoice("pii.ipv4", true, false)
            ],
            new HashSet<string>(["pii.email"], StringComparer.OrdinalIgnoreCase));

        Assert.Contains("pii.email", user.DisabledRuleIds);
        Assert.Contains("pii.phone", user.DisabledRuleIds);
        Assert.Contains("pii.ipv4", user.EnabledRuleIds);
    }

    [Fact]
    public void A_policy_file_change_is_reported()
    {
        using var dir = new TempDir();
        var path = dir.Write("policy.json", "{}");
        using var watcher = new PolicyFileWatcher(path);
        using var seen = new ManualResetEventSlim(false);
        watcher.Changed += () => seen.Set();
        watcher.Start();
        Thread.Sleep(150);
        File.WriteAllText(path, """{"watchClipboard":false}""");
        Assert.True(seen.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Default_policy_path_is_program_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Peritum",
            "Protect",
            "policy.json");
        Assert.Equal(expected, PolicyPaths.DefaultFile);
        Assert.Equal(@"Software\Policies\Peritum\Protect", PolicyPaths.RegistrySubKey);
    }

    private static PolicyRegistrySnapshot Snapshot(params PolicyRegistryValue[] values) => new() { Values = values };

    private static PolicyRegistryValue Dword(string name, int number) => new()
    {
        Name = name,
        Kind = PolicyValueKind.Dword,
        Number = number
    };

    private static PolicyRegistryValue Text(string name, string text) => new()
    {
        Name = name,
        Kind = PolicyValueKind.String,
        Text = text
    };

    private static PolicyRegistryValue Multi(string name, params string[] lines) => new()
    {
        Name = name,
        Kind = PolicyValueKind.MultiString,
        Lines = lines
    };

    private sealed class TempDir : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "peritum-policy-" + Guid.NewGuid().ToString("n"));

        public TempDir() => Directory.CreateDirectory(_path);

        public string File(string name) => Path.Combine(_path, name);

        public string Write(string name, string text)
        {
            var path = File(name);
            System.IO.File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
