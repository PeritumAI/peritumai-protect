using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Tests;

public class VersionAgeTests
{
    [Fact]
    public void Age_line_uses_the_injected_clock()
    {
        var built = new DateTime(2026, 1, 1);
        Assert.Equal("This version is 0 days old.", VersionAge.AgeLine(built, built));
        Assert.Equal("This version is 1 day old.", VersionAge.AgeLine(built, built.AddDays(1)));
        Assert.Equal("This version is 10 days old.", VersionAge.AgeLine(built, built.AddDays(10)));
        Assert.Equal("This version is 0 days old.", VersionAge.AgeLine(built, built.AddDays(-3)));
        Assert.False(VersionAge.ShowQuietLine(built, built.AddDays(180)));
        Assert.True(VersionAge.ShowQuietLine(built, built.AddDays(181)));
    }
}
