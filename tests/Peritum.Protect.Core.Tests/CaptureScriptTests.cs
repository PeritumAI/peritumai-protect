namespace Peritum.Protect.Core.Tests;

public class CaptureScriptTests
{
    [Fact]
    public void Capture_script_writes_protected_copies_in_the_temporary_data_folder()
    {
        var root = Repo();
        var script = File.ReadAllText(Path.Combine(root, "tools", "capture-screens.ps1"));
        Assert.DoesNotContain("--protect $Fixture", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--scan $Fixture", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fixtures\\batch", script, StringComparison.Ordinal);
        Assert.Contains("$data", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item", script, StringComparison.Ordinal);
        Assert.Contains("ScrollPattern", script, StringComparison.Ordinal);
        Assert.Contains("layout-scale", script, StringComparison.Ordinal);
        Assert.Contains("ClipboardWarning", script, StringComparison.Ordinal);
        Assert.Contains("show-warning", script, StringComparison.Ordinal);
        Assert.Contains("stray numbered file", script, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "0")), "a stray file named 0 is in the repo root");
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tools", "capture-screens.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("capture-screens.ps1 was not found.");
    }
}
