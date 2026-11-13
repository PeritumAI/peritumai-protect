using System.IO.Compression;
using System.Text;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Tests;

public class BatchScanTests
{
    [Fact]
    public void Limits_stop_at_500_files_and_depth_5_and_skip_hidden_files()
    {
        var root = NewDir();
        try
        {
            var hiddenDir = Path.Combine(root, "shown");
            Directory.CreateDirectory(hiddenDir);
            var hidden = Path.Combine(hiddenDir, "secret.txt");
            File.WriteAllText(hidden, "hidden");
            File.WriteAllText(Path.Combine(hiddenDir, "visible.txt"), "hello");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            var hiddenPlan = BatchScan.Collect([hiddenDir]);
            Assert.Equal(1, hiddenPlan.SkippedHiddenOrSystem);
            Assert.Contains(hiddenPlan.Files, file => file.Name == "visible.txt");
            Assert.DoesNotContain(hiddenPlan.Files, file => file.Name.Equals("secret.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("1 hidden or system file was skipped.", BatchScan.Notice(hiddenPlan), StringComparison.Ordinal);

            var many = Path.Combine(root, "many");
            Directory.CreateDirectory(many);
            for (var i = 0; i < 501; i++)
                File.WriteAllText(Path.Combine(many, "f" + i + ".txt"), "hello");
            var capped = BatchScan.Collect([many]);
            Assert.Equal(BatchScan.MaxFiles, capped.Files.Count);
            Assert.True(capped.HitFileLimit);
            Assert.Contains(BatchScan.FileLimit, BatchScan.Notice(capped), StringComparison.Ordinal);

            var deep = NewDir();
            try
            {
                var folder = deep;
                for (var level = 1; level <= 6; level++)
                {
                    folder = Path.Combine(folder, "d" + level);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "level" + level + ".txt"), "x");
                }

                var plan = BatchScan.Collect([deep]);
                Assert.Contains(plan.Files, file => file.Name == "level5.txt");
                Assert.DoesNotContain(plan.Files, file => file.Name == "level6.txt");
                Assert.True(plan.HitDepthLimit);
                Assert.Contains(BatchScan.DepthLimit, BatchScan.Notice(plan), StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(deep, true);
            }
        }
        finally
        {
            ClearAttributes(root);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Summary_counts_findings_and_files_that_were_not_checked()
    {
        var root = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "note.txt"), "Write to ada@example.com today");
            File.WriteAllText(Path.Combine(root, "clean.txt"), "hello");
            File.WriteAllText(Path.Combine(root, "old.rtf"), "legacy");
            var plan = BatchScan.Collect([root]);
            var summary = BatchScan.Run(plan.Files, new ProtectService(), Rules(), null, CancellationToken.None, null);
            Assert.Equal("1 of 3 files has findings, 1 finding in total", summary.Message);
            Assert.Equal(2, summary.Checked);
            Assert.Equal(1, summary.WithFindings);
            Assert.Equal(1, summary.NotChecked);
            Assert.Contains(plan.Files, file => file.Name == "note.txt" && file.Status == "1 finding");
            Assert.Contains(plan.Files, file => file.Name == "clean.txt" && file.Status == BatchScan.NothingFound);
            Assert.Contains(plan.Files, file => file.Name == "old.rtf" && file.Status.StartsWith("Not checked", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Cancel_stops_after_the_current_file()
    {
        var root = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "one");
            File.WriteAllText(Path.Combine(root, "b.txt"), "two");
            File.WriteAllText(Path.Combine(root, "c.txt"), "three");
            var plan = BatchScan.Collect([root]);
            var cancel = new CancellationTokenSource();
            var summary = BatchScan.Run(plan.Files, new ProtectService(), Rules(), null, cancel.Token, file =>
            {
                if (file.Status != BatchScan.Checking)
                    cancel.Cancel();
            });
            Assert.Equal(1, plan.Files.Count(file => file.Session is not null));
            Assert.Equal(2, plan.Files.Count(file => file.Status.Length == 0));
            Assert.Equal("0 of 1 file have findings, 0 findings in total", summary.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_folder_with_a_zip_bomb_finishes_and_marks_that_file_incomplete()
    {
        var root = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "hello.txt"), "hello");
            var bomb = Path.Combine(root, "bomb.zip");
            using (var archive = new ZipArchive(File.Create(bomb), ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("pad.txt", CompressionLevel.SmallestSize);
                using var writer = entry.Open();
                var chunk = Encoding.ASCII.GetBytes(new string('A', 8192));
                for (var i = 0; i < 40; i++)
                    writer.Write(chunk);
            }

            var plan = BatchScan.Collect([root]);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BatchSummary summary;
            try
            {
                summary = BatchScan.Run(plan.Files, new ProtectService(), Rules(), null, CancellationToken.None, null);
            }
            catch (Exception ex)
            {
                Assert.Fail("Exception escaped: " + ex);
                return;
            }

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Took " + clock.Elapsed);
            Assert.Contains(plan.Files, file => file.Name == "bomb.zip" && file.Status == BatchScan.Incomplete);
            Assert.Contains(plan.Files, file => file.Name == "hello.txt" && file.Status == BatchScan.NothingFound);
            Assert.Equal("0 of 2 files have findings, 0 findings in total", summary.Message);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(root, "hello.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static IReadOnlyList<Rule> Rules() =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "peritum-batch-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void ClearAttributes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
                // Best effort so the temp folder can be deleted.
            }
        }
    }
}
