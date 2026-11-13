using Peritum.Protect.App.Services;

namespace Peritum.Protect.App.Tests;

public class IconRasterTests
{
    [Fact]
    public void Raster_icon_bytes_are_a_valid_png_and_the_expected_ico_frames()
    {
        var png = IconRaster.Png(64, false, TrayState.On);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png.Take(4).ToArray());

        AssertFrames(IconRaster.AppIco(), [16, 20, 24, 32, 40, 48, 64, 256]);
        AssertFrames(IconRaster.TrayIco(TrayState.On), [16, 20, 24, 32]);
        AssertFrames(IconRaster.TrayIco(TrayState.Paused), [16, 20, 24, 32]);
        AssertFrames(IconRaster.TrayIco(TrayState.Warning), [16, 20, 24, 32]);
    }

    private static void AssertFrames(byte[] ico, int[] sizes)
    {
        Assert.True(ico.Length > 22);
        Assert.Equal(0, ico[0]);
        Assert.Equal(0, ico[1]);
        Assert.Equal(1, ico[2]);
        Assert.Equal(0, ico[3]);
        var count = BitConverter.ToUInt16(ico, 4);
        Assert.Equal(sizes.Length, (int)count);
        var found = new int[count];
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);
            var width = ico[entry];
            found[i] = width == 0 ? 256 : width;
            var length = BitConverter.ToInt32(ico, entry + 8);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            Assert.True(offset >= 0 && length > 8 && offset + length <= ico.Length);
            Assert.Equal(0x89, ico[offset]);
            Assert.Equal((byte)'P', ico[offset + 1]);
            Assert.Equal((byte)'N', ico[offset + 2]);
            Assert.Equal((byte)'G', ico[offset + 3]);
        }
        Assert.Equal(sizes, found);
    }
}
