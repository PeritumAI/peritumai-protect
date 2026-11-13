using Peritum.Protect.App.Services;

namespace Peritum.Protect.App.Tests;

public class StartupShortcutRemovalTests
{
    [Fact]
    public void Removing_a_startup_shortcut_deletes_that_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var link = Path.Combine(dir, "PeritumAI Protect.lnk");
        try
        {
            File.WriteAllText(link, "shortcut");
            StartupShortcutService.Remove(link);
            Assert.False(File.Exists(link));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
