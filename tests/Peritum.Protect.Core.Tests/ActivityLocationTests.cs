using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class ActivityLocationTests
{
    private const string Email = "ada@example.com";
    private const string Key = "sk-testFAKEkey1234567890";

    [Fact]
    public void Paths_are_stored_only_when_the_setting_is_on_and_delete_removes_them()
    {
        var root = Temp();
        try
        {
            var file = Path.Combine(root, "note.txt");
            File.WriteAllText(file, "Contact ada@example.com about the invoice.\n");
            var session = new ProtectService().Scan(ScanRequest.ForFile(file), Rules());
            var log = new EventLog(Path.Combine(root, "events.jsonl"));
            var copy = Path.Combine(root, "note_protect.txt");
            log.Append(log.FromScan(session.Result, EventActions.Warn, null, file, rememberLocation: true, copyPath: copy));
            var stored = log.ReadAll().Single();
            Assert.Equal(Path.GetFullPath(file), stored.FilePath, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(copy), stored.CopyPath, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(Email, File.ReadAllText(Directory.GetFiles(root, "events*.jsonl").Single()), StringComparison.Ordinal);

            var hidden = Temp();
            try
            {
                var quiet = new EventLog(Path.Combine(hidden, "events.jsonl"));
                quiet.Append(quiet.FromScan(session.Result, EventActions.Warn, null, file, rememberLocation: false));
                var kept = quiet.ReadAll().Single();
                Assert.Null(kept.FilePath);
                Assert.Null(kept.CopyPath);
                Assert.DoesNotContain(Email, File.ReadAllText(Directory.GetFiles(hidden, "events*.jsonl").Single()), StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(hidden, true);
            }

            LocalData.DeleteActivity(root);
            Assert.Empty(Directory.GetFiles(root, "events*.jsonl"));
            LocalData.DeleteExceptSettings(root);
            Assert.DoesNotContain(file, string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Policy_off_turns_remembered_locations_off()
    {
        var root = Temp();
        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            File.WriteAllText(policyPath, """{"rememberFileLocations":false}""");
            var policy = PolicyFile.Read(policyPath, null).Policy;
            var settings = new AppSettings { RememberFileLocations = true };
            policy.ApplyTo(settings);
            Assert.False(settings.RememberFileLocations);
            Assert.True(policy.Locks(PolicyKeys.RememberFileLocations));

            var user = new AppSettings();
            Assert.True(user.RememberFileLocations);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Moved_file_message_and_findings_csv_omit_values()
    {
        var root = Temp();
        try
        {
            var file = Path.Combine(root, "secrets.txt");
            File.WriteAllText(file, "Contact ada@example.com about the invoice.\nThe test key is sk-testFAKEkey1234567890.\n");
            var session = new ProtectService().Scan(ScanRequest.ForFile(file), Rules());
            Assert.Equal("", FileLocations.MissingMessage(file));
            var gone = Path.Combine(root, "missing", "gone.txt");
            Assert.Equal(FileLocations.Missing, FileLocations.MissingMessage(gone));
            Assert.Equal(root, FileLocations.ParentIfExists(Path.Combine(root, "gone.txt")));

            var csv = FileLocations.FindingsCsv(
            [
                FileLocations.FromScan(file, "TXT", "Found 2", session.Result.Findings)
            ]);
            Assert.Contains(file, csv, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("line 1, column", csv, StringComparison.Ordinal);
            Assert.DoesNotContain(Email, csv, StringComparison.Ordinal);
            Assert.DoesNotContain(Key, csv, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static IReadOnlyList<Rule> Rules() =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "peritum-locact-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
