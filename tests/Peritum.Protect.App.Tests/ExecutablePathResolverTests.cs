using Peritum.Protect.App.Services;

namespace Peritum.Protect.App.Tests;

public class ExecutablePathResolverTests
{
    [Fact]
    public void Uses_published_executable_from_base_directory()
    {
        var dir = TempDir();
        try
        {
            var exe = Path.Combine(dir, "Peritum.Protect.exe");
            File.WriteAllBytes(exe, []);

            Assert.Equal(
                exe,
                ExecutablePathResolver.Resolve(dir, @"C:\Program Files\dotnet\dotnet.exe"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rejects_sdk_host_when_no_published_executable_exists()
    {
        var dir = TempDir();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ExecutablePathResolver.Resolve(dir, @"C:\Program Files\dotnet\dotnet.exe"));

            Assert.Contains("published app", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-exe-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
