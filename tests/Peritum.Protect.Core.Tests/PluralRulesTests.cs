using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Tests;

public class PluralRulesTests
{
    [Theory]
    [InlineData("en", 0, "other")]
    [InlineData("en", 1, "one")]
    [InlineData("en", 2, "other")]
    [InlineData("sr-Latn", 1, "one")]
    [InlineData("sr", 21, "one")]
    [InlineData("sr", 2, "few")]
    [InlineData("sr", 4, "few")]
    [InlineData("sr", 5, "other")]
    [InlineData("sr", 11, "other")]
    [InlineData("ru", 1, "one")]
    [InlineData("ru", 21, "one")]
    [InlineData("ru", 2, "few")]
    [InlineData("ru", 5, "many")]
    [InlineData("ru", 11, "many")]
    [InlineData("pl", 1, "one")]
    [InlineData("pl", 2, "few")]
    [InlineData("pl", 5, "many")]
    [InlineData("pl", 12, "many")]
    [InlineData("pl", 22, "few")]
    [InlineData("ar", 0, "zero")]
    [InlineData("ar", 1, "one")]
    [InlineData("ar", 2, "two")]
    [InlineData("ar", 3, "few")]
    [InlineData("ar", 11, "many")]
    [InlineData("ar", 100, "other")]
    public void Categories_follow_cldr(string language, long number, string category) =>
        Assert.Equal(category, PluralRules.Category(language, number));

    [Theory]
    [InlineData("ar", true)]
    [InlineData("he", true)]
    [InlineData("fa", true)]
    [InlineData("ur", true)]
    [InlineData("qps-rtl", true)]
    [InlineData("en", false)]
    [InlineData("qps-ploc", false)]
    public void Right_to_left_languages_are_named(string language, bool rtl) =>
        Assert.Equal(rtl, PluralRules.IsRightToLeft(language));

    [Fact]
    public void Pseudo_localization_is_accented_longer_and_bracketed()
    {
        var text = PseudoLoc.Apply("Check {count}");
        Assert.StartsWith("[", text, StringComparison.Ordinal);
        Assert.EndsWith("]", text, StringComparison.Ordinal);
        Assert.Contains("{count}", text, StringComparison.Ordinal);
        Assert.Contains('é', text);
        Assert.True(text.Length >= (int)Math.Ceiling("Check {count}".Length * 1.4), text);
    }
}
