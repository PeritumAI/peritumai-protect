using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class UserDictionaryTests
{
    [Fact]
    public void Exact_term_hits_and_roundtrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-user-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "user.json");
            UserDictionary.Save(path, [new UserTerm { Label = "ClientNames", Text = "ProjectNimbus" }]);
            var loaded = UserDictionary.Load(path);
            Assert.Equal("ProjectNimbus", Assert.Single(loaded).Text);

            var pack = RulePackLoader.LoadFile(path);
            var rules = RulePackLoader.SelectActive([pack], [], []);
            var text = "Send this to ProjectNimbus today.";
            var findings = new RuleScanner().Find(text, rules);
            var hit = Assert.Single(findings);
            Assert.Equal("CUSTOM:ClientNames", hit.Placeholder);
            Assert.Equal("Send this to [CUSTOM:ClientNames] today.", Redaction.TextRedactor.Apply(text, findings));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class EventAndSettingsTests
{
    [Fact]
    public void Event_log_stores_hash_and_counts_not_the_clipboard()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-log-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var secret = "ada@example.com sk-testFAKEkey1234567890";
            var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
            var session = new ProtectService().Scan(ScanRequest.ForText(secret, ScanSource.Clipboard), rules);
            var key = new byte[32];
            key[0] = 7;
            var log = new EventLog(Path.Combine(dir, "events.jsonl"), key);
            log.Append(log.FromScan(session.Result, EventActions.Warn, "chrome", null));
            var raw = string.Join('\n', Directory.GetFiles(dir, "events*.jsonl").Select(File.ReadAllText));
            Assert.Equal(Hashing.HmacText(session.Result.ContentHash, key), log.ReadRecent(1)[0].SnippetHash);
            Assert.DoesNotContain("ada@example.com", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-test", raw, StringComparison.Ordinal);
            Assert.DoesNotContain(session.Result.ContentHash, raw, StringComparison.Ordinal);
            Assert.Contains("\"snippetHash\"", raw, StringComparison.Ordinal);
            Assert.Contains("pii.email", raw, StringComparison.Ordinal);
            Assert.Contains("clipboard", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_roundtrip_uses_local_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-settings-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var settings = AppSettings.CreateDefault();
            settings.StartWithWindows = false;
            settings.DisabledRuleIds.Add("pii.email");
            settings.CopyNamePattern = "{name}_protect{ext}";
            settings.MinHitsForPopup = 3;
            store.Save(settings);
            var loaded = store.Load();
            Assert.False(loaded.StartWithWindows);
            Assert.Contains("pii.email", loaded.DisabledRuleIds);
            Assert.True(loaded.WatchClipboard);
            Assert.Equal("ask", loaded.DefaultPasteAction);
            Assert.Equal(3, loaded.MinHitsForPopup);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_normalizes_popup_threshold_to_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-settings-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, """{"schemaVersion":1,"minHitsForPopup":0}""");

            Assert.Equal(1, new SettingsStore(path).Load().MinHitsForPopup);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("ask")]
    [InlineData("ignore")]
    [InlineData("stop")]
    [InlineData("redact")]
    public void Settings_roundtrip_preserves_supported_paste_actions(string action)
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-settings-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var settings = AppSettings.CreateDefault();
            settings.DefaultPasteAction = action;
            store.Save(settings);

            Assert.Equal(action, store.Load().DefaultPasteAction);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Target_process_match_ignores_exe_suffix()
    {
        Assert.True(TargetProcessList.Matches("chrome", ["chrome.exe"]));
        Assert.False(TargetProcessList.Matches("notepad", TargetProcessList.Default));
    }

    [Theory]
    [InlineData("ms-teams")]
    [InlineData("slack")]
    [InlineData("copilot")]
    public void Default_targets_include_common_ai_destinations(string processName) =>
        Assert.True(TargetProcessList.Matches(processName, TargetProcessList.Default));
}
