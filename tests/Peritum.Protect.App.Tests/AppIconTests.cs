using Peritum.Protect.App.Native;
using Peritum.Protect.App.Services;

namespace Peritum.Protect.App.Tests;

public class AppIconTests
{
    [Theory]
    [InlineData(1.0, 16, 32)]
    [InlineData(1.24, 16, 32)]
    [InlineData(1.25, 20, 32)]
    [InlineData(1.5, 20, 48)]
    [InlineData(2.0, 20, 48)]
    public void Icon_frames_follow_dpi(double scale, int small, int big)
    {
        Assert.Equal((small, big), AppIcon.FramesForDpi(scale));
    }

    [Fact]
    public void App_icon_exposes_the_small_and_big_frames()
    {
        var ico = IconRaster.AppIco();
        Assert.Equal(16, PngWidth(AppIcon.PngFrame(ico, 16)));
        Assert.Equal(20, PngWidth(AppIcon.PngFrame(ico, 20)));
        Assert.Equal(32, PngWidth(AppIcon.PngFrame(ico, 32)));
        Assert.Equal(48, PngWidth(AppIcon.PngFrame(ico, 48)));
    }

    [Fact]
    public void Startup_shortcut_sets_the_app_id_and_the_exe_icon()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-shortcut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "Peritum.Protect.exe");
            File.WriteAllBytes(exe, [0x4D, 0x5A]);
            var link = Path.Combine(dir, "PeritumAI Protect.lnk");
            StartupShortcutService.Create(link, exe, dir);
            Assert.True(File.Exists(link));
            Assert.Equal(exe, StartupShortcutService.IconLocation(link), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(AppUserModel.Id, StartupShortcutService.ShortcutAppUserModelId(link));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static int PngWidth(byte[] png)
    {
        Assert.Equal(0x89, png[0]);
        return (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
    }
}
