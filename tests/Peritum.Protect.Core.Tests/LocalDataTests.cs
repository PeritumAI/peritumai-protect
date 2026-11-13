using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Tests;

public class LocalDataTests
{
    [Fact]
    public void Data_commands_remove_only_what_they_name_and_keep_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-data-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        Directory.CreateDirectory(Path.Combine(root, "rules"));
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"theme\":\"dark\"}");
        File.WriteAllText(Path.Combine(root, "events-2026-09.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(root, "allowlist.json"), "{}");
        File.WriteAllText(Path.Combine(root, "maps", "abc.map"), "map");
        File.WriteAllText(Path.Combine(root, "rules", "user.json"), "{}");
        try
        {
            LocalData.DeleteActivity(root);
            Assert.False(File.Exists(Path.Combine(root, "events-2026-09.jsonl")));
            Assert.True(File.Exists(Path.Combine(root, "allowlist.json")));
            File.WriteAllText(Path.Combine(root, "events-2026-08.jsonl"), "{}\n");

            LocalData.DeleteAllowList(root);
            Assert.False(File.Exists(Path.Combine(root, "allowlist.json")));

            LocalData.DeleteMaps(root);
            Assert.False(File.Exists(Path.Combine(root, "maps", "abc.map")));

            var described = LocalData.DescribeExceptSettings(root);
            Assert.Contains("events-2026-08.jsonl", described, StringComparison.Ordinal);
            Assert.Contains("rules" + Path.DirectorySeparatorChar + "user.json", described, StringComparison.Ordinal);
        Assert.Contains("except settings.json", described, StringComparison.Ordinal);
        var listed = described.Split(Environment.NewLine).Skip(1);
        Assert.DoesNotContain(listed, line => line.Equals("settings.json", StringComparison.OrdinalIgnoreCase));

            LocalData.DeleteExceptSettings(root);
            Assert.True(File.Exists(Path.Combine(root, "settings.json")));
            Assert.Equal("{\"theme\":\"dark\"}", File.ReadAllText(Path.Combine(root, "settings.json")));
            Assert.Empty(LocalData.ExceptSettings(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
