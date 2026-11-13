using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap7bPeselTests
{
    [Fact]
    public void Published_pesels_and_every_century_offset_are_found()
    {
        Assert.Equal("44051401359", IndependentPesel(1944, 5, 14, 135));
        Assert.Equal("02070803628", IndependentPesel(1902, 7, 8, 362));
        Assert.Equal("50831500013", IndependentPesel(1850, 3, 15, 1));
        Assert.Equal("50231500015", IndependentPesel(2050, 3, 15, 1));
        Assert.Equal("50431500011", IndependentPesel(2150, 3, 15, 1));
        Assert.Equal("50631500017", IndependentPesel(2250, 3, 15, 1));
        Assert.Equal("00222900016", IndependentPesel(2000, 2, 29, 1));

        Assert.True(NationalId.ValidPesel("44051401359"));
        Assert.True(NationalId.ValidPesel("02070803628"));
        Assert.Equal("pl.pesel", Hit("44051401359"));
        Assert.Equal("pl.pesel", Hit("02070803628"));

        foreach (var year in new[] { 1850, 1944, 2050, 2150, 2250 })
        {
            var pesel = IndependentPesel(year, 3, 15, 1);
            Assert.True(NationalId.ValidPesel(pesel), pesel);
            Assert.Equal("pl.pesel", Hit(pesel));
        }

        Assert.True(NationalId.ValidPesel(IndependentPesel(2000, 2, 29, 1)));
        Assert.True(NationalId.ValidPesel(IndependentPesel(1904, 2, 29, 1)));
    }

    [Theory]
    [InlineData(1910, 5, 0, 1)]
    [InlineData(1910, 5, 32, 1)]
    [InlineData(1910, 2, 30, 1)]
    [InlineData(1900, 2, 29, 1)]
    [InlineData(1901, 2, 29, 1)]
    [InlineData(1910, 4, 31, 1)]
    public void Impossible_pesel_dates_are_rejected(int year, int month, int day, int serial)
    {
        var pesel = IndependentPesel(year, month, day, serial);
        Assert.False(NationalId.ValidPesel(pesel), pesel);
        Assert.Null(Hit(pesel));
    }

    private static string? Hit(string text)
    {
        var rule = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == "pl.pesel");
        var findings = new RuleScanner().Find("ID " + text + " end", [rule]);
        return findings.Count == 0 ? null : findings[0].RuleId;
    }

    /// <summary>
    /// Published PESEL layout: YY, month with century offset, DD, four-digit serial, then
    /// check digit from weights 1,3,7,9,1,3,7,9,1,3. Does not call <see cref="NationalId"/>.
    /// </summary>
    private static string IndependentPesel(int year, int month, int day, int serial)
    {
        var monthCode = (year / 100) switch
        {
            18 => month + 80,
            19 => month,
            20 => month + 20,
            21 => month + 40,
            22 => month + 60,
            _ => throw new ArgumentOutOfRangeException(nameof(year))
        };
        var body = $"{year % 100:00}{monthCode:00}{day:00}{serial:0000}";
        var weights = new[] { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
        var sum = 0;
        for (var i = 0; i < 10; i++)
            sum += (body[i] - '0') * weights[i];
        var check = (10 - (sum % 10)) % 10;
        return body + check;
    }
}
